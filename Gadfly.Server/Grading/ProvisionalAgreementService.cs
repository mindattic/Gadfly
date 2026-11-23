using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates.Mock;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>An exam the report can be narrowed to.</summary>
public sealed record ProvisionalAgreementExam(Guid Id, string Title);

/// <summary>
/// How the live turn's fast model agreed with the full panel over the organization's ended sessions (or one exam's).
/// </summary>
/// <param name="ExamId">The exam the report is narrowed to; null for the whole organization.</param>
/// <param name="ExamTitle">That exam's title; null for the whole organization.</param>
/// <param name="Sessions">Ended sessions read.</param>
/// <param name="SessionsWithProvisional">Of those, sessions with at least one turn the fast model judged.</param>
/// <param name="PendingTurns">Provisional turns left out because the panel's full judgment never arrived.</param>
/// <param name="UnreadableSessions">Sessions whose transcript could not be read.</param>
/// <param name="AuditedDisagreements">The <see cref="SessionOrchestrator.ProvisionalDisagreedAction"/> audit events of the sessions read.</param>
/// <param name="AnyMock">True when any compared turn was judged by the mock fast model.</param>
public sealed record ProvisionalAgreementSummary(
    Guid? ExamId,
    string? ExamTitle,
    int Sessions,
    int SessionsWithProvisional,
    int PendingTurns,
    int UnreadableSessions,
    int AuditedDisagreements,
    bool AnyMock,
    ProvisionalAgreementReport Report,
    IReadOnlyList<ProvisionalAgreementExam> Exams)
{
    /// <summary>True when the audit log and the transcripts count the same number of disagreements.</summary>
    public bool AuditMatches => AuditedDisagreements == Report.Overall.Disagreements;
}

/// <summary>
/// Reads the organization's ended sessions' transcripts (optionally one exam's) into the fast-versus-panel agreement analysis
/// (GAD-T645, <see cref="ProvisionalAgreement"/>): one observation per turn the live turn's fast model judged whose full panel
/// judgment arrived, grouped by the exam's subject, the move and the fast provider/model. The disagreements the transcripts
/// record are cross-checked against the <c>judgment.provisional.disagreed</c> audit events (SessionOrchestrator.Judging.cs).
/// Local arithmetic: no model is called.
/// </summary>
public sealed class ProvisionalAgreementService(GadflyDbContext db, ITenantContext tenant)
{
    private GradingActor Actor => new(tenant.UserId, tenant.Role);

    public async Task<ProvisionalAgreementSummary?> AnalyzeAsync(Guid? examId = null, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !Actor.Has(Permission.ViewAnalytics)) return null;
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == org)
            .Select(e => new { e.Id, e.Title, e.Subject }).ToListAsync(ct);
        string? examTitle = null;
        if (examId is { } only)
        {
            if (exams.FirstOrDefault(e => e.Id == only) is not { } exam) return null;
            examTitle = exam.Title;
        }
        var subjects = exams.ToDictionary(e => e.Id, e => e.Subject);

        const string notAnAttempt = nameof(EndReason.PromptSetChanged);
        var query = db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == org && s.State == SessionState.Ended && s.EndReason != notAnAttempt && s.TranscriptJson != null);
        if (examId is { } id) query = query.Where(s => s.ExamId == id);
        var sessions = await query.Select(s => new { s.Id, s.ExamId, s.TranscriptJson }).ToListAsync(ct);

        var observations = new List<ProvisionalObservation>();
        int withProvisional = 0, pending = 0, unreadable = 0;
        foreach (var s in sessions)
        {
            IReadOnlyList<Turn> turns;
            try { turns = SessionTranscriptSerializer.Deserialize(s.TranscriptJson!).Turns; }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException) { unreadable++; continue; }
            var subject = subjects.GetValueOrDefault(s.ExamId) ?? "";
            var any = false;
            foreach (var t in turns)
            {
                if (ProvisionalObservation.From(t, subject) is not { } o) continue;
                any = true;
                // Only fully judged turns: one whose panel judgment never arrived has nothing to compare.
                if (o.FullScore is null) { pending++; continue; }
                observations.Add(o);
            }
            if (any) withProvisional++;
        }

        var ids = sessions.Select(s => s.Id).ToList();
        var audited = ids.Count == 0 ? 0 : await db.AuditEvents.AsNoTracking()
            .CountAsync(a => a.OrganizationId == org && a.Action == SessionOrchestrator.ProvisionalDisagreedAction
                             && a.SessionId != null && ids.Contains(a.SessionId.Value), ct);

        return new ProvisionalAgreementSummary(examId, examTitle, sessions.Count, withProvisional, pending, unreadable, audited,
            observations.Any(o => string.Equals(o.ProviderId, MockVotingClient.ProviderId, StringComparison.OrdinalIgnoreCase)),
            ProvisionalAgreement.Analyze(observations),
            exams.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Select(e => new ProvisionalAgreementExam(e.Id, e.Title)).ToList());
    }
}
