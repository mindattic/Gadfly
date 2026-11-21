using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Grading;

/// <summary>
/// Gathers what AI grading needs from SQL (Part 11.1 Stage 4, evidence collection): the session's response
/// records, the exam's learning objectives and rubrics, and each objective's evidence. Never invents
/// evidence: an objective with no verified response on it is graded Level 0 (insufficient evidence).
/// </summary>
internal static class GradingInputs
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Makes sure every answered turn has its immutable <see cref="StudentResponse"/> record. Sessions recorded
    /// before response capture existed (or by a client that skipped it) get one built from the stored answer,
    /// state Captured, with version 1 of the transcript. Existing records are never changed.
    /// </summary>
    public static async Task<List<StudentResponse>> EnsureResponsesAsync(GadflyDbContext db, ExamSession session, DateTimeOffset now, CancellationToken ct)
    {
        var responses = await db.StudentResponses.Where(r => r.SessionId == session.Id && r.OrganizationId == session.OrganizationId)
            .OrderBy(r => r.Sequence).ToListAsync(ct);
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == session.Id && t.OrganizationId == session.OrganizationId && t.AnswerText != null)
            .OrderBy(t => t.Index).ToListAsync(ct);
        var covered = responses.Select(r => r.TurnId).ToHashSet();
        var next = responses.Count == 0 ? 1 : responses.Max(r => r.Sequence) + 1;
        var added = 0;
        foreach (var t in turns.Where(t => !covered.Contains(t.Id) && !string.IsNullOrWhiteSpace(t.AnswerText)))
        {
            var r = new StudentResponse
            {
                OrganizationId = session.OrganizationId, SessionId = session.Id, TurnId = t.Id, Sequence = next++, Kind = ResponseKind.Original,
                Modality = t.AnswerMode is "voice" or "speech" ? "voice" : "text", OriginalText = t.AnswerText!, VerifiedText = t.AnswerText!,
                VerificationState = VerificationState.Captured, SubmittedAt = t.AnswerCompletedAt ?? t.AskedAt,
            };
            r.Versions.Add(new TranscriptVersion { OrganizationId = session.OrganizationId, Version = 1, Text = t.AnswerText!, Source = "student", CreatedAt = now });
            db.StudentResponses.Add(r);
            responses.Add(r);
            added++;
        }
        if (added > 0)
        {
            GradingAudit.Add(db, session.OrganizationId, GradingActor.System, "response.captured", "ExamSession", session.Id.ToString(), session.Id, now,
                after: new { responses = added }, reason: "Response records built from the session's stored answers for grading (state Captured, original text preserved).");
            await db.SaveChangesAsync(ct);
        }
        return responses;
    }

    /// <summary>
    /// The exam's learning objectives with their rubric criteria. An exam with none gets one objective per
    /// concept in its policy (all of its source material's concepts when the policy names none), each with the
    /// four default Part 5.1 criteria, weight 1 and a maximum of 100; the derivation is audited and the
    /// instructor can edit the objectives afterwards.
    /// </summary>
    public static async Task<List<LearningObjective>> EnsureObjectivesAsync(GadflyDbContext db, Exam exam, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await LoadObjectivesAsync(db, exam.Id, exam.OrganizationId, ct);
        if (existing.Count > 0) return existing;

        var policy = Read<SessionPolicy>(exam.PolicyJson) ?? new SessionPolicy();
        var concepts = await db.Concepts.AsNoTracking().Where(c => c.CollectionId == exam.CollectionId && c.OrganizationId == exam.OrganizationId && !c.IsArchived)
            .OrderBy(c => c.Ordinal).ToListAsync(ct);
        var chosen = policy.ConceptIds.Count > 0
            ? policy.ConceptIds.Select(k => concepts.FirstOrDefault(c => c.Key == k)).OfType<Concept>().ToList()
            : concepts;
        if (chosen.Count == 0) return [];

        var ordinal = 0;
        foreach (var c in chosen)
        {
            var o = new LearningObjective
            {
                OrganizationId = exam.OrganizationId, ExamId = exam.Id, Key = Cap(c.Key, 120), Title = Cap(string.IsNullOrWhiteSpace(c.Title) ? c.Key : c.Title, 300),
                Description = string.IsNullOrWhiteSpace(c.Definition) ? null : c.Definition, ConceptKey = Cap(c.Key, 120), Weight = 1m, MaxScore = 100m, Ordinal = ordinal++,
            };
            var i = 0;
            foreach (var d in AiObjectiveGrader.DefaultCriteria())
                o.Criteria.Add(new RubricCriterion { OrganizationId = exam.OrganizationId, Dimension = d.Dimension, Name = d.Name, Description = d.Description, Weight = d.Weight, Ordinal = i++ });
            db.LearningObjectives.Add(o);
        }
        GradingAudit.Add(db, exam.OrganizationId, GradingActor.System, "objective.derived", "Exam", exam.Id.ToString(), null, now,
            after: new { objectives = chosen.Select(c => c.Key).ToList() },
            reason: "The exam had no learning objectives; one per examined concept was derived (weight 1, maximum 100, default criteria).");
        await db.SaveChangesAsync(ct);
        return await LoadObjectivesAsync(db, exam.Id, exam.OrganizationId, ct);
    }

    public static async Task<List<LearningObjective>> LoadObjectivesAsync(GadflyDbContext db, Guid examId, Guid organizationId, CancellationToken ct) =>
        (await db.LearningObjectives.AsNoTracking().Include(o => o.Criteria).ThenInclude(c => c.Levels)
            .Where(o => o.ExamId == examId && o.OrganizationId == organizationId).ToListAsync(ct))
        .OrderBy(o => o.Ordinal).ThenBy(o => o.Title).ToList();

    /// <summary>The grader's input for one objective.</summary>
    public static async Task<ObjectiveGradingInput> BuildInputAsync(GadflyDbContext db, ExamSession session, Exam exam, LearningObjective objective,
        IReadOnlyList<StudentResponse> responses, bool allowAssisted, CancellationToken ct)
    {
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.SessionId == session.Id && t.OrganizationId == session.OrganizationId)
            .Select(t => new { t.Id, t.ConceptKey, t.Question }).ToDictionaryAsync(t => t.Id, ct);
        var evidenceRows = await db.AssessmentEvidence.AsNoTracking()
            .Where(e => e.SessionId == session.Id && e.ObjectiveId == objective.Id && e.OrganizationId == session.OrganizationId).ToListAsync(ct);
        var byId = responses.ToDictionary(r => r.Id);

        List<EvidenceItem> items;
        if (evidenceRows.Count > 0)
        {
            // The evidence record organized for this objective (AssessmentEvidence) wins.
            items = evidenceRows.Where(e => byId.ContainsKey(e.ResponseId)).GroupBy(e => e.ResponseId).Select(g =>
            {
                var r = byId[g.Key];
                var first = g.First();
                return new EvidenceItem(r.Id, r.Sequence, turns.TryGetValue(r.TurnId, out var t) ? t.Question : "", r.VerifiedText, r.VerificationState,
                    Assisted: r.Assisted || g.Any(e => e.Assisted), Contaminated: g.Any(e => e.Contaminated),
                    Note: string.Join("; ", g.Select(e => e.Note).Where(n => !string.IsNullOrWhiteSpace(n))),
                    Stance: (EvidenceStanceHint)(int)first.Stance);
            }).ToList();
        }
        else
        {
            // Otherwise the session's responses on the objective's concept.
            items = objective.ConceptKey is not { Length: > 0 } key ? []
                : responses.Where(r => turns.TryGetValue(r.TurnId, out var t) && t.ConceptKey == key)
                    .Select(r => new EvidenceItem(r.Id, r.Sequence, turns[r.TurnId].Question, r.VerifiedText, r.VerificationState, Assisted: r.Assisted))
                    .ToList();
        }

        string? conceptTitle = null, definition = null;
        List<string> misconceptions = [];
        if (objective.ConceptKey is { Length: > 0 } ck)
        {
            var concept = await db.Concepts.AsNoTracking().FirstOrDefaultAsync(c => c.CollectionId == exam.CollectionId && c.Key == ck && c.OrganizationId == exam.OrganizationId, ct);
            if (concept is not null)
            {
                conceptTitle = concept.Title;
                definition = concept.Definition;
                misconceptions = Read<List<string>>(concept.MisconceptionsJson) ?? [];
            }
        }

        var criteria = objective.Criteria.OrderBy(c => c.Ordinal).Select(c => new GradingCriterion(c.Name, c.Dimension, c.Description, c.Weight,
            c.Levels.ToDictionary(l => l.Level, l => l.Descriptor))).ToList();
        return new ObjectiveGradingInput(
            new GradingObjective(objective.Key, objective.Title, objective.Description, objective.MaxScore, criteria, conceptTitle, definition, misconceptions),
            items, allowAssisted);
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, CamelCase); }
        catch (JsonException) { return null; }
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];
}
