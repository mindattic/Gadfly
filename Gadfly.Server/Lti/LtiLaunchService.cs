using System.Security.Cryptography;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Lti;

/// <summary>A launch validated end to end, or the plain-English reason it was refused.</summary>
public sealed record LtiLaunchResult(bool Ok, string? Error, LtiRegistration? Registration = null, string? MessageType = null,
    string? TargetLinkUri = null, LtiLaunchClaims? Claims = null)
{
    public static LtiLaunchResult Fail(string error) => new(false, error);
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
        // posted into this browser would sign it in as them).
        if (string.IsNullOrEmpty(browserState) || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(browserState), System.Text.Encoding.UTF8.GetBytes(state)))
            return LtiLaunchResult.Fail("This browser did not start this launch, or it blocked the cookie Gadfly uses to check that. "
                + "Open the exam from the LMS again. If this keeps happening, ask your instructor to set the Gadfly link to open in a new window, "
                + "or allow cookies for this site.");

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
        return new LtiLaunchResult(true, null, reg, validation.MessageType, loginState.TargetLinkUri, validation.Claims);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
