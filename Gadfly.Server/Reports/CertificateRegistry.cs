using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Reports;

/// <summary>
/// Issuing and verifying completion certificates (GAD-T311/GAD-T379), built exactly like <see cref="TranscriptRegistry"/>
/// (GAD-T295: an audited <c>certificate.issued</c> event naming the hash, checked against a freshly-rebuilt certificate
/// when someone later verifies the code) so <c>/transcripts/verify</c> can recognize a certificate code without a
/// second verify page: it reuses <see cref="TranscriptRegistry.Outcome"/> and <see cref="TranscriptRegistry.Verdict"/>
/// directly, naming its own <see cref="TranscriptRegistry.Outcome.Kind"/> ("certificate") so the page's wording
/// says the right noun. A certificate carries no transcript-style JSON download, so only the code is ever checked,
/// never an uploaded file.
/// </summary>
public static class CertificateRegistry
{
    public const string IssuedAction = "certificate.issued";
    public const string EntityType = "Certificate";
    public const string Kind = "certificate";

    private sealed record Issued(string Sha256);

    /// <summary>Stamps a certificate and records it (saved at once). Reuses the transcript's hash/code/verify-URL machinery.</summary>
    public static async Task<TranscriptStamp> IssueAsync(GadflyDbContext db, ITenantContext tenant, CertificateDocument document, string baseUrl,
        DateTimeOffset now, CancellationToken ct)
    {
        var sha = Sha256Of(document);
        var code = TranscriptHash.Code(sha);
        var stamp = new TranscriptStamp(sha, code, now, TranscriptHash.VerifyUrl(baseUrl, code));
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = tenant.Role,
            Action = IssuedAction,
            EntityType = EntityType,
            EntityId = stamp.Code,
            SessionId = document.SessionId,
            AfterJson = JsonSerializer.Serialize(new Issued(sha), TranscriptHash.Json),
            Reason = "Completion certificate downloaded.",
            At = now,
        });
        await db.SaveChangesAsync(ct);
        return stamp;
    }

    /// <summary>Checks a typed code. <paramref name="db"/> must be a system-scope context (the visitor may be signed out).</summary>
    public static async Task<TranscriptRegistry.Outcome> VerifyCodeAsync(GadflyDbContext db, string? typed, CancellationToken ct)
    {
        if (TranscriptHash.NormalizeCode(typed) is not { } code) return new(TranscriptRegistry.Verdict.Unreadable, Kind: Kind);
        if (await FirstIssueAsync(db, code, ct) is not { } issue) return new(TranscriptRegistry.Verdict.NotFound, code, Kind: Kind);
        var current = await CertificateBuilder.BuildAsync(db, issue.OrganizationId, issue.SessionId, ct);
        if (current is null) return new(TranscriptRegistry.Verdict.NoLongerHeld, code, FirstIssuedAt: issue.At, Kind: Kind);
        var sha = Sha256Of(current);
        return new(string.Equals(sha, issue.Sha256, StringComparison.OrdinalIgnoreCase) ? TranscriptRegistry.Verdict.Matches : TranscriptRegistry.Verdict.RecordChangedSince,
            code, FirstIssuedAt: issue.At, Kind: Kind);
    }

    private static string Sha256Of(CertificateDocument document) =>
        TranscriptHash.Sha256(TranscriptHash.Canonical(JsonSerializer.SerializeToElement(document, TranscriptHash.Json)));

    private sealed record IssueRow(Guid OrganizationId, Guid SessionId, string Sha256, DateTimeOffset At);

    private static async Task<IssueRow?> FirstIssueAsync(GadflyDbContext db, string code, CancellationToken ct)
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
            return new IssueRow(r.OrganizationId!.Value, r.SessionId!.Value, issued.Sha256, r.At);
        }
        return null;
    }
}
