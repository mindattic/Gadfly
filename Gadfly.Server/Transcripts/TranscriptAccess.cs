using System.Security.Claims;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Desk;
using Gadfly.Server.Grading;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// Who may have which transcript copy (GAD-T295). Decided on the server for every download, never by the page:
/// <list type="bullet">
/// <item>an API key, an organization Owner or Admin, or an admin-desk administrator: the reviewer copy;</item>
/// <item>a role that can view transcripts (Instructor, Examiner, Second Reviewer): the reviewer copy, or the blind grader
/// copy while the session's grading is blind (<see cref="BlindGrading"/>);</item>
/// <item>a student: the student copy of their own session, and only once its result has been published to them;</item>
/// <item>anyone else: nothing.</item>
/// </list>
/// A caller may ask for a lesser copy than their own (a reviewer downloading the student's copy), never a greater one.
/// </summary>
public static class TranscriptAccess
{
    public static async Task<TranscriptAudience?> EntitledAsync(GadflyDbContext db, ITenantContext tenant, ClaimsPrincipal user, IConfiguration config,
        GradingService grading, Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        if (tenant.IsSystem || tenant.OrganizationId is not { } org || tenant.Role is not { } role) return null;
        if (role is OrgRoles.Api or OrgRoles.Owner or OrgRoles.Admin) return TranscriptAudience.Reviewer;
        if (DeskAccess.IsDeskAdmin(user, tenant, config)) return TranscriptAudience.Reviewer;
        if (PermissionMatrix.Has(role, Permission.ViewTranscripts))
        {
            var blind = await BlindGrading.BlindSessionsAsync(db, org, [sessionId], now, ct);
            return blind.Contains(sessionId) ? TranscriptAudience.BlindGrader : TranscriptAudience.Reviewer;
        }
        if (PermissionMatrix.Has(role, Permission.ViewOwnPublishedResults))
            return (await grading.MyResultsAsync(ct)).Any(r => r.SessionId == sessionId) ? TranscriptAudience.Student : null;
        return null;
    }

    /// <summary>For an API caller asking for the student copy: only once the result has been published with approval.</summary>
    public static async Task<bool> PublishedToStudentAsync(GadflyDbContext db, Guid org, Guid sessionId, CancellationToken ct)
    {
        var status = await db.FinalGrades.AsNoTracking().Where(g => g.SessionId == sessionId && g.OrganizationId == org).Select(g => (GradeStatus?)g.Status).FirstOrDefaultAsync(ct);
        if (status is not (GradeStatus.Published or GradeStatus.UnderAppeal or GradeStatus.Amended)) return false;
        var actions = await db.ApprovalRecords.AsNoTracking().Where(a => a.SessionId == sessionId && a.OrganizationId == org
            && (a.Action == ApprovalAction.Publish || a.Action == ApprovalAction.Finalize)).Select(a => a.Action).Distinct().ToListAsync(ct);
        return actions.Contains(ApprovalAction.Publish) && actions.Contains(ApprovalAction.Finalize);
    }
}

/// <summary>
/// Issuing and verifying transcript copies (GAD-T295). Every download is recorded as a <c>transcript.issued</c> audit
/// event naming the copy's verification code, hash, audience and format; that record is what /transcripts/verify checks a
/// code against. The audit row holds no transcript content.
/// </summary>
public static class TranscriptRegistry
{
    public const string IssuedAction = "transcript.issued";
    public const string EntityType = "Transcript";

    private sealed record Issued(string Sha256, string Audience, string Format);

    /// <summary>Stamps a copy and records it (saved at once).</summary>
    public static async Task<TranscriptStamp> IssueAsync(GadflyDbContext db, ITenantContext tenant, TranscriptDocument document, TranscriptFormat format,
        string baseUrl, DateTimeOffset now, CancellationToken ct)
    {
        var stamp = TranscriptHash.Stamp(document, now, baseUrl);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = tenant.Role,
            Action = IssuedAction,
            EntityType = EntityType,
            EntityId = stamp.Code,
            SessionId = document.SessionId,
            AfterJson = JsonSerializer.Serialize(new Issued(stamp.Sha256, document.Audience, TranscriptFormats.Extension(format)), TranscriptHash.Json),
            Reason = $"{TranscriptAudiences.Label(TranscriptAudiences.Parse(document.Audience) ?? TranscriptAudience.Reviewer)} downloaded as {TranscriptFormats.Label(format)}.",
            At = now,
        });
        await db.SaveChangesAsync(ct);
        return stamp;
    }

    public enum Verdict
    {
        /// <summary>The copy is on record and Gadfly's record still gives exactly this content.</summary>
        Matches,
        /// <summary>The copy was issued by Gadfly, but the record has changed since (a later correction, a removal under retention rules).</summary>
        RecordChangedSince,
        /// <summary>The copy was issued, but its session is no longer held (deleted at the student's or organization's request).</summary>
        NoLongerHeld,
        /// <summary>The uploaded file names a code Gadfly issued, but its content is not what was issued: it was edited.</summary>
        FileAltered,
        /// <summary>Gadfly never issued a copy with this code or content.</summary>
        NotFound,
        /// <summary>What was given is not a verification code or a transcript JSON file.</summary>
        Unreadable,
    }

    /// <summary>
    /// The outcome shown to whoever checks a copy: never any transcript content. <paramref name="Kind"/> names what was
    /// matched ("transcript" or, from <see cref="Reports.CertificateRegistry"/>, "certificate"; GAD-T311), so the shared
    /// verify page at <see cref="TranscriptEndpoints.VerifyPath"/> can say the right noun without a second verify page.
    /// </summary>
    public sealed record Outcome(Verdict Verdict, string? Code = null, string? Audience = null, DateTimeOffset? FirstIssuedAt = null, string Kind = "transcript");

    /// <summary>Checks a typed code. <paramref name="db"/> must be a system-scope context (the visitor may be signed out).</summary>
    public static async Task<Outcome> VerifyCodeAsync(GadflyDbContext db, string? typed, CancellationToken ct)
    {
        if (TranscriptHash.NormalizeCode(typed) is not { } code) return new(Verdict.Unreadable);
        return await CheckAsync(db, code, sha256: null, ct);
    }

    /// <summary>Checks an uploaded JSON download. <paramref name="db"/> must be a system-scope context.</summary>
    public static async Task<Outcome> VerifyFileAsync(GadflyDbContext db, Stream json, CancellationToken ct)
    {
        JsonDocument parsed;
        try { parsed = await JsonDocument.ParseAsync(json, new JsonDocumentOptions { MaxDepth = 64 }, ct); }
        catch (JsonException) { return new(Verdict.Unreadable); }
        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(Verdict.Unreadable);
            var transcript = root.TryGetProperty("transcript", out var t) && t.ValueKind == JsonValueKind.Object ? t
                : root.TryGetProperty("schema", out _) ? root : default;
            if (transcript.ValueKind != JsonValueKind.Object) return new(Verdict.Unreadable);
            var sha = TranscriptHash.Sha256(TranscriptHash.Canonical(transcript));
            var code = TranscriptHash.Code(sha);
            var byContent = await CheckAsync(db, code, sha, ct);
            if (byContent.Verdict != Verdict.NotFound) return byContent;
            // Not issued with this content: if the file carries a code Gadfly did issue, the file was edited after it was issued.
            var claimed = root.TryGetProperty("verification", out var v) && v.ValueKind == JsonValueKind.Object
                && v.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? TranscriptHash.NormalizeCode(c.GetString()) : null;
            if (claimed is not null && await FirstIssueAsync(db, claimed, null, ct) is { } issue)
                return new(Verdict.FileAltered, claimed, issue.Audience, issue.At);
            return new(Verdict.NotFound, code);
        }
    }

    private sealed record IssueRow(Guid OrganizationId, Guid SessionId, string Sha256, string Audience, DateTimeOffset At);

    private static async Task<IssueRow?> FirstIssueAsync(GadflyDbContext db, string code, string? sha256, CancellationToken ct)
    {
        var rows = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == IssuedAction && a.EntityType == EntityType && a.EntityId == code && a.OrganizationId != null && a.SessionId != null)
            .OrderBy(a => a.Id).Take(20).Select(a => new { a.OrganizationId, a.SessionId, a.AfterJson, a.At }).ToListAsync(ct);
        foreach (var r in rows)
        {
            Issued? issued;
            try { issued = r.AfterJson is null ? null : JsonSerializer.Deserialize<Issued>(r.AfterJson, TranscriptHash.Json); }
            catch (JsonException) { issued = null; }
            if (issued is null || !TranscriptHash.IsSha256(issued.Sha256)) continue;
            if (sha256 is not null && !string.Equals(issued.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) continue;
            return new IssueRow(r.OrganizationId!.Value, r.SessionId!.Value, issued.Sha256, issued.Audience, r.At);
        }
        return null;
    }

    private static async Task<Outcome> CheckAsync(GadflyDbContext db, string code, string? sha256, CancellationToken ct)
    {
        if (await FirstIssueAsync(db, code, sha256, ct) is not { } issue) return new(Verdict.NotFound, code);
        var audience = TranscriptAudiences.Parse(issue.Audience) ?? TranscriptAudience.Reviewer;
        var current = await new TranscriptBuilder(db).BuildAsync(issue.OrganizationId, issue.SessionId, audience, ct);
        if (current is null) return new(Verdict.NoLongerHeld, code, issue.Audience, issue.At);
        var now = TranscriptHash.Sha256(TranscriptHash.Canonical(current));
        return new(string.Equals(now, issue.Sha256, StringComparison.OrdinalIgnoreCase) ? Verdict.Matches : Verdict.RecordChangedSince,
            code, issue.Audience, issue.At);
    }
}
