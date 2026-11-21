using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>The planner's move selection and every stop rule (mastery, aporia, time, turn limit, per-concept limit, synthesize).</summary>
[TestFixture]
public class QuestionPlannerTests
{
    private static QuestionPlanner Planner(params string[] ids) => new(ids.Select(id => new PlannerConcept(id)).ToList());

    private static QuestionPlanner Related() => new(
    [
        new PlannerConcept("a", ["b"]),
        new PlannerConcept("b", ["a"]),
    ]);

    [Test]
    public void NeedsAtLeastOneConcept() => Assert.Throws<ArgumentException>(() => new QuestionPlanner([]));

    [Test]
    public void ConceptIdsMustBeUnique() => Assert.Throws<ArgumentException>(() => Planner("a", "a"));

    [Test]
    public void FirstMove_ElicitsTheFirstConcept()
    {
        var d = Planner("a", "b").Next(Session(), T0);
        Assert.That(d.EndsSession, Is.False);
        Assert.That(d.ConceptId, Is.EqualTo("a"));
        Assert.That(d.Move, Is.EqualTo(MoveKind.Elicit));
        Assert.That(d.Reason, Is.Not.Empty);
    }

    [Test]
    public void PolicyConceptOrder_Wins()
    {
        var s = Session(new SessionPolicy { ConceptIds = ["b", "a"] });
        Assert.That(Planner("a", "b").Next(s, T0).ConceptId, Is.EqualTo("b"));
    }

    [Test]
    public void PolicyNamingAnUnknownConcept_IsRefused()
    {
        var s = Session(new SessionPolicy { ConceptIds = ["zzz"] });
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(s, T0));
    }

    [Test]
    public void CannotPlanWhileATurnIsUnanswered()
    {
        var s = Session();
        s.Ask("a", MoveKind.Elicit, "Q?", T0);
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(s, T0));
    }

    [Test]
    public void CannotPlanWhileATurnIsUnjudged()
    {
        var s = Session();
        s.AskAnswer("a", MoveKind.Elicit);
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(s, T0));
    }

    [Test]
    public void EndedSession_ReturnsItsEndReason()
    {
        var s = Session();
        s.End(EndReason.Abandoned, T0);
        var d = Planner("a").Next(s, T0);
        Assert.That(d.EndsSession, Is.True);
        Assert.That(d.End, Is.EqualTo(EndReason.Abandoned));
    }

    [Test]
    public void TimeUp_EndsTheSession()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(10) });
        Assert.That(Planner("a").Next(s, T0.AddMinutes(10)).End, Is.EqualTo(EndReason.TimeUp));
        Assert.That(Planner("a").Next(s, T0.AddMinutes(30)).End, Is.EqualTo(EndReason.TimeUp));
        Assert.That(Planner("a").Next(s, T0.AddMinutes(9.9)).EndsSession, Is.False);
    }

    [Test]
    public void TurnLimit_EndsTheSession_EvenMidConcept()
    {
        var s = Session(new SessionPolicy { MaxTurns = 2 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Probe, 0.5, 0.5);
        var d = Planner("a", "b").Next(s, T0.AddMinutes(1));
        Assert.That(d.End, Is.EqualTo(EndReason.TurnLimit));
    }

    [Test]
    public void TimeIsCheckedBeforeTheTurnLimit()
    {
        var s = Session(new SessionPolicy { MaxTurns = 1, TimeBudget = TimeSpan.FromMinutes(1) });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(Planner("a").Next(s, T0.AddMinutes(2)).End, Is.EqualTo(EndReason.TimeUp));
    }

    [Test]
    public void UnclearAnswer_GetsAClarification()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.2, 0.4);
        var d = Planner("a").Next(s, T0);
        Assert.That(d.Move, Is.EqualTo(MoveKind.Clarify));
        Assert.That(d.ConceptId, Is.EqualTo("a"));
    }

    [Test]
    public void NeverClarifiesTwiceInARow()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.2, 0.4);
        s.Judged("a", MoveKind.Clarify, 0.2, 0.35);
        Assert.That(Planner("a").Next(s, T0).Move, Is.EqualTo(MoveKind.Probe));
    }

    [TestCase(0.34, MoveKind.Clarify)]
    [TestCase(0.35, MoveKind.Probe)]
    [TestCase(0.69, MoveKind.Probe)]
    [TestCase(0.7, MoveKind.Counterexample)]
    public void ScoreThresholds_PickTheFollowUp(double score, MoveKind expected)
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, score, 0.5);
        Assert.That(Planner("a").Next(s, T0).Move, Is.EqualTo(expected));
    }

    [Test]
    public void StrongAnswers_PushHarder_CounterexampleThenApply()
    {
        var s = Session();
        var p = Planner("a");
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        Assert.That(p.Next(s, T0).Move, Is.EqualTo(MoveKind.Counterexample));
        s.Judged("a", MoveKind.Counterexample, 0.9, 0.7);
        Assert.That(p.Next(s, T0).Move, Is.EqualTo(MoveKind.Apply));
        s.Judged("a", MoveKind.Apply, 0.9, 0.75);
        Assert.That(p.Next(s, T0).Move, Is.EqualTo(MoveKind.Apply), "both used: another transfer case");
    }

    [Test]
    public void PendingContradiction_IsConfrontedFirst()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        var d = Planner("a").Next(s, T0, [new ContradictionCandidate("a", "c1", "c2")]);
        Assert.That(d.Move, Is.EqualTo(MoveKind.Contradiction));
        Assert.That(d.TargetClaimIds, Is.EqualTo(new[] { "c1", "c2" }));
    }

    [Test]
    public void ContradictionOnAnotherConcept_IsIgnored()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        var d = Planner("a", "b").Next(s, T0, [new ContradictionCandidate("b", "c1", "c2")]);
        Assert.That(d.Move, Is.EqualTo(MoveKind.Probe));
    }

    [Test]
    public void NeverTwoContradictionsInARow()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Contradiction, 0.5, 0.5, targets: ["c1", "c2"]);
        var d = Planner("a").Next(s, T0, [new ContradictionCandidate("a", "c3", "c4")]);
        Assert.That(d.Move, Is.Not.EqualTo(MoveKind.Contradiction));
    }

    [Test]
    public void AClaimAlreadyConfronted_IsNotConfrontedAgain()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Contradiction, 0.5, 0.5, targets: ["c1", "c2"]);
        s.Judged("a", MoveKind.Probe, 0.5, 0.5);
        var d = Planner("a").Next(s, T0, [new ContradictionCandidate("a", "c2", "c5"), new ContradictionCandidate("a", "c6", "c7")]);
        Assert.That(d.Move, Is.EqualTo(MoveKind.Contradiction));
        Assert.That(d.TargetClaimIds, Is.EqualTo(new[] { "c6", "c7" }));
    }

    [Test]
    public void Mastery_NeedsTwoJudgedTurns()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.95);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Open), "one strong answer is not mastery (Scenario A)");
        s.Judged("a", MoveKind.Counterexample, 1, 0.95);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Mastered));
    }

    [Test]
    public void AllConceptsMastered_EndsWithMastery()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.85);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        var d = Planner("a").Next(s, T0.AddMinutes(1));
        Assert.That(d.End, Is.EqualTo(EndReason.Mastery));
    }

    [Test]
    public void MasteryThreshold_IsInclusive()
    {
        var s = Session(new SessionPolicy { MasteryThreshold = 0.8 });
        s.Judged("a", MoveKind.Elicit, 1, 0.8);
        s.Judged("a", MoveKind.Probe, 1, 0.8);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Mastered));
    }

    [Test]
    public void Aporia_NeedsTheMinimumTurns()
    {
        var s = Session(new SessionPolicy { MinTurnsBeforeAporia = 3 });
        s.Judged("a", MoveKind.Elicit, 0, 0.2);
        s.Judged("a", MoveKind.Clarify, 0, 0.1);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Open));
        s.Judged("a", MoveKind.Probe, 0, 0.05);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Aporia));
    }

    [Test]
    public void AporiaThreshold_IsInclusive()
    {
        var s = Session(new SessionPolicy { AporiaThreshold = 0.25, MinTurnsBeforeAporia = 1 });
        s.Judged("a", MoveKind.Elicit, 0, 0.25);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Aporia));
    }

    [Test]
    public void EveryConceptClosed_WithOneInAporia_EndsWithAporia()
    {
        var s = Session(new SessionPolicy { MinTurnsBeforeAporia = 1 });
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 0, 0.1);
        Assert.That(Planner("a", "b").Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.Aporia));
    }

    [Test]
    public void PerConceptLimit_MovesOnToTheNextConcept()
    {
        var s = Session(new SessionPolicy { MaxTurnsPerConcept = 2 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Probe, 0.5, 0.5);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Exhausted));
        var d = Planner("a", "b").Next(s, T0);
        Assert.That(d.ConceptId, Is.EqualTo("b"));
        Assert.That(d.Move, Is.EqualTo(MoveKind.Elicit));
    }

    [Test]
    public void EveryConceptExhausted_EndsAtTheTurnLimit()
    {
        var s = Session(new SessionPolicy { MaxTurnsPerConcept = 1 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(Planner("a").Next(s, T0).End, Is.EqualTo(EndReason.TurnLimit));
    }

    [Test]
    public void StatusOf_NotStartedThenOpen()
    {
        var s = Session();
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.NotStarted));
        s.Ask("a", MoveKind.Elicit, "Q?", T0);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Open));
    }

    [Test]
    public void TwoMasteredRelatedConcepts_AreSynthesized()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        var d = Related().Next(s, T0.AddMinutes(2));
        Assert.That(d.Move, Is.EqualTo(MoveKind.Synthesize));
        Assert.That(d.ConceptId, Is.EqualTo("a"));
        Assert.That(d.RelatedConceptId, Is.EqualTo("b"));
    }

    [Test]
    public void Synthesize_OncePerConcept_ThenEnds()
    {
        var s = Session();
        var p = Related();
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        s.Judged("a", MoveKind.Synthesize, 1, 0.92);
        var second = p.Next(s, T0.AddMinutes(3));
        Assert.That(second.Move, Is.EqualTo(MoveKind.Synthesize));
        Assert.That(second.ConceptId, Is.EqualTo("b"));
        s.Judged("b", MoveKind.Synthesize, 1, 0.92);
        Assert.That(p.Next(s, T0.AddMinutes(4)).End, Is.EqualTo(EndReason.Mastery));
    }

    [Test]
    public void NoSynthesis_WhenLessThanAQuarterOfTheTimeIsLeft()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) });
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        Assert.That(Related().Next(s, T0.AddMinutes(16)).End, Is.EqualTo(EndReason.Mastery));
    }

    [Test]
    public void NoSynthesis_WithFewerThanTwoTurnsLeft()
    {
        var s = Session(new SessionPolicy { MaxTurns = 5 });
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        Assert.That(Related().Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.Mastery));
    }

    [Test]
    public void NoSynthesis_ThatWouldStarveAnUnstartedConcept()
    {
        var p = new QuestionPlanner([new PlannerConcept("a", ["b"]), new PlannerConcept("b", ["a"]), new PlannerConcept("c")]);
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) });
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        var late = p.Next(s, T0.AddMinutes(11));   // under half the budget left: open c instead
        Assert.That(late.ConceptId, Is.EqualTo("c"));
        Assert.That(late.Move, Is.EqualTo(MoveKind.Elicit));
        var early = p.Next(s, T0.AddMinutes(5));
        Assert.That(early.Move, Is.EqualTo(MoveKind.Synthesize));
    }

    [Test]
    public void UnrelatedMasteredConcepts_AreNotSynthesized()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Counterexample, 1, 0.9);
        s.Judged("b", MoveKind.Elicit, 1, 0.9);
        s.Judged("b", MoveKind.Counterexample, 1, 0.9);
        Assert.That(Planner("a", "b").Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.Mastery));
    }

    [Test]
    public void Deterministic_SameInputsSameDecision()
    {
        PlanDecision Run()
        {
            var s = Session();
            s.Judged("a", MoveKind.Elicit, 0.8, 0.6);
            s.Judged("a", MoveKind.Counterexample, 0.4, 0.55);
            return Planner("a", "b").Next(s, T0.AddMinutes(3), [new ContradictionCandidate("a", "c1", "c2")]);
        }
        var x = Run();
        var y = Run();
        Assert.That(y.Move, Is.EqualTo(x.Move));
        Assert.That(y.ConceptId, Is.EqualTo(x.ConceptId));
        Assert.That(y.TargetClaimIds, Is.EqualTo(x.TargetClaimIds));
        Assert.That(y.Reason, Is.EqualTo(x.Reason));
    }

    [Test]
    public void PlanDecision_Ending()
    {
        var d = PlanDecision.Ending(EndReason.TimeUp, "why");
        Assert.That(d.EndsSession, Is.True);
        Assert.That(d.Reason, Is.EqualTo("why"));
        Assert.That(new PlanDecision().EndsSession, Is.False);
    }
}
