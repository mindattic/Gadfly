using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Whole examinations simulated against the planner and the evidence assessor alone (no panel): a scoring rule
/// answers every planned question, and the outcome per objective is checked (GAD-T89..T93; scenarios A, B, H, I
/// and Learning Mode as the engine's own simulation runs them).
/// </summary>
[TestFixture]
public class PlannerSimulationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly QuestionPlanner Planner = new([new PlannerConcept("a", ["b"]), new PlannerConcept("b", ["a"])]);

    private sealed record Run(SocraticSession Session, IReadOnlyList<PlanDecision> Decisions, PlanDecision End, IReadOnlyDictionary<string, ObjectiveAssessment> Result);

    private static Run Simulate(Func<MoveKind, int, double> score, SessionPolicy? policy = null, Action<SocraticSession, int>? hook = null)
    {
        var p = policy ?? new SessionPolicy { ConceptIds = ["a", "b"], MaxTurns = 20, TimeBudget = TimeSpan.FromMinutes(60) };
        var s = new SocraticSession("sim", p, ExamPrompts.Set, T0);
        var decisions = new List<PlanDecision>();
        PlanDecision? end = null;
        for (var i = 0; i < 40; i++)
        {
            var now = T0.AddMinutes(i);
            var d = Planner.Next(s, now);
            if (d.EndsSession) { s.End(d.End, now); end = d; break; }
            decisions.Add(d);
            s.Ask(d.ConceptId!, d.Move, "q", now, d.TargetClaimIds, d.Reason, d.ObjectiveKey, d.IsReplacement);
            s.RecordAnswer(new Answer { Text = "some answer words", CompletedAt = now });
            var sc = score(d.Move, i);
            s.RecordJudgment(new AnswerJudgment { Criteria = sc == 0 ? [] : [new CriterionScore { Criterion = "accuracy", Score = sc }], OverallScore = sc }, 0.5);
            hook?.Invoke(s, i);
        }
        Assert.That(end, Is.Not.Null, "the session must end on its own");
        return new Run(s, decisions, end!, Planner.Assess(s).ToDictionary(a => a.ObjectiveKey));
    }

    [Test]
    public void StrongStudent_EveryObjectiveSufficientAtLevel3OrAbove()
    {
        var r = Simulate((_, _) => 0.9);
        Assert.That(r.End.End, Is.EqualTo(EndReason.EvidenceComplete));
        foreach (var a in r.Result.Values)
        {
            Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Sufficient), a.Explanation);
            Assert.That(a.Level, Is.GreaterThanOrEqualTo(MasteryLevel.ConceptualUnderstanding));
            Assert.That(a.IndependentAnswers, Is.GreaterThanOrEqualTo(2));
        }
        Assert.That(r.Decisions.Count(d => d.Move == MoveKind.Elicit), Is.EqualTo(2), "each objective is opened once");
    }

    [Test]
    public void ScenarioA_CorrectDefinitionWithoutUnderstanding_NeverReachesConceptualUnderstanding()
    {
        var r = Simulate((m, _) => m == MoveKind.Elicit ? 0.9 : 0.2);
        foreach (var a in r.Result.Values)
        {
            Assert.That(a.Level, Is.LessThan(MasteryLevel.ConceptualUnderstanding), a.Explanation);
            Assert.That(a.Questions, Is.GreaterThan(1), "follow-ups always come after the definition");
        }
        Assert.That(QuestionPlanner.StatusOf(r.Session, "a"), Is.Not.EqualTo(ConceptStatus.SettledConceptualOrAbove));
    }

    [Test]
    public void ScenarioB_WrongFirstThenStrong_TheWholeBodyOfEvidenceCounts()
    {
        var r = Simulate((m, i) => i == 0 || m == MoveKind.Clarify ? 0.1 : 0.85);
        var a = r.Result["a"];
        Assert.That(a.Answers.First().Result, Is.EqualTo(AnswerResult.Negative), "the initial error is kept");
        Assert.That(a.ProvisionalLevel, Is.GreaterThanOrEqualTo(MasteryLevel.ConceptualUnderstanding), a.Explanation);
        Assert.That(a.SupportingAnswers, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void ScenarioH_LimitsHitFirst_ReportLevel0()
    {
        var r = Simulate((_, _) => 0.5, new SessionPolicy { ConceptIds = ["a", "b"], MaxTurns = 5, TimeBudget = TimeSpan.FromMinutes(60) });
        Assert.That(r.End.End, Is.EqualTo(EndReason.TurnLimit).Or.EqualTo(EndReason.EvidenceComplete));
        Assert.That(r.Session.Turns, Has.Count.LessThanOrEqualTo(5));
        Assert.That(r.Result.Values.Any(a => a.Status == ObjectiveStatus.Inconclusive && a.Level == MasteryLevel.InsufficientEvidence), Is.True,
            "an objective the limits cut short is Level 0, not a guess");
        Assert.That(r.Result.Values.All(a => a.Level < MasteryLevel.ConceptualUnderstanding), Is.True, "partial answers never show more than Level 2");
    }

    [Test]
    public void ScenarioI_LeakOnTheSecondTurn_ContaminatesAndIsReplaced()
    {
        var r = Simulate((_, _) => 0.9, hook: (s, i) => { if (i == 1) s.FlagLeak(1, new LeakFlag(LeakSource.Analysis, "gave it away", T0)); });
        var leaked = r.Session.Turns[1];
        var key = ExamObjectives.KeyOf(leaked);
        Assert.That(r.Result[key].ContaminatedAnswers, Is.GreaterThanOrEqualTo(1));
        Assert.That(r.Result[key].LeakedTurns, Is.EqualTo(new[] { 1 }));
        var replacements = r.Session.Turns.Where(t => t.IsReplacement && ExamObjectives.KeyOf(t) == key).ToList();
        Assert.That(replacements, Is.Not.Empty, "a replacement question follows the leak");
        Assert.That(replacements.All(t => ObjectiveAssessor.LeakResistantMoves.Contains(t.Move)), Is.True);
    }

    [Test]
    public void LearningMode_HintsThenReassessment_ReportedAsAssisted()
    {
        var r = Simulate((m, i) => MoveProfiles.IsAssistance(m) ? 0.7 : (i < 3 ? 0.2 : 0.8),
            new SessionPolicy { ConceptIds = ["a"], Mode = AssessmentMode.Learning, MaxTurns = 12, TimeBudget = TimeSpan.FromMinutes(60) });
        var a = r.Result["a"];
        Assert.That(r.Session.Turns.Any(t => t.Move == MoveKind.Hint), Is.True, "a weak answer in Learning Mode gets a hint");
        Assert.That(a.AssistsGiven, Is.InRange(1, r.Session.Policy.MaxAssistsPerObjective));
        Assert.That(a.AssistedAnswers, Is.GreaterThan(0), "everything after the first hint is assisted");
        Assert.That(a.AssistedLevel, Is.Not.Null);
        var firstHint = r.Session.Turns.First(t => t.Move == MoveKind.Hint).Index;
        Assert.That(a.Answers.Where(o => o.TurnIndex >= firstHint).All(o => o.Assisted), Is.True);
    }

    [Test]
    public void Accommodation_TimeMultiplierStretchesTheSession()
    {
        var plain = Simulate((_, _) => 0.5, new SessionPolicy { ConceptIds = ["a", "b"], TimeBudget = TimeSpan.FromMinutes(4), MaxTurns = 30 });
        var extended = Simulate((_, _) => 0.5, new SessionPolicy { ConceptIds = ["a", "b"], TimeBudget = TimeSpan.FromMinutes(4), TimeMultiplier = 1.5, MaxTurns = 30 });
        Assert.That(extended.Session.Turns.Count, Is.GreaterThanOrEqualTo(plain.Session.Turns.Count));
        Assert.That(extended.Session.Policy.EffectiveTimeBudget, Is.EqualTo(TimeSpan.FromMinutes(6)));
    }

    [Test]
    public void HintsOutsideLearningMode_AreRefused()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        Assert.Throws<InvalidOperationException>(() => s.Ask("a", MoveKind.Hint, "hint?", T0));
        var learning = new SocraticSession("x", new SessionPolicy { Mode = AssessmentMode.Learning }, ExamPrompts.Set, T0);
        Assert.DoesNotThrow(() => learning.Ask("a", MoveKind.Hint, "hint?", T0));
    }
}
