using System.Security.Cryptography;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

/// <summary>A launch validated end to end, or the plain-English reason it was refused.</summary>
public sealed record LtiLaunchResult(bool Ok, string? Error, LtiRegistration? Registration = null, string? MessageType = null,
    string? TargetLinkUri = null, LtiLaunchClaims? Claims = null)
{
    public static LtiLaunchResult Fail(string error) => new(false, error);

    /// <summary>
    /// GAD-T458: the launch was genuine (signature, nonce, deployment all checked) but this browser didn't send back the state
    /// cookie, as browsers that block every cookie inside a frame do. It is not accepted here (that check is what stops a
    /// launch being posted into someone else's browser); the person is offered "open in a new window" instead, which starts
    /// the login again where the cookie works. Null when the platform's hints weren't kept, so it can't be restarted.
    /// </summary>
    public LtiRestart? CookieMissingRestart { get; init; }
    public bool CookieMissing { get; init; }
}

/// <summary>What <c>/lti/login</c> needs to start the same launch again (GAD-T458), as the platform first sent it.</summary>
public sealed record LtiRestart(string Iss, string ClientId, string DeploymentId, string LoginHint, string? LtiMessageHint, string? TargetLinkUri)
{
    /// <summary>The fields of a GET form to <c>/lti/login</c>, in order.</summary>
    public IEnumerable<(string Name, string Value)> Fields()
    {
        yield return ("iss", Iss);
        yield return ("client_id", ClientId);
        yield return ("lti_deployment_id", DeploymentId);
        yield return ("login_hint", LoginHint);
        if (!string.IsNullOrEmpty(LtiMessageHint)) yield return ("lti_message_hint", LtiMessageHint);
        if (!string.IsNullOrEmpty(TargetLinkUri)) yield return ("target_link_uri", TargetLinkUri);
    }
}

/// <summary>
/// The OIDC third-party-initiated login and the launch it leads to (GAD-T312/GAD-T380). <c>/lti/login</c> redirects
/// the browser to the platform's own authorization endpoint with a fresh <c>state</c> and <c>nonce</c> recorded in
/// <see cref="LtiLoginState"/> (and the state also set in a browser cookie by the endpoint, so a launch is only
/// accepted in the browser that started it); <c>/lti/launch</c> is where the platform posts back a signed
/// <c>id_token</c>, which <see cref="LtiTokenValidator"/> checks against the platform's JWKS.
/// <para>Runs in a system-tenant scope (the endpoint opens it): the caller is not signed in to Gadfly yet, so the
/// registration lookup by issuer must see every organization. Anything written for an organization happens later,
/// in a scope pinned to that organization (<see cref="LtiUserProvisioner"/>, the endpoint's placement work).</para>
/// </summary>
public sealed class LtiLaunchService(GadflyDbContext db, LtiPlatformKeys platformKeys, TimeProvider clock, ILogger<LtiLaunchService> logger)
{
    /// <summary>How long the browser has between <c>/lti/login</c> and the platform posting the launch back.</summary>
    public static readonly TimeSpan LoginLifetime = TimeSpan.FromMinutes(10);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>A uniformly random token over [A-Za-z0-9] (rejection sampling, so no character is likelier than another).</summary>
    public static string RandomToken(int length = 48)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    // ================================================================== login initiation

    public sealed record LoginRequest(string Iss, string? ClientId, string? LoginHint, string? LtiMessageHint, string? TargetLinkUri, string? LtiDeploymentId);

    public sealed record LoginStart(string? RedirectUrl, string? State, string? Error);

    /// <summary>The platform's authorization-endpoint URL to redirect the browser to (and the state the endpoint binds to
    /// the browser), or the plain-English reason it can't.</summary>
    public async Task<LoginStart> BeginLoginAsync(LoginRequest req, string launchRedirectUri, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Iss) || string.IsNullOrWhiteSpace(req.LoginHint))
            return new(null, null, "This link is missing information an LTI platform is required to send (iss and login_hint).");
        var candidates = await db.LtiRegistrations.AsNoTracking().Where(r => r.Enabled && r.Issuer == req.Iss).ToListAsync(ct);
        // Narrow by client id, then by deployment id, whichever the platform sent; a single remaining row is the match.
        IEnumerable<LtiRegistration> matching = candidates;
        if (!string.IsNullOrEmpty(req.ClientId)) matching = matching.Where(r => r.ClientId == req.ClientId);
        if (!string.IsNullOrEmpty(req.LtiDeploymentId)) matching = matching.Where(r => r.DeploymentId == req.LtiDeploymentId);
        var list = matching.ToList();
        if (list.Count != 1)
            return new(null, null, list.Count > 1
                ? "More than one Gadfly connection matches this platform, and it did not say which (no client_id or lti_deployment_id). Ask an organization admin to check the LMS connection page."
                : "This platform is not connected to Gadfly, or its connection is turned off. Ask an organization admin to set it up on Gadfly's LMS connection page.");
        var reg = list[0];

        var now = clock.GetUtcNow();
        var state = RandomToken();
        var nonce = RandomToken(32);
        db.LtiLoginStates.Add(new LtiLoginState
        {
            State = state, OrganizationId = reg.OrganizationId, RegistrationId = reg.Id, Nonce = nonce,
            TargetLinkUri = Truncate(req.TargetLinkUri ?? "", 2000), CreatedAt = now, ExpiresAt = now + LoginLifetime,
            // Kept only for the new-window restart (GAD-T458); a hint too long to keep just means no restart button.
            LoginHint = req.LoginHint is { Length: <= 1000 } h ? h : null,
            LtiMessageHint = req.LtiMessageHint is { Length: > 0 and <= 4000 } m ? m : null,
        });
        await db.SaveChangesAsync(ct);
        // Opportunistic cleanup of abandoned logins (no separate sweeper: this table only ever holds minutes of rows).
        await db.LtiLoginStates.Where(s => s.ExpiresAt < now.AddMinutes(-5)).ExecuteDeleteAsync(ct);

        var query = new Dictionary<string, string?>
        {
            ["scope"] = "openid",
            ["response_type"] = "id_token",
            ["client_id"] = reg.ClientId,
            ["redirect_uri"] = launchRedirectUri,
            ["login_hint"] = req.LoginHint,
            ["state"] = state,
            ["response_mode"] = "form_post",
            ["nonce"] = nonce,
            ["prompt"] = "none",
            ["lti_message_hint"] = string.IsNullOrEmpty(req.LtiMessageHint) ? null : req.LtiMessageHint,
        };
        return new(WithQuery(reg.AuthLoginUrl, query), state, null);
    }

    public static string WithQuery(string baseUrl, IReadOnlyDictionary<string, string?> q)
    {
        var parts = q.Where(kv => kv.Value is not null).Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        return baseUrl + (baseUrl.Contains('?') ? '&' : '?') + string.Join('&', parts);
    }

    // ================================================================== launch validation

    /// <param name="browserState">The state the browser's own cookie holds (set at <c>/lti/login</c>); must equal <paramref name="state"/>.</param>
    public async Task<LtiLaunchResult> ValidateLaunchAsync(string? idToken, string? state, string? browserState, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(state))
            return LtiLaunchResult.Fail("This is not a valid LTI launch (no id_token or state).");

        // Consume the state first, atomically: exactly one request can flip Used from false to true, so a captured
        // launch posted twice (or twice at once) gets in at most once.
        var now = clock.GetUtcNow();
        var claimed = await db.LtiLoginStates.Where(s => s.State == state && !s.Used && s.ExpiresAt >= now)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Used, true), ct);
        var loginState = await db.LtiLoginStates.AsNoTracking().FirstOrDefaultAsync(s => s.State == state, ct);
        if (claimed != 1 || loginState is null)
            return LtiLaunchResult.Fail("This launch has expired or was already used. Open the exam from the LMS again.");

        // The browser that posts the launch must be the one that started it (login CSRF: someone else's launch
        // posted into this browser would sign it in as them). A cookie that came back different is refused outright; one that
        // didn't come back at all is usually a browser blocking every cookie inside the LMS's frame (GAD-T458): the launch is
        // still verified below, then the person is offered a new window instead of being signed in here.
        var cookieMissing = string.IsNullOrEmpty(browserState);
        if (!cookieMissing && !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(browserState!), System.Text.Encoding.UTF8.GetBytes(state)))
            return LtiLaunchResult.Fail("This browser did not start this launch. Open the exam from the LMS again. If this keeps happening, "
                + "ask your instructor to set the Gadfly link to open in a new window, or allow cookies for this site.");

        var reg = await db.LtiRegistrations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == loginState.RegistrationId && r.Enabled, ct);
        if (reg is null) return LtiLaunchResult.Fail("This platform's connection to Gadfly no longer exists or has been turned off.");

        var expect = new LtiExpectations(reg.Issuer, reg.ClientId, reg.DeploymentId, loginState.Nonce);
        LtiTokenValidation validation;
        try
        {
            validation = await LtiTokenValidator.ValidateAsync(idToken, expect, await platformKeys.KeysAsync(reg.KeySetUrl, ct));
            if (validation.SigningKeyNotFound)
            {
                // The platform may have just rotated its key: fetch its key set once more, then decide.
                platformKeys.Evict(reg.KeySetUrl);
                validation = await LtiTokenValidator.ValidateAsync(idToken, expect, await platformKeys.KeysAsync(reg.KeySetUrl, ct));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException
                                      or Microsoft.IdentityModel.Tokens.SecurityTokenException)
        {
            logger.LogWarning(e, "LTI launch: could not read the platform key set for registration {RegistrationId}", reg.Id);
            return LtiLaunchResult.Fail("Gadfly could not read the platform's key set to verify this launch. Try again in a minute; if it keeps failing, an organization admin should check the key-set (JWKS) URL on the LMS connection page.");
        }
        if (!validation.Ok)
        {
            logger.LogInformation("LTI launch refused for registration {RegistrationId}: {Reason}", reg.Id, validation.Error);
            return LtiLaunchResult.Fail(validation.Error!);
        }
        if (cookieMissing)
        {
            logger.LogInformation("LTI launch for registration {RegistrationId} came back without its state cookie; offering a new window.", reg.Id);
            return new LtiLaunchResult(false, "Your browser blocked the cookie Gadfly uses to check that this launch started in this browser, "
                + "which some browsers do for every site shown inside another page.")
            {
                CookieMissing = true,
                CookieMissingRestart = loginState.LoginHint is { Length: > 0 } hint
                    ? new LtiRestart(reg.Issuer, reg.ClientId, reg.DeploymentId, hint, loginState.LtiMessageHint, NullIfEmpty(loginState.TargetLinkUri))
                    : null,
            };
        }
        return new LtiLaunchResult(true, null, reg, validation.MessageType, loginState.TargetLinkUri, validation.Claims);
    }

    // ================================================================== new-window hand-off (GAD-T458)

    /// <summary>How long the "open the exam in a new window" button works.</summary>
    public static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(2);

    private const string HandoffPrefix = "handoff:";

    /// <summary>
    /// Records a new-window hand-off token as unused, in the database (the same table as login states, which an instance
    /// restart or a second instance both see), so <see cref="ClaimHandoffAsync"/> lets it through exactly once anywhere.
    /// </summary>
    public async Task RecordHandoffAsync(string handoffId, Guid organizationId, Guid registrationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        db.LtiLoginStates.Add(new LtiLoginState
        {
            State = HandoffPrefix + handoffId, OrganizationId = organizationId, RegistrationId = registrationId, Nonce = "",
            TargetLinkUri = "", CreatedAt = now, ExpiresAt = now + HandoffLifetime,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>True for exactly one caller per hand-off token, on any instance, while it is fresh: an atomic flip of its row.</summary>
    public async Task<bool> ClaimHandoffAsync(string handoffId, Guid organizationId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(handoffId) || handoffId.Length > 60) return false;
        var key = HandoffPrefix + handoffId;
        var now = clock.GetUtcNow();
        return await db.LtiLoginStates.Where(s => s.State == key && s.OrganizationId == organizationId && !s.Used && s.ExpiresAt >= now)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Used, true), ct) == 1;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
