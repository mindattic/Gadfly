using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>An exam's self-preference check.</summary>
/// <param name="SessionsWithAuthorship">Sessions whose transcript records who wrote each question (recorded since GAD-T431).</param>
/// <param name="Authors">How many asked questions each provider family wrote, across the exam.</param>
public sealed record SelfPreferenceReport(Guid ExamId, string ExamTitle, int Sessions, int SessionsWithAuthorship, int Observations,
    IReadOnlyDictionary<string, int> Authors, IReadOnlyList<SelfPreferenceRow> Rows, bool AnyMock);

/// <summary>
/// Reads an exam's transcripts (who wrote each question) and its AI grades (each member's level per objective) into the
/// self-preference check (GAD-T431, <see cref="SelfPreference"/>). Local arithmetic: no model is called.
/// </summary>
public sealed class SelfPreferenceService(GadflyDbContext db, ITenantContext tenant)
{
    private GradingActor Actor => new(tenant.UserId, tenant.Role);

    public async Task<SelfPreferenceReport?> AnalyzeAsync(Guid examId, CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org || !(Actor.Has(Permission.ApproveGradingPolicyChange) || Actor.Has(Permission.ViewAnalytics))) return null;
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org).Select(e => new { e.Id, e.Title }).FirstOrDefaultAsync(ct);
        if (exam is null) return null;
        const string notAnAttempt = nameof(EndReason.PromptSetChanged);
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == org && s.ExamId == examId && s.State == SessionState.Ended && s.EndReason != notAnAttempt && s.TranscriptJson != null)
            .Select(s => new { s.Id, s.TranscriptJson }).ToListAsync(ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var objectives = await GradingInputs.LoadObjectivesAsync(db, examId, org, ct);
        var ai = (await db.AiObjectiveGrades.AsNoTracking().Where(g => g.OrganizationId == org && ids.Contains(g.SessionId))
                .Select(g => new { g.SessionId, g.ObjectiveId, g.CriteriaJson, g.IsMock, g.CreatedAt }).ToListAsync(ct))
            .GroupBy(g => (g.SessionId, g.ObjectiveId)).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());
        var human = (await db.HumanObjectiveGrades.AsNoTracking().Where(h => h.OrganizationId == org && ids.Contains(h.SessionId) && h.WasBlind && h.SupersedesId == null)
                .Select(h => new { h.SessionId, h.ObjectiveId, h.Level, h.SubmittedAt }).ToListAsync(ct))
            .GroupBy(h => (h.SessionId, h.ObjectiveId)).ToDictionary(g => g.Key, g => (int)g.OrderBy(x => x.SubmittedAt).First().Level);

        var observations = new List<SelfPreferenceObservation>();
        var authors = new Dictionary<string, int>(StringComparer.Ordinal);
        var withAuthorship = 0;
        var anyMock = false;
        foreach (var s in sessions)
        {
            IReadOnlyList<Turn> turns;
            try { turns = SessionTranscriptSerializer.Deserialize(s.TranscriptJson!).Turns; }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException) { continue; }
            var written = turns.Where(t => t.QuestionAuthor is { ModelWritten: true }).ToList();
            if (written.Count == 0) continue;
            withAuthorship++;
            foreach (var family in written.SelectMany(t => t.QuestionAuthor!.Families)) authors[family] = authors.GetValueOrDefault(family) + 1;

            foreach (var o in objectives)
            {
                if (!ai.TryGetValue((s.Id, o.Id), out var grade)) continue;
                var onObjective = written.Where(t => t.ObjectiveKey == o.Key || (t.ObjectiveKey is null && o.ConceptKey is { } ck && t.ConceptId == ck)).ToList();
                if (onObjective.Count == 0) continue;
                var voters = GradingJson.Read<StoredAiDetail>(grade.CriteriaJson)?.Voters ?? [];
                anyMock |= grade.IsMock;
                human.TryGetValue((s.Id, o.Id), out var h);
                foreach (var v in voters)
                {
                    var family = v.ProviderId.Trim().ToLowerInvariant();
                    var share = onObjective.Count(t => t.QuestionAuthor!.Families.Contains(family)) / (double)onObjective.Count;
                    var peers = voters.Where(x => !ReferenceEquals(x, v)).Select(x => (int)x.Level).ToList();
                    observations.Add(new SelfPreferenceObservation(family, (int)v.Level, SelfPreference.PeerMedian(peers),
                        human.ContainsKey((s.Id, o.Id)) ? h : null, share));
                }
            }
        }
        return new SelfPreferenceReport(exam.Id, exam.Title, sessions.Count, withAuthorship, observations.Count, authors,
            SelfPreference.Analyze(observations), anyMock);
    }
}
