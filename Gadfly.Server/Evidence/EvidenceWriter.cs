using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Evidence;

/// <summary>What one <see cref="EvidenceWriter.SyncAsync"/> did.</summary>
public sealed record EvidenceSyncResult(int Added, int NewlyContaminated, int NewlyAssisted, int SkippedNoObjective, int SkippedNoResponse,
    int SkippedUnverified, int SkippedNotVerbatim);

/// <summary>
/// Evidence Collection Engine persistence (GAD-T91, Component D): maps the engine's evidence items
/// (<see cref="SourceExamEngine.CollectEvidence"/>) to <see cref="AssessmentEvidence"/> rows linked to the
/// student's verified response and its latest transcript version. Evidence is never fabricated: an item is
/// written only when its quote is a verbatim span of the response's verified text and the response is in a
/// verified state. Contamination (GAD-T92) and assistance (GAD-T93) flags only ever turn on. Idempotent: the
/// orchestrator can call it after every step. Adds rows and audit events to the context; the caller saves.
/// </summary>
public static class EvidenceWriter
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const int MaxNote = 2000;

    public static EvidenceStance StanceOf(EvidencePolarity polarity) => polarity switch
    {
        EvidencePolarity.Supports => EvidenceStance.Supports,
        EvidencePolarity.Contradicts => EvidenceStance.Contradicts,
        EvidencePolarity.Misconception => EvidenceStance.Misconception,
        _ => EvidenceStance.Uncertain,
    };

    /// <summary>The row's note: the student's quoted words, then what they show.</summary>
    public static string NoteOf(CollectedEvidence e)
    {
        var note = $"\"{e.Quote}\" - {e.Note}";
        return note.Length <= MaxNote ? note : note[..MaxNote];
    }

    /// <summary>The text grading uses for a response: its verified text, or the original when none was recorded.</summary>
    public static string GradingText(StudentResponse response) =>
        string.IsNullOrEmpty(response.VerifiedText) ? response.OriginalText : response.VerifiedText;

    /// <summary>
    /// Maps one evidence item to a row for <paramref name="response"/>, or null when it must not be written:
    /// the response is not verified (or needs a human), or the quote is not in its verified text.
    /// </summary>
    public static AssessmentEvidence? ToRow(CollectedEvidence e, StudentResponse response, Guid objectiveId, Guid? transcriptVersionId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(response);
        if (!VerificationMachine.IsVerified(response.VerificationState)) return null;
        if (!ResponseAnalyzer.IsVerbatim(e.Quote, GradingText(response))) return null;
        return new AssessmentEvidence
        {
            OrganizationId = response.OrganizationId,
            SessionId = response.SessionId,
            ObjectiveId = objectiveId,
            ResponseId = response.Id,
            TranscriptVersionId = transcriptVersionId,
            Stance = StanceOf(e.Polarity),
            Dimension = e.Dimension,
            Note = NoteOf(e),
            Assisted = e.Assisted || response.Assisted,
            Contaminated = e.Contaminated,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Writes the session's evidence: new rows for items not yet stored, and contamination/assistance flags turned
    /// on for stored rows the engine now marks. Objectives are matched by key among the exam's LearningObjective
    /// rows (an exam without objective rows stores no evidence rows); responses by turn index (the latest response
    /// event on the turn). Writes one audit event per kind of change. Does not save.
    /// </summary>
    public static async Task<EvidenceSyncResult> SyncAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, Guid examId,
        IReadOnlyList<CollectedEvidence> evidence, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0) return new EvidenceSyncResult(0, 0, 0, 0, 0, 0, 0);

        var objectives = await db.LearningObjectives.Where(o => o.OrganizationId == organizationId && o.ExamId == examId)
            .Select(o => new { o.Key, o.Id }).ToListAsync(ct);
        var objectiveIds = objectives.GroupBy(o => o.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var turnIds = await db.SessionTurns.Where(t => t.OrganizationId == organizationId && t.SessionId == sessionId)
            .ToDictionaryAsync(t => t.Index, t => t.Id, ct);
        var responses = await db.StudentResponses.Where(r => r.OrganizationId == organizationId && r.SessionId == sessionId).ToListAsync(ct);
        var latestByTurn = responses.GroupBy(r => r.TurnId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Sequence).First());
        var responseIds = responses.Select(r => r.Id).ToList();
        var versions = await db.TranscriptVersions.Where(v => v.OrganizationId == organizationId && responseIds.Contains(v.ResponseId))
            .Select(v => new { v.ResponseId, v.Id, v.Version }).ToListAsync(ct);
        var latestVersion = versions.GroupBy(v => v.ResponseId).ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Version).First().Id);
        var stored = await db.AssessmentEvidence.Where(e => e.OrganizationId == organizationId && e.SessionId == sessionId).ToListAsync(ct);
        var byKey = stored.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());

        int added = 0, contaminated = 0, assisted = 0, noObjective = 0, noResponse = 0, unverified = 0, notVerbatim = 0;
        var newlyContaminated = new List<Guid>();
        foreach (var e in evidence)
        {
            if (!objectiveIds.TryGetValue(e.ObjectiveKey, out var objectiveId)) { noObjective++; continue; }
            if (!turnIds.TryGetValue(e.TurnIndex, out var turnId) || !latestByTurn.TryGetValue(turnId, out var response)) { noResponse++; continue; }
            if (!VerificationMachine.IsVerified(response.VerificationState)) { unverified++; continue; }
            var row = ToRow(e, response, objectiveId, latestVersion.TryGetValue(response.Id, out var v) ? v : null, now);
            if (row is null) { notVerbatim++; continue; }

            if (byKey.TryGetValue(Key(row), out var existing))
            {
                foreach (var x in existing)
                {
                    if (row.Contaminated && !x.Contaminated) { x.Contaminated = true; contaminated++; newlyContaminated.Add(x.Id); }
                    if (row.Assisted && !x.Assisted) { x.Assisted = true; assisted++; }
                }
                continue;
            }
            db.AssessmentEvidence.Add(row);
            byKey[Key(row)] = [row];
            added++;
        }

        if (added > 0) Audit(db, organizationId, sessionId, "evidence.recorded", new { added }, now);
        if (contaminated > 0)
            Audit(db, organizationId, sessionId, "evidence.contaminated", new { rows = newlyContaminated }, now,
                "Given at or after a question that revealed the answer: excluded from independent mastery (Scenario I).");
        if (assisted > 0) Audit(db, organizationId, sessionId, "evidence.assisted", new { rows = assisted }, now);
        return new EvidenceSyncResult(added, contaminated, assisted, noObjective, noResponse, unverified, notVerbatim);
    }

    private static (Guid, Guid, EvidenceStance, AssessmentDimension?, string) Key(AssessmentEvidence e) =>
        (e.ResponseId, e.ObjectiveId, e.Stance, e.Dimension, e.Note);

    private static void Audit(GadflyDbContext db, Guid organizationId, Guid sessionId, string action, object after, DateTimeOffset now, string? reason = null) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorRole = "system",
            Action = action,
            EntityType = nameof(AssessmentEvidence),
            EntityId = sessionId.ToString(),
            SessionId = sessionId,
            AfterJson = JsonSerializer.Serialize(after, Json),
            Reason = reason,
            At = now,
        });
}
