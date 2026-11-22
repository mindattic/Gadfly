using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// Builds a session's transcript from the response ledger (GAD-T295): loads the session, its turns, every response with
/// its transcript versions, the read-back and clarification requests, the speech marks (word timings and unsure words)
/// and, for the reviewer copy only, the integrity events and the panel's judgments; then hands them to
/// <see cref="TranscriptComposer"/>. Every query names the organization on top of the tenant filter, and a scoped
/// instructor asking for another instructor's session gets null (the refusal audited, as everywhere else).
/// Who may have which copy is decided by <see cref="TranscriptAccess"/>, never here.
/// </summary>
public sealed class TranscriptBuilder(GadflyDbContext db)
{
    public async Task<TranscriptDocument?> BuildAsync(Guid organizationId, Guid sessionId, TranscriptAudience audience, CancellationToken ct = default)
    {
        var source = await LoadAsync(organizationId, sessionId, audience, ct);
        return source is null ? null : TranscriptComposer.Compose(source, audience);
    }

    public async Task<TranscriptSource?> LoadAsync(Guid org, Guid sessionId, TranscriptAudience audience, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == org, ct);
        if (session is null)
        {
            await db.RefuseForeignAsync(OwnershipScope.Kind.Session, sessionId, "Downloading a transcript", ct);
            return null;
        }
        var full = audience == TranscriptAudience.Reviewer;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == session.ExamId && e.OrganizationId == org, ct);
        var titles = exam is null ? new Dictionary<string, string>()
            : (await db.Concepts.AsNoTracking().Where(c => c.OrganizationId == org && c.CollectionId == exam.CollectionId)
                .Select(c => new { c.Key, c.Title }).ToListAsync(ct))
              .GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First().Title);
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == sessionId && t.OrganizationId == org).OrderBy(t => t.Index).ToListAsync(ct);
        var responses = await db.StudentResponses.AsNoTracking().Include(r => r.Versions)
            .Where(r => r.SessionId == sessionId && r.OrganizationId == org).OrderBy(r => r.Sequence).ToListAsync(ct);
        var clarifications = await db.ClarificationEvents.AsNoTracking().Where(c => c.SessionId == sessionId && c.OrganizationId == org)
            .OrderBy(c => c.AskedAt).ToListAsync(ct);
        var marks = await SpeechMarks.LoadAsync(db, org, responses, ct);
        var student = await db.AuthUsers.AsNoTracking().Where(u => u.Id == session.StudentUserId)
            .Select(u => new { u.UserName, u.DisplayName }).FirstOrDefaultAsync(ct);
        var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == org).Select(o => o.Name).FirstOrDefaultAsync(ct);

        IReadOnlyList<IntegrityEvent> integrity = [];
        IReadOnlyList<Judgment> judgments = [];
        if (full)
        {
            integrity = await db.IntegrityEvents.AsNoTracking().Where(e => e.SessionId == sessionId && e.OrganizationId == org).OrderBy(e => e.StartedAt).ToListAsync(ct);
            var turnIds = turns.Select(t => t.Id).ToList();
            judgments = await db.Judgments.AsNoTracking().Where(j => j.OrganizationId == org && turnIds.Contains(j.TurnId)).ToListAsync(ct);
        }

        return new TranscriptSource
        {
            Session = session,
            Exam = exam,
            OrganizationName = orgName,
            StudentName = student is null ? "(unknown student)" : string.IsNullOrWhiteSpace(student.DisplayName) ? student.UserName : $"{student.DisplayName} ({student.UserName})",
            ConceptTitles = titles,
            Turns = turns,
            Responses = responses,
            Clarifications = clarifications,
            Marks = marks,
            IntegrityEvents = integrity,
            Judgments = judgments,
        };
    }
}
