using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Sources.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The path each objective took (GAD-T435; ElenchusAtScale Prop. 3.4(c), M10): moves asked, criteria reached and left out, and
/// every excluded answer by reason; criteria and exclusions hidden while blind grading hides the AI's view. Pure: no database.
/// </summary>
[TestFixture]
public class ObjectivePathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static AnswerJudgment Judged(double score, RecitationFlag? recited = null) => new()
    {
        Criteria = [new CriterionScore { Criterion = "accuracy", Score = score }], OverallScore = score, Recitation = recited,
    };

    private static readonly AnswerJudgment NoVerdict = new() { Criteria = [], OverallScore = 0, NeedsHumanReview = true };

    private static void Turn(SocraticSession s, MoveKind move, AnswerJudgment judgment, string objective = "o1")
    {
        s.Ask("c1", move, "Question?", T0, objectiveKey: objective);
        s.RecordAnswer(new Answer { Text = "An answer.", CompletedAt = T0 });
        s.RecordJudgment(judgment, 0.5);
    }

    private static SocraticSession Session()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        s.StartClock(T0);
        Turn(s, MoveKind.Elicit, Judged(0.8));
        Turn(s, MoveKind.Probe, Judged(0.7, new RecitationFlag { MatchesSource = true, Reason = "close to the source" }));
        Turn(s, MoveKind.Probe, Judged(0.6));
        Turn(s, MoveKind.Apply, NoVerdict);
        Turn(s, MoveKind.Elicit, Judged(0.9), objective: "o2");
        return s;
    }

    private static readonly List<RubricCriterion> Rubric =
    [
        new() { Name = "Definition", Weight = 1m, Ordinal = 0 },
        new() { Name = "Reasoning", Weight = 1m, Ordinal = 1 },
        new() { Name = "Application", Weight = 2m, Ordinal = 2 },
    ];

    private static AiGradeView Ai(params (string Name, MasteryLevel Level)[] levels) => new(new AiObjectiveGrade { Level = MasteryLevel.BasicExplanation },
        levels.Select(l => new CriterionGrade(l.Name, AssessmentDimension.ConceptualUnderstanding, l.Level, 1m, 2m, "", [], [], [], false)).ToList(),
        [], [], [], [], []);

    [Test]
    public void Moves_are_counted_per_kind_for_the_objective_only()
    {
        var path = ObjectivePath.Build(Session(), "o1", Rubric, null, aiVisible: false, assistedCounts: false);
        Assert.That(path.Questions, Is.EqualTo(4));
        Assert.That(path.Moves.Single(m => m.Move == MoveKind.Probe).Count, Is.EqualTo(2));
        Assert.That(path.Moves[0].Move, Is.EqualTo(MoveKind.Elicit));
        Assert.That(path.MovesLine(), Does.StartWith("4 questions"));
    }

    [Test]
    public void A_blind_grader_sees_the_questions_but_not_criteria_or_exclusions()
    {
        var path = ObjectivePath.Build(Session(), "o1", Rubric, Ai(("Definition", MasteryLevel.BasicExplanation)), aiVisible: false, assistedCounts: false);
        Assert.That(path.Criteria, Is.Null);
        Assert.That(path.Exclusions, Is.Null);
        Assert.That(path.CriteriaLine(), Is.Null);
    }

    [Test]
    public void Exclusions_name_their_reason_and_turn()
    {
        var path = ObjectivePath.Build(Session(), "o1", Rubric, null, aiVisible: true, assistedCounts: false);
        Assert.That(path.Exclusions!.Select(e => e.Turn), Is.EqualTo(new[] { 2, 4 }));
        Assert.That(path.Exclusions![0].Reason, Is.EqualTo(ObjectivePath.Recited));
        Assert.That(path.Exclusions![1].Reason, Is.EqualTo(ObjectivePath.Unjudged));
    }

    [Test]
    public void Criteria_left_at_level_0_are_reported_with_the_weight_the_score_covers()
    {
        var path = ObjectivePath.Build(Session(), "o1", Rubric,
            Ai(("Definition", MasteryLevel.BasicExplanation), ("Reasoning", MasteryLevel.InsufficientEvidence)), aiVisible: true, assistedCounts: false);
        Assert.That(path.Criteria!.Select(c => c.Reached), Is.EqualTo(new[] { true, false, false }));
        Assert.That(path.Coverage, Is.EqualTo(0.25m));
        Assert.That(path.CriteriaLine(), Does.Contain("Reached 1 of 3 criteria (25% of the weight)").And.Contain("not reached: Reasoning, Application"));
    }

    [Test]
    public void An_objective_never_asked_says_so()
    {
        var path = ObjectivePath.Build(Session(), "o3", Rubric, null, aiVisible: true, assistedCounts: false);
        Assert.That(path.Questions, Is.EqualTo(0));
        Assert.That(path.MovesLine(), Does.StartWith("not examined"));
    }
}
