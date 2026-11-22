using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Assessments;

/// <summary>A drafted opening question for the instructor to edit and approve, or why none could be drafted.</summary>
public sealed record OpeningQuestionDraft(bool Ok, string Message, string? Text = null);

/// <summary>
/// Fixed opening questions (GAD-T436; ElenchusAtScale M2, 2.4.3, 4.3.2 correction 31). Every objective opens with Elicit, but
/// the wording used to be new for each student. Here an instructor writes, or has the panel draft, one opening question per
/// objective and approves it; it is then asked word for word to every student, so the one observation all students share is
/// literally the same. Approval runs the leak guard against the objective's concept rubric first and fails closed: a question
/// that gives away expected points, or that the guard cannot clear, is not saved. Stored in the draft exam's policy JSON
/// (<see cref="SessionPolicy.OpeningQuestions"/>); publishing carries it into the policy sessions run on. Every approval and
/// removal is audited. The panel is called only here, while the instructor sets the exam up: in the exam itself the fixed
/// question needs no panel call at all.
/// </summary>
public sealed class OpeningQuestionService(GadflyDbContext db, ITenantContext tenant, ISocraticVotingClient panel, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly AssessmentGuard guard = new(db, tenant, clock);

    private Guid Org => tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");

    /// <summary>
    /// Drafts an opening question for one objective the way the exam would: the examiner's own question pipeline (the panel
    /// words an Elicit question from the concept's source passages, and the leak guard checks it). Nothing is saved: the
    /// instructor edits the draft and approves it.
    /// </summary>
    public async Task<OpeningQuestionDraft> DraftAsync(Guid examId, Guid objectiveId, CancellationToken ct = default)
    {
        var (exam, objective, subject, refusal) = await LoadAsync(examId, objectiveId, "Drafting an opening question", ct);
        if (refusal is not null) return new(false, refusal);
        var policy = AssessmentService.BuildSessionPolicy(exam!, [objective!]) with
        {
            OpeningQuestions = new Dictionary<string, string>(StringComparer.Ordinal),
            ConceptIds = [objective!.ConceptKey!],
        };
        try
        {
            policy.Validate();
            var engine = await SourceExamEngine.StartAsync(panel, subject!, policy, clock, Guid.NewGuid(), ct);
            engine.AssessTextStyle = false;
            var turn = await engine.NextQuestionAsync(ct);
            if (turn is null || turn.Move != MoveKind.Elicit || string.IsNullOrWhiteSpace(turn.Question))
                return new(false, "The examiner could not draft an opening question for this objective. Write one yourself.");
            return new(true, "Drafted. Edit it if you like, then approve it.", turn.Question.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or HttpRequestException)
        {
            return new(false, $"The examiner could not draft a question right now ({ex.Message}). Try again, or write one yourself.");
        }
    }

    /// <summary>
    /// Approves <paramref name="text"/> as the objective's fixed opening question, after the checks every examiner question
    /// gets: it must read as a question, must not ask how the student arrived at an answer (GAD-T445), and the leak guard must
    /// clear it against the concept's rubric (fail closed). Audited.
    /// </summary>
    public async Task<AssessmentResult> ApproveAsync(Guid examId, Guid objectiveId, string? text, CancellationToken ct = default)
    {
        var question = (text ?? "").Trim();
        if (question.Length == 0) return new(false, "Write the opening question first, or draft one.");
        if (question.Length > SessionPolicy.MaxOpeningQuestionLength)
            return new(false, $"Keep the opening question under {SessionPolicy.MaxOpeningQuestionLength} characters: it is spoken aloud.");
        if (question.Contains('\n')) return new(false, "Keep the opening question to one line.");
        if (question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 4)
            return new(false, "That is too short to be an opening question. Ask the student to explain the idea in their own words.");
        if (QuestionGenerator.IsIntrospective(question))
            return new(false, "That asks how the student arrived at an answer, which people cannot reliably report. Ask for the reasons behind a claim instead.");

        var (exam, objective, subject, refusal) = await LoadAsync(examId, objectiveId, "Approving an opening question", ct);
        if (refusal is not null) return new(false, refusal);

        var rubric = subject!.Rubric(objective!.ConceptKey!).Describe();
        if (!string.IsNullOrWhiteSpace(rubric))
        {
            bool? leaks;
            try
            {
                leaks = await new QuestionGenerator(panel).CheckLeakAsync(question, rubric, "", ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                leaks = null;
            }
            if (leaks == true)
                return new(false, "Not approved: the check found that this question states or hints at points the student is expected to give. Ask without them.");
            if (leaks is null)
                return new(false, "Not approved: the check could not confirm that this question gives nothing away. Try again in a moment, or reword it.");
        }

        var tracked = await db.Exams.FirstAsync(e => e.Id == exam!.Id && e.OrganizationId == Org, ct);
        var p = ExamService.ReadPolicy(tracked.PolicyJson);
        var before = p.OpeningQuestions.TryGetValue(objective.Key, out var old) ? old : null;
        if (before == question) return new(true, "That is already this objective's opening question.", exam!.Id);
        var updated = new Dictionary<string, string>(p.OpeningQuestions, StringComparer.Ordinal) { [objective.Key] = question };
        tracked.PolicyJson = JsonSerializer.Serialize(p with { OpeningQuestions = updated }, Json);
        guard.Audit("objective.opening.approved", "LearningObjective", objective.Id.ToString(),
            before is null ? null : new { objective.Key, OpeningQuestion = before },
            new { objective.Key, objective.ConceptKey, OpeningQuestion = question, LeakGuard = string.IsNullOrWhiteSpace(rubric) ? "no rubric" : "safe" },
            "Fixed opening question: asked word for word to every student (GAD-T436).");
        await db.SaveChangesAsync(ct);
        return new(true, "Approved. Every student now hears this as the first question on this objective.", exam!.Id);
    }

    /// <summary>Removes an objective's fixed opening question: the panel words the opening for each student again. Audited.</summary>
    public async Task<AssessmentResult> RemoveAsync(Guid examId, Guid objectiveId, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Removing an opening question", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That assessment no longer exists.");
        if (exam.Status != ExamStatus.Draft) return new(false, Frozen(exam.Status));
        var objective = await db.LearningObjectives.AsNoTracking().FirstOrDefaultAsync(o => o.Id == objectiveId && o.ExamId == examId && o.OrganizationId == org, ct);
        if (objective is null) return new(false, "That objective no longer exists.");
        if (Drop(exam, objective.Key) is not { } old) return new(true, "This objective has no fixed opening question.", examId);
        guard.Audit("objective.opening.removed", "LearningObjective", objective.Id.ToString(), new { objective.Key, OpeningQuestion = old });
        await db.SaveChangesAsync(ct);
        return new(true, "Removed. The examiner words the opening question for each student again.", examId);
    }

    /// <summary>
    /// Takes <paramref name="objectiveKey"/>'s fixed opening question out of a tracked exam's policy (the caller saves and
    /// audits); returns the removed text, or null when it had none. Used when the objective is removed or relinked to another
    /// concept, since the question was checked against that concept's rubric.
    /// </summary>
    public static string? Drop(Exam exam, string objectiveKey)
    {
        var p = ExamService.ReadPolicy(exam.PolicyJson);
        if (!p.OpeningQuestions.TryGetValue(objectiveKey, out var old)) return null;
        var rest = p.OpeningQuestions.Where(kv => kv.Key != objectiveKey).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        exam.PolicyJson = JsonSerializer.Serialize(p with { OpeningQuestions = rest }, Json);
        return old;
    }

    private async Task<(Exam? Exam, LearningObjective? Objective, SourceMaterialSubject? Subject, string? Refusal)> LoadAsync(
        Guid examId, Guid objectiveId, string action, CancellationToken ct)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, action, "Exam", examId.ToString(), ct))
            return (null, null, null, AssessmentGuard.Denied(Permission.ManageAssessments));
        var org = Org;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return (null, null, null, "That assessment no longer exists.");
        if (exam.Status != ExamStatus.Draft) return (null, null, null, Frozen(exam.Status));
        var objective = await db.LearningObjectives.AsNoTracking().Include(o => o.Criteria)
            .FirstOrDefaultAsync(o => o.Id == objectiveId && o.ExamId == examId && o.OrganizationId == org, ct);
        if (objective is null) return (null, null, null, "That objective no longer exists.");
        if (string.IsNullOrWhiteSpace(objective.ConceptKey))
            return (null, null, null, "Link this objective to a source concept first: its opening question is checked against that concept's rubric.");

        var collection = exam.CollectionId.ToString();
        var maps = new SqlConceptMapStore(db, org);
        var map = await maps.LoadMapAsync(collection, ct);
        if (map is null || map.Concepts.All(c => c.Key != objective.ConceptKey))
            return (null, null, null, "The objective's concept is not in the source material's concept map. Relink it first.");
        var rubrics = await maps.LoadRubricsAsync(collection, ct);
        var passages = await new SqlSourceStore(db, org, Guid.Empty, clock).GetPassagesAsync(collection, ct);
        return (exam, objective, new SourceMaterialSubject(exam.Title, map, rubrics, new GroundingRetriever(passages)), null);
    }

    private static string Frozen(ExamStatus status) =>
        $"This assessment is {status.ToString().ToLowerInvariant()}, so its objectives and opening questions are locked.";
}
