using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;

namespace Gadfly.Server.Grading;

/// <summary>One row of the grading queue.</summary>
public sealed record GradingQueueRow(Guid SessionId, string StudentName, string ExamTitle, int Attempt, DateTimeOffset? EndedAt,
    GradeStatus? Status, decimal? FinalPercent, int OpenDisagreements, bool OpenAppeal, string? JobState, string? JobError, bool IsMock);

/// <summary>One response with its full history (Part 8.2: verified responses, originals, versions, clarifications).</summary>
public sealed record ResponseView(StudentResponse Response, int TurnIndex, string ConceptKey, string Question,
    IReadOnlyList<TranscriptVersion> Versions, IReadOnlyList<ResponseVerification> History, IReadOnlyList<ClarificationEvent> Clarifications)
{
    public bool NeedsHuman => VerificationMachine.NeedsHuman(Response.VerificationState);
    public bool TextChanged => !string.Equals(Response.OriginalText, Response.VerifiedText, StringComparison.Ordinal);
}

/// <summary>A stored AI objective grade, parsed for display.</summary>
public sealed record AiGradeView(AiObjectiveGrade Row, IReadOnlyList<CriterionGrade> Criteria, IReadOnlyList<VoterGrade> Voters, IReadOnlyList<Guid> EvidenceRefs,
    IReadOnlyList<NotedMisconception> Misconceptions, IReadOnlyList<string> Ambiguities, IReadOnlyList<string> Flags)
{
    public bool Insufficient => Row.Level == MasteryLevel.InsufficientEvidence;

    public static AiGradeView From(AiObjectiveGrade row)
    {
        var detail = GradingJson.Read<StoredAiDetail>(row.CriteriaJson);
        var criteria = detail?.Criteria ?? GradingJson.ReadList<CriterionGrade>(row.CriteriaJson.TrimStart().StartsWith('[') ? row.CriteriaJson : null);
        return new AiGradeView(row, criteria, detail?.Voters ?? [], GradingJson.ReadList<Guid>(row.EvidenceRefsJson),
            GradingJson.ReadList<NotedMisconception>(row.MisconceptionsJson), GradingJson.ReadList<string>(row.AmbiguitiesJson), GradingJson.ReadList<string>(row.ReviewFlagsJson));
    }
}

/// <summary>Everything about one learning objective in one session.</summary>
public sealed record ObjectiveView(LearningObjective Objective, AiGradeView? Ai, HumanObjectiveGrade? Human, IReadOnlyList<HumanObjectiveGrade> HumanHistory,
    IReadOnlyList<AssessmentEvidence> Evidence, ObjectiveResult? Combined, bool AiExcluded, GradeDisagreement? Disagreement,
    IReadOnlyList<GradeReconciliation> Reconciliations)
{
    public GradeReconciliation? LatestReconciliation => Reconciliations.Count == 0 ? null : Reconciliations[^1];
    public bool NeedsReconciliation => Disagreement is { Band: DisagreementBand.MandatoryReconciliation, ResolvedAt: null };
}

/// <summary>What FinalGrade.ResultJson holds.</summary>
public sealed record StoredResult(CombinedResult Result, IReadOnlyList<string> AiExcludedObjectives, DateTimeOffset ComputedAt);

/// <summary>The grading workspace of one session (Part 8.2 reviewer interface).</summary>
public sealed record GradingWorkspace
{
    public required ExamSession Session { get; init; }
    public required string ExamTitle { get; init; }
    public required string StudentName { get; init; }
    public required FinalGrade? Grade { get; init; }
    public required PolicySnapshot Policy { get; init; }
    /// <summary>False while blind human grading hides the AI's evaluation (Part 8.3).</summary>
    public required bool AiVisible { get; init; }
    public required IReadOnlyList<ResponseView> Responses { get; init; }
    public required IReadOnlyList<ObjectiveView> Objectives { get; init; }
    public required CombinedResult? Combined { get; init; }
    public required IReadOnlyList<(ApprovalRecord Record, string Actor)> Approvals { get; init; }
    public required IReadOnlyList<(GradeAppeal Appeal, string Student)> Appeals { get; init; }
    public required IReadOnlyList<(AuditEvent Event, string Actor)> Audit { get; init; }
    public required IReadOnlyDictionary<Guid, string> Names { get; init; }
    public required IReadOnlyList<(Guid UserId, string Name)> SecondReviewers { get; init; }
    /// <summary>Second reviewers requested per disagreement (from the audit log).</summary>
    public required IReadOnlyDictionary<Guid, Guid> RequestedSecondReviews { get; init; }
    public string? JobState { get; init; }
    public string? JobError { get; init; }
    public required GradingActor Viewer { get; init; }

    public GradeStatus? Status => Grade?.Status;
    public bool AnyMock => Objectives.Any(o => o.Ai?.Row.IsMock == true);
    public bool HasFinalizeApproval => Approvals.Any(a => a.Record.Action == ApprovalAction.Finalize);
    public GradeAppeal? OpenAppeal => Appeals.Select(a => a.Appeal).FirstOrDefault(a => a.State == AppealState.Open);
    public int ResponsesNeedingHuman => Responses.Count(r => r.NeedsHuman);
    public bool AllHumanGraded => Objectives.Count > 0 && Objectives.All(o => o.Human is not null);
    public string Name(Guid? id) => id is { } x && Names.TryGetValue(x, out var n) ? n : "(unknown)";
}

/// <summary>A human examiner's grade for one objective, as entered.</summary>
public sealed record HumanGradeInput(Guid ObjectiveId, MasteryLevel Level, decimal? Score, string? Comment, string? EvidenceNote);

/// <summary>The causes a reconciliation may record (Part 10.3 step 5).</summary>
public static class ReconciliationCauses
{
    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        ("error-in-ai", "Error in the AI evaluation"),
        ("error-in-human", "Error in the human evaluation"),
        ("transcript-ambiguity", "Transcript or meaning ambiguity"),
        ("rubric-interpretation", "Different reading of the rubric"),
        ("both-defensible", "Both evaluations are defensible"),
    ];

    public static bool IsValid(string? code) => All.Any(c => c.Code == code);
    public static string Label(string code) => All.FirstOrDefault(c => c.Code == code).Label ?? code;
}

/// <summary>A student's view of one published result (Stage 11).</summary>
public sealed record StudentResultRow(Guid SessionId, Guid AssignmentId, string ExamTitle, int Attempt, GradeStatus Status, decimal? FinalPercent,
    string? Label, DateTimeOffset PublishedAt, bool OpenAppeal);
