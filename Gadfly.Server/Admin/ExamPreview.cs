using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Sessions;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates.Mock;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Admin;

/// <summary>One exchange of a preview, as the preview page shows it (with the examiner's reasoning, which a student never sees).</summary>
public sealed record PreviewTurn(int Number, string ConceptTitle, string Move, string Question, string? Why, string? Answer, string? Note);

/// <summary>
/// "Preview as a student" (GAD-T297): the exam's real question engine (its planner, its objectives, rubric and concept map, its
/// session settings) run entirely in memory against the clearly labelled mock panel, whatever the deployment's AI mode.
/// Nothing is written: no assignment, no session, no turns, no grades, no usage, no integrity notes. So a preview can never be a
/// counted attempt, and no grading queue, dashboard, analytics or report can ever see one; closing the page discards it.
/// The only trace is one audit row, assessment.previewed, written when it starts.
/// </summary>
public sealed class ExamPreview
{
    private readonly SourceExamEngine engine;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);

    private ExamPreview(SourceExamEngine engine, TimeProvider clock, string title, Guid examId)
    {
        this.engine = engine;
        this.clock = clock;
        Title = title;
        ExamId = examId;
    }

    public Guid ExamId { get; }
    public string Title { get; }
    public bool IsEnded => engine.Session.IsEnded;
    public string? EndReason => engine.Session.IsEnded ? engine.Session.EndReason.ToString() : null;
    public TimeSpan Remaining => engine.Session.Remaining(clock.GetUtcNow());
    public Turn? OpenTurn => engine.Session.OpenTurn;
    public int ObjectivesTotal => engine.Objectives.Count;
    public int ObjectivesClosed => engine.Assessments().Count(a => a.IsClosed);

    /// <summary>
    /// Builds a preview of an exam in the caller's organization, from the database the caller's scope gives. Throws
    /// <see cref="InvalidOperationException"/> with a message to show when the exam can't be previewed yet.
    /// </summary>
    public static async Task<ExamPreview> StartAsync(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, Guid examId, CancellationToken ct = default)
    {
        var guard = new AssessmentGuard(db, tenant, clock);
        if (!await guard.RequireAsync(Gadfly.Assessment.Permission.ManageAssessments, "Previewing an exam", "Exam", examId.ToString(), ct))
            throw new InvalidOperationException(AssessmentGuard.Denied(Gadfly.Assessment.Permission.ManageAssessments));
        var org = tenant.OrganizationId ?? throw new InvalidOperationException("No active organization.");
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct)
            ?? throw new InvalidOperationException("That exam no longer exists.");

        var id = exam.CollectionId.ToString();
        var maps = new SqlConceptMapStore(db, org);
        var map = await maps.LoadMapAsync(id, ct);
        if (map is null || map.Concepts.Count == 0)
            throw new InvalidOperationException("Its source material has no concept map yet, so there is nothing to ask about. Process the source material first.");
        var rubrics = await maps.LoadRubricsAsync(id, ct);
        var passages = await new SqlSourceStore(db, org, Guid.Empty, clock).GetPassagesAsync(id, ct);
        var subject = new SourceMaterialSubject(exam.Title, map, rubrics, new GroundingRetriever(passages));

        // A draft's objectives live in their rows until publishing writes them into the policy: build it the way publishing will.
        var objectives = await db.LearningObjectives.AsNoTracking().Include(o => o.Criteria)
            .Where(o => o.ExamId == examId && o.OrganizationId == org).OrderBy(o => o.Ordinal).ToListAsync(ct);
        var basis = exam.Status == ExamStatus.Draft && objectives.Count > 0
            ? AssessmentService.BuildSessionPolicy(exam, objectives)
            : ExamService.ReadPolicy(exam.PolicyJson);
        var policy = await SessionObjectives.ForSessionAsync(db, basis, exam, 1m, ct);
        var known = map.Concepts.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        policy = policy with
        {
            ConceptIds = policy.ConceptIds.Where(known.Contains).ToList() is { Count: > 0 } keys ? keys : map.Concepts.Select(c => c.Key).ToList(),
            Objectives = policy.Objectives.Select(o => o with { ConceptKeys = o.ConceptKeys.Where(known.Contains).ToList() }).ToList(),
        };
        try
        {
            policy.Validate();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Its settings are not valid yet: {ex.Message}");
        }

        var panel = new MockVotingClient { Reason = "Exam preview: always the mock panel, so a preview never calls an AI provider." };
        var engine = await SourceExamEngine.StartAsync(panel, subject, policy, clock, Guid.NewGuid(), ct);
        engine.AssessTextStyle = false;   // integrity signals have no meaning in a preview

        guard.Audit("assessment.previewed", "Exam", exam.Id.ToString(), after: new { exam.Title, Status = exam.Status.ToString() },
            reason: "Preview as a student: run in memory on the mock panel; nothing about it was saved.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new ExamPreview(engine, clock, exam.Title, exam.Id);
    }

    /// <summary>The question waiting for an answer (planning and wording a new one when needed); null once the preview has ended.</summary>
    public async Task<Turn?> NextAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await engine.NextQuestionAsync(ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>Answers the waiting question as a student would, then returns the next question (null once it has ended).</summary>
    public async Task<Turn?> AnswerAsync(string text, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (engine.Session.IsEnded || engine.Session.OpenTurn is null) return null;
            var now = clock.GetUtcNow();
            await engine.SubmitAnswerAsync(new Answer { Text = string.IsNullOrWhiteSpace(text) ? SourceExamEngine.AporiaWord : text.Trim(), StartedAt = now, CompletedAt = now }, ct);
            return await engine.NextQuestionAsync(ct);
        }
        finally { gate.Release(); }
    }

    public void End()
    {
        if (!engine.Session.IsEnded) engine.End(Gadfly.Engine.EndReason.Abandoned);
    }

    /// <summary>The exchanges so far, oldest first.</summary>
    public IReadOnlyList<PreviewTurn> Turns() => engine.Session.Turns.Select(t => new PreviewTurn(t.Index + 1, ConceptTitle(t.ConceptId), Words(t.Move),
        t.Question, string.IsNullOrWhiteSpace(t.Reason) ? null : t.Reason, t.Answer?.Text,
        t.Judgment is { } j ? j.Summary : null)).ToList();

    public string ConceptTitle(string key)
    {
        try { return engine.Subject.Concept(key).Title; }
        catch (KeyNotFoundException) { return key; }
    }

    /// <summary>A move in plain words.</summary>
    public static string Words(MoveKind move) => move switch
    {
        MoveKind.Elicit => "Opening question",
        MoveKind.Hint => "Hint (practice)",
        MoveKind.GuidedExplanation => "Explains, then asks again (practice)",
        _ => System.Text.RegularExpressions.Regex.Replace(move.ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant() is var s && s.Length > 0
            ? char.ToUpperInvariant(s[0]) + s[1..] : move.ToString(),
    };
}
