using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// The examination control engine (GAD-T89/T90): objective-driven move selection and every stop rule (time, turn
/// limit, evidence sufficiency, per-objective limit, leaving room for unstarted objectives, synthesis), with
/// turns judged by score only (no panel analysis), as the planner's fallback path sees them.
/// </summary>
[TestFixture]
public class QuestionPlannerTests
{
    private static QuestionPlanner Planner(params string[] ids) => new(ids.Select(id => new PlannerConcept(id)).ToList());

    private static QuestionPlanner Related() => new([new PlannerConcept("a", ["b"]), new PlannerConcept("b", ["a"])]);

    /// <summary>Four strong answers that corroborate Level 5 (positive at 2, 4 and twice at 5): sufficient evidence.</summary>
    private static void Excellent(SocraticSession s, string concept, int minute = 0)
    {
        s.Judged(concept, MoveKind.Elicit, 0.9, 0.6, minute);
        s.Judged(concept, MoveKind.Apply, 0.9, 0.7, minute);
        s.Judged(concept, MoveKind.Counterexample, 0.9, 0.8, minute);
        s.Judged(concept, MoveKind.AssumptionTesting, 0.9, 0.85, minute);
    }

    [Test]
    public void NeedsAtLeastOneConcept() => Assert.Throws<ArgumentException>(() => new QuestionPlanner([]));

    [Test]
    public void ConceptIdsMustBeUnique() => Assert.Throws<ArgumentException>(() => Planner("a", "a"));

    [Test]
    public void FirstMove_ElicitsTheFirstObjective()
    {
        var d = Planner("a", "b").Next(Session(), T0);
        Assert.That(d.EndsSession, Is.False);
        Assert.That(d.ConceptId, Is.EqualTo("a"));
        Assert.That(d.ObjectiveKey, Is.EqualTo("a"));
        Assert.That(d.Move, Is.EqualTo(MoveKind.Elicit));
        Assert.That(d.Reason, Is.Not.Empty);
    }

    [Test]
    public void PolicyConceptOrder_Wins() =>
        Assert.That(Planner("a", "b").Next(Session(new SessionPolicy { ConceptIds = ["b", "a"] }), T0).ConceptId, Is.EqualTo("b"));

    [Test]
    public void PolicyNamingAnUnknownConcept_IsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(Session(new SessionPolicy { ConceptIds = ["zzz"] }), T0));

    [Test]
    public void ObjectivesMustBeGroundedInKnownConcepts()
    {
        var ungrounded = new SessionPolicy { Objectives = [new ExamObjective { Key = "o1", Title = "O1" }] };
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(Session(ungrounded), T0));
        var unknown = new SessionPolicy { Objectives = [new ExamObjective { Key = "o1", Title = "O1", ConceptKeys = ["zzz"] }] };
        Assert.Throws<InvalidOperationException>(() => Planner("a").Next(Session(unknown), T0));
    }

    [Test]
    public void ExplicitObjective_RotatesItsGroundingConcepts()
    {
        var policy = new SessionPolicy { Objectives = [new ExamObjective { Key = "energy", Title = "Energy", ConceptKeys = ["a", "b"] }] };
        var s = Session(policy);
        var p = Planner("a", "b");
        var first = p.Next(s, T0);
        Assert.That(first.ObjectiveKey, Is.EqualTo("energy"));
        Assert.That(first.ConceptId, Is.EqualTo("a"));
        s.Ask(first.ConceptId!, first.Move, "Q?", T0, objectiveKey: first.ObjectiveKey);
        s.RecordAnswer(new Answer { Text = "x", CompletedAt = T0 });
        s.RecordJudgment(Judgment(0.5), 0.5);
        var second = p.Next(s, T0);
        Assert.That(second.ObjectiveKey, Is.EqualTo("energy"));
        Assert.That(second.ConceptId, Is.EqualTo("b"), "grounding rotates to the concept asked least");
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
        Assert.That(Planner("a").Next(s, T0).End, Is.EqualTo(EndReason.Abandoned));
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
    public void TimeMultiplier_ExtendsTheBudget()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(10), TimeMultiplier = 1.5 });
        Assert.That(s.Remaining(T0), Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(Planner("a").Next(s, T0.AddMinutes(12)).EndsSession, Is.False, "an accommodation is honored");
        Assert.That(Planner("a").Next(s, T0.AddMinutes(15)).End, Is.EqualTo(EndReason.TimeUp));
    }

    [Test]
    public void TurnLimit_EndsTheSession_EvenMidObjective()
    {
        var s = Session(new SessionPolicy { MaxTurns = 2 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Probe, 0.5, 0.5);
        Assert.That(Planner("a", "b").Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.TurnLimit));
    }

    [Test]
    public void TimeIsCheckedBeforeTheTurnLimit()
    {
        var s = Session(new SessionPolicy { MaxTurns = 1, TimeBudget = TimeSpan.FromMinutes(1) });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(Planner("a").Next(s, T0.AddMinutes(2)).End, Is.EqualTo(EndReason.TimeUp));
    }

    [TestCase(0.2, true)]
    [TestCase(0.34, true)]
    [TestCase(0.35, false)]
    [TestCase(0.9, false)]
    public void UnclearAnswer_GetsANeutralClarification(double score, bool clarify)
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, score, 0.5);
        var d = Planner("a").Next(s, T0);
        Assert.That(d.Move == MoveKind.Clarify, Is.EqualTo(clarify));
        Assert.That(d.ConceptId, Is.EqualTo("a"));
    }

    [Test]
    public void NeverClarifiesTwiceInARow()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.2, 0.4);
        s.Judged("a", MoveKind.Clarify, 0.2, 0.35);
        Assert.That(Planner("a").Next(s, T0).Move, Is.Not.EqualTo(MoveKind.Clarify));
    }

    [Test]
    public void AnUnjudgedAnswer_IsNotClarified()
    {
        var s = Session();
        s.AskAnswer("a", MoveKind.Elicit);
        s.RecordJudgment(new AnswerJudgment { Criteria = [], OverallScore = 0, NeedsHumanReview = true }, 0.5);
        Assert.That(Planner("a").Next(s, T0).Move, Is.Not.EqualTo(MoveKind.Clarify), "no verdict is not an unclear answer");
    }

    [Test]
    public void OneStrongAnswer_IsNeverEnough_TheFollowUpProbesHigher()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1.0, 0.7);
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Open));
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Open));
        var d = Planner("a").Next(s, T0);
        Assert.That(MoveProfiles.Strategies, Does.Contain(d.Move));
        Assert.That(MoveProfiles.ProbedLevel(d.Move), Is.GreaterThan(MoveProfiles.ProbedLevel(MoveKind.Elicit)));
        Assert.That(d.Reason, Does.Contain("one answer never establishes"));
    }

    [Test]
    public void FollowUps_AvoidRepeatingTheLastMove()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        s.Judged("a", MoveKind.Apply, 0.9, 0.7);
        Assert.That(Planner("a").Next(s, T0).Move, Is.Not.EqualTo(MoveKind.Apply));
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
    public void ContradictionOnAnotherObjective_IsIgnored()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(Planner("a", "b").Next(s, T0, [new ContradictionCandidate("b", "c1", "c2")]).Move, Is.Not.EqualTo(MoveKind.Contradiction));
    }

    [Test]
    public void NeverTwoContradictionsInARow()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Contradiction, 0.5, 0.5, targets: ["c1", "c2"]);
        Assert.That(Planner("a").Next(s, T0, [new ContradictionCandidate("a", "c3", "c4")]).Move, Is.Not.EqualTo(MoveKind.Contradiction));
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
    public void AnUnconfrontedContradiction_BlocksSufficiency()
    {
        var s = Session();
        Excellent(s, "a");
        var o = ExamObjectives.Implicit("a", "a");
        Assert.That(ObjectiveAssessor.Assess(s, o).Status, Is.EqualTo(ObjectiveStatus.Sufficient));
        var pending = ObjectiveAssessor.Assess(s, o, [new ContradictionCandidate("a", "c1", "c2")]);
        Assert.That(pending.Status, Is.EqualTo(ObjectiveStatus.Open));
        Assert.That(pending.UnconfrontedContradictions, Is.EqualTo(1));
        Assert.That(pending.Consistent, Is.False);
    }

    [Test]
    public void CorroboratedEvidence_IsSufficient_AndTheSessionEndsEvidenceComplete()
    {
        var s = Session();
        Excellent(s, "a");
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Sufficient));
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.TransferAndCriticalReasoning));
        Assert.That(a.Corroborated && a.ReasoningShown && a.TransferShown && a.CriticalReasoningShown, Is.True);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Mastered));
        Assert.That(Planner("a").Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.EvidenceComplete));
    }

    [Test]
    public void SufficientBelowLevel3_ReadsAsAporia()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        s.Judged("a", MoveKind.Probe, 0.9, 0.65);
        s.Judged("a", MoveKind.Apply, 0.1, 0.5);
        s.Judged("a", MoveKind.ScenarioVariation, 0.1, 0.4);
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Sufficient), a.Explanation);
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.BasicExplanation));
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Aporia));
    }

    [Test]
    public void PerObjectiveLimit_ClosesItInconclusive_AndMovesOn()
    {
        var s = Session(new SessionPolicy { MaxTurnsPerConcept = 2 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        s.Judged("a", MoveKind.Probe, 0.5, 0.5);
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Inconclusive));
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence), "never a guessed level");
        Assert.That(a.Explanation, Does.Contain("Insufficient Evidence"));
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Exhausted));
        var d = Planner("a", "b").Next(s, T0);
        Assert.That((d.ConceptId, d.Move), Is.EqualTo(("b", MoveKind.Elicit)));
        Assert.That(Planner("a").Next(s, T0).End, Is.EqualTo(EndReason.EvidenceComplete));
    }

    [Test]
    public void LeavesRoomForUnstartedObjectives()
    {
        var s = Session(new SessionPolicy { MaxTurns = 3 });
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        var d = Planner("a", "b").Next(s, T0);
        Assert.That((d.ConceptId, d.Move), Is.EqualTo(("b", MoveKind.Elicit)));
        Assert.That(d.Reason, Does.Contain("Leaving"));
    }

    [Test]
    public void EndedSession_LeavesOpenObjectivesInconclusive()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        s.End(EndReason.TimeUp, T0.AddMinutes(20));
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.Status, Is.EqualTo(ObjectiveStatus.Inconclusive));
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
    }

    [Test]
    public void StatusOf_NotStartedThenOpen()
    {
        var s = Session();
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.NotStarted));
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(QuestionPlanner.StatusOf(s, "a"), Is.EqualTo(ConceptStatus.Open));
    }

    [Test]
    public void LeakedQuestion_ContaminatesWhatFollows_AndGetsAReplacement()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.6);
        s.Judged("a", MoveKind.Probe, 0.9, 0.7);
        Assert.That(s.FlagLeak(0, new LeakFlag(LeakSource.Reviewer, "stated the definition", T0)), Is.True);
        Assert.That(s.FlagLeak(0, new LeakFlag(LeakSource.Reviewer, "again", T0)), Is.False, "the first flag stands");
        var a = ObjectiveAssessor.Assess(s, ExamObjectives.Implicit("a", "a"));
        Assert.That(a.IndependentAnswers, Is.Zero);
        Assert.That(a.ContaminatedAnswers, Is.EqualTo(2));
        Assert.That(a.LeakedTurns, Is.EqualTo(new[] { 0 }));
        Assert.That(a.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
        var d = Planner("a").Next(s, T0);
        Assert.That(d.IsReplacement, Is.True);
        Assert.That(ObjectiveAssessor.LeakResistantMoves, Does.Contain(d.Move));
    }

    [Test]
    public void Capacity_IsTheLowerOfTurnsAndTimeAtThePace()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20), MaxTurns = 40 });
        Assert.That(QuestionPlanner.Capacity(s, T0), Is.EqualTo(13), "20 minutes at the assumed 90 s a turn");
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        Assert.That(QuestionPlanner.Capacity(s, T0.AddMinutes(5)), Is.EqualTo(3), "15 minutes left at 5 minutes a turn");
        Assert.That(QuestionPlanner.Capacity(Session(new SessionPolicy { MaxTurns = 4 }), T0), Is.EqualTo(4));
        Assert.That(QuestionPlanner.Capacity(s, T0.AddHours(1)), Is.Zero);
    }

    [Test]
    public void TwoSufficientRelatedObjectives_AreSynthesizedOnce()
    {
        var s = Session();
        var p = Related();
        Excellent(s, "a");
        Assert.That(p.Next(s, T0.AddMinutes(1)).ConceptId, Is.EqualTo("b"), "b is opened before any synthesis");
        Excellent(s, "b");
        var synth = p.Next(s, T0.AddMinutes(1));
        Assert.That(synth.Move, Is.EqualTo(MoveKind.Synthesize));
        Assert.That((synth.ConceptId, synth.RelatedConceptId), Is.EqualTo(("a", "b")));
        s.Judged("a", MoveKind.Synthesize, 0.9, 0.9);
        Assert.That(p.Next(s, T0.AddMinutes(2)).End, Is.EqualTo(EndReason.EvidenceComplete), "one connection question per pair");
    }

    [Test]
    public void NoSynthesis_WhenLessThanAQuarterOfTheTimeIsLeft()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(20) });
        Excellent(s, "a");
        Excellent(s, "b");
        Assert.That(Related().Next(s, T0.AddMinutes(16)).End, Is.EqualTo(EndReason.EvidenceComplete));
    }

    [Test]
    public void UnrelatedObjectives_AreNotSynthesized()
    {
        var s = Session();
        Excellent(s, "a");
        Excellent(s, "b");
        Assert.That(Planner("a", "b").Next(s, T0.AddMinutes(1)).End, Is.EqualTo(EndReason.EvidenceComplete));
    }

    [Test]
    public void ExaminationMode_NeverTeaches()
    {
        var s = Session();
        Assert.Throws<InvalidOperationException>(() => s.Ask("a", MoveKind.Hint, "Here is a hint?", T0));
        Assert.Throws<InvalidOperationException>(() => s.Ask("a", MoveKind.GuidedExplanation, "Let me explain?", T0));
    }

    [Test]
    public void Deterministic_SameInputsSameDecision()
    {
        PlanDecision Run()
        {
            var s = Session();
            s.Judged("a", MoveKind.Elicit, 0.8, 0.6);
            s.Judged("a", MoveKind.Apply, 0.45, 0.55);
            return Planner("a", "b").Next(s, T0.AddMinutes(3));
        }
        var x = Run();
        var y = Run();
        Assert.That((y.Move, y.ConceptId, y.Reason), Is.EqualTo((x.Move, x.ConceptId, x.Reason)));
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
