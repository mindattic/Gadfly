using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gadfly.Server.Lti;

/// <summary>What the exam picker carries from the deep-linking launch to the response it eventually submits back to the
/// platform (GAD-T312/GAD-T381): a short-lived, tamper-proof token rather than a database row, since the picker is a
/// one-shot, few-minutes interaction.</summary>
public sealed record DeepLinkContinuation(Guid OrganizationId, Guid RegistrationId, Guid UserId, string DeepLinkReturnUrl, string? Data, bool AcceptMultiple);

/// <summary>A rendered LTI page and the headers it needs (who may frame it, where it may post, its script nonce).</summary>
public sealed record LtiPage(string Html, string FrameAncestors, string? FormAction = null, string? ScriptNonce = null);

/// <summary>
/// LTI Deep Linking 2.0 (GAD-T312/GAD-T381): when an instructor adds Gadfly content to a course, the platform sends an
/// <c>LtiDeepLinkingRequest</c> launch instead of the usual resource-link one. Gadfly shows a plain picker of the
/// organization's published exams, then POSTs a signed <c>LtiDeepLinkingResponse</c> back to the platform's
/// <c>deep_link_return_url</c> naming one <c>ltiResourceLink</c> content item per exam chosen. Each item carries the exam
/// id in its <c>custom</c> parameters (so every later launch of the placement knows its exam) and a <c>lineItem</c>
/// (so platforms that support it create the gradebook column the score is passed back to).
/// <para>Only course staff (an LTI role that maps to Instructor) get the picker. When the organization limits
/// instructors to their own exams (decision D18), the picker lists only exams this person created.</para>
/// </summary>
public sealed class LtiDeepLinking(GadflyDbContext db, LtiKeys keys, IDataProtectionProvider protection, TimeProvider clock)
{
    public static readonly TimeSpan ContinuationLifetime = TimeSpan.FromMinutes(30);

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("Gadfly.Lti.DeepLink").ToTimeLimitedDataProtector();

    public string ProtectContinuation(DeepLinkContinuation c) =>
        Protector.Protect(JsonSerializer.Serialize(c), clock.GetUtcNow() + ContinuationLifetime);

    public DeepLinkContinuation? UnprotectContinuation(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try { return JsonSerializer.Deserialize<DeepLinkContinuation>(Protector.Unprotect(token)); }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return null; }
    }

    /// <summary>The picker page: every published exam the person may add, radio or checkbox per the platform's own
    /// <c>accept_multiple</c>, posting to <c>/lti/deep-link/submit</c>.</summary>
    public async Task<LtiPage> PickerAsync(LtiRegistration reg, LtiLaunchClaims claims, Guid userId, CancellationToken ct)
    {
        var settings = claims.DeepLinkingSettings;
        var returnUrl = LtiTokenValidator.Str(settings, "deep_link_return_url");
        var origin = LtiPages.OriginOf(returnUrl);
        if (origin is null)
            return Error("The platform's content-picker request named no valid (https) return address, so Gadfly has nowhere to send your choice.");
        var frame = origin;
        if (claims.GadflyRole != OrgRoles.Instructor)
            return Error("Only course instructors can add a Gadfly exam to a course.", frame);
        if (settings is { } s && s.TryGetProperty("accept_types", out var types) && types.ValueKind == JsonValueKind.Array
            && !types.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "ltiResourceLink"))
            return Error("This spot in the course cannot hold a Gadfly exam link (the platform does not accept a resource link here). Add Gadfly as an assignment or a link instead.", frame);

        var data = LtiTokenValidator.Str(settings, "data");
        var acceptMultiple = settings is { } s3 && s3.TryGetProperty("accept_multiple", out var am) && am.ValueKind == JsonValueKind.True;

        var scoped = OwnershipPolicy.InstructorsScoped(db, reg.OrganizationId);
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == reg.OrganizationId && e.Status == ExamStatus.Published && (!scoped || e.CreatedByUserId == userId))
            .OrderBy(e => e.Title).Select(e => new { e.Id, e.Title, e.Subject, e.Topic }).ToListAsync(ct);

        var continuation = ProtectContinuation(new DeepLinkContinuation(reg.OrganizationId, reg.Id, userId, returnUrl!, data, acceptMultiple));
        var sb = new StringBuilder();
        sb.Append("<h1>Add a Gadfly exam</h1>")
          .Append("<p>Choose the exam to add to this course. Students who open it go straight into Gadfly; their grade is sent back here once it is released.</p>");
        if (exams.Count == 0)
        {
            sb.Append("<p class=\"notice\">")
              .Append(scoped
                  ? "You have no published exams yet. Your organization shows instructors only the exams they built: publish one of yours in Gadfly first, then add it here."
                  : "This organization has no published exams yet. Publish one in Gadfly first, then add it here.")
              .Append("</p>");
            return new LtiPage(LtiPages.Document("Add a Gadfly exam", sb.ToString()), frame);
        }
        var inputType = acceptMultiple ? "checkbox" : "radio";
        sb.Append("<form method=\"post\" action=\"/lti/deep-link/submit\">")
          .Append("<input type=\"hidden\" name=\"continuation\" value=\"").Append(LtiPages.Encode(continuation)).Append("\" />")
          .Append("<fieldset style=\"border:0;padding:0;margin:0\"><legend class=\"hint\">")
          .Append(acceptMultiple ? "Choose one or more exams." : "Choose one exam.").Append("</legend>");
        foreach (var e in exams)
        {
            var subtitle = string.Join(" · ", new[] { e.Subject, e.Topic }.Where(x => !string.IsNullOrWhiteSpace(x)));
            sb.Append("<label class=\"pick\"><input type=\"").Append(inputType).Append("\" name=\"examId\" value=\"").Append(e.Id).Append("\"")
              .Append(acceptMultiple ? "" : " required").Append(" />")
              .Append("<span><strong>").Append(LtiPages.Encode(e.Title)).Append("</strong>")
              .Append(subtitle.Length > 0 ? " <span class=\"hint\">" + LtiPages.Encode(subtitle) + "</span>" : "").Append("</span></label>");
        }
        sb.Append("</fieldset><button type=\"submit\">Add to course</button></form>");
        return new LtiPage(LtiPages.Document("Add a Gadfly exam", sb.ToString()), frame);
    }

    /// <summary>Builds the signed <c>LtiDeepLinkingResponse</c> and the auto-submitting form that posts it to the platform.</summary>
    public async Task<LtiPage> ResponseAsync(DeepLinkContinuation continuation, IReadOnlyList<Guid> examIds, string launchUrl, CancellationToken ct)
    {
        var frame = LtiPages.OriginOf(continuation.DeepLinkReturnUrl) ?? "'none'";
        var reg = await db.LtiRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == continuation.RegistrationId && r.OrganizationId == continuation.OrganizationId && r.Enabled, ct);
        if (reg is null) return Error("This LMS connection no longer exists or has been turned off.", frame);
        if (!continuation.AcceptMultiple && examIds.Count > 1) examIds = [examIds[0]];

        var scoped = OwnershipPolicy.InstructorsScoped(db, continuation.OrganizationId);
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == continuation.OrganizationId && e.Status == ExamStatus.Published && examIds.Contains(e.Id)
                && (!scoped || e.CreatedByUserId == continuation.UserId))
            .Select(e => new { e.Id, e.Title }).ToListAsync(ct);
        if (exams.Count == 0) return Error("That exam is no longer available. Go back and choose another.", frame);

        var (credentials, _) = await keys.SigningCredentialsAsync(ct);
        var jwt = BuildResponseJwt(credentials, reg, continuation.Data, exams.Select(e => (e.Id, e.Title)).ToList(), launchUrl, clock.GetUtcNow());

        var nonce = LtiPages.NewNonce();
        var body = new StringBuilder()
            .Append("<h1>Adding the exam to your course…</h1>")
            .Append("<form id=\"f\" method=\"post\" action=\"").Append(LtiPages.Encode(continuation.DeepLinkReturnUrl)).Append("\">")
            .Append("<input type=\"hidden\" name=\"JWT\" value=\"").Append(LtiPages.Encode(jwt)).Append("\" />")
            .Append("<noscript><p>Your browser is not running scripts. Press the button to finish.</p><button type=\"submit\">Finish adding the exam</button></noscript>")
            .Append("</form>")
            .Append("<script nonce=\"").Append(nonce).Append("\">document.getElementById('f').submit();</script>")
            .ToString();
        return new LtiPage(LtiPages.Document("Adding the exam", body), frame, LtiPages.OriginOf(continuation.DeepLinkReturnUrl), nonce);
    }

    /// <summary>The <c>LtiDeepLinkingResponse</c> JWT (Deep Linking 2.0 section 4.2): issued by the client id, for the
    /// platform's issuer, signed with Gadfly's RS256 key (its <c>kid</c> in the header, published at <c>/lti/jwks.json</c>).
    /// Public and pure so the tests can check its shape.</summary>
    public static string BuildResponseJwt(SigningCredentials credentials, LtiRegistration reg, string? data,
        IReadOnlyList<(Guid Id, string Title)> exams, string launchUrl, DateTimeOffset now)
    {
        var contentItems = exams.Select(e => new Dictionary<string, object?>
        {
            ["type"] = "ltiResourceLink",
            ["title"] = e.Title,
            ["url"] = launchUrl,
            ["custom"] = new Dictionary<string, string> { ["examId"] = e.Id.ToString() },
            ["lineItem"] = new Dictionary<string, object> { ["scoreMaximum"] = 100, ["label"] = e.Title.Length > 200 ? e.Title[..200] : e.Title },
        }).ToArray();
        var payload = new Dictionary<string, object>
        {
            ["iss"] = reg.ClientId,
            ["aud"] = reg.Issuer,
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nonce"] = LtiLaunchService.RandomToken(32),
            [LtiClaims.MessageType] = LtiClaims.DeepLinkingResponse,
            [LtiClaims.Version] = LtiClaims.SupportedVersion,
            [LtiClaims.DeploymentId] = reg.DeploymentId,
            [LtiClaims.DeepLinkingContentItems] = contentItems,
        };
        if (!string.IsNullOrEmpty(data)) payload[LtiClaims.DeepLinkingData] = data;
        return new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload), credentials);
    }

    private static LtiPage Error(string message, string frameAncestors = "https:") =>
        new(LtiPages.Document("Can't add this exam", $"<h1>Can't add this exam</h1><p>{LtiPages.Encode(message)}</p>"), frameAncestors);
}
