using Gadfly.Server.Data;
using Gadfly.Server.Transcripts;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Reports;

/// <summary>
/// One student's completion certificate for one exam attempt (GAD-T311/GAD-T379). Deterministic content only (no
/// "generated at" time, same discipline as <see cref="TranscriptDocument"/>): hashing the same record twice always
/// gives the same verification code, so a later re-download still verifies and a later correction to the grade is
/// detected as "the record has changed since" rather than silently matching.
/// </summary>
public sealed record CertificateDocument
{
    public string Schema { get; init; } = "gadfly.certificate/1";
    public required Guid SessionId { get; init; }
    public string? Organization { get; init; }
    public required string ExamTitle { get; init; }
    public string? Subject { get; init; }
    public string? Topic { get; init; }
    public required string StudentName { get; init; }
    public int Attempt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public decimal? FinalPercent { get; init; }
    public string? Label { get; init; }
}

/// <summary>
/// Builds a certificate from the current grade record (GAD-T311/GAD-T379), the same way <see cref="TranscriptBuilder"/>
/// builds a transcript from the current response ledger: called both when a certificate is first issued and whenever
/// one is re-verified, so a later amendment is caught as a change rather than silently matching the old code. Null
/// when there is nothing to certify: the session is gone, or the result has not been published to the student
/// (<see cref="TranscriptAccess.PublishedToStudentAsync"/>, exactly the same gate the student's transcript copy uses).
/// </summary>
public static class CertificateBuilder
{
    public static async Task<CertificateDocument?> BuildAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId, ct);
        if (session is null) return null;
        if (!await TranscriptAccess.PublishedToStudentAsync(db, organizationId, sessionId, ct)) return null;
        var grade = await db.FinalGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SessionId == sessionId && g.OrganizationId == organizationId, ct);
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == session.ExamId && e.OrganizationId == organizationId, ct);
        var student = await db.AuthUsers.AsNoTracking().Where(u => u.Id == session.StudentUserId).Select(u => new { u.UserName, u.DisplayName }).FirstOrDefaultAsync(ct);
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).FirstOrDefaultAsync(ct);
        return new CertificateDocument
        {
            SessionId = sessionId,
            Organization = orgName,
            ExamTitle = exam?.Title ?? "(exam)",
            Subject = exam?.Subject,
            Topic = exam?.Topic,
            StudentName = student is null ? "(unknown student)" : string.IsNullOrWhiteSpace(student.DisplayName) ? student.UserName : $"{student.DisplayName} ({student.UserName})",
            Attempt = session.Attempt,
            CompletedAt = session.EndedAt ?? session.CreatedAt,
            FinalPercent = grade?.FinalPercent,
            Label = grade?.LetterOrLabel,
        };
    }
}
