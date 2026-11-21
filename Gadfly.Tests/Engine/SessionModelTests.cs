using System.Text.Json;
using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

[TestFixture]
public class MasteryModelTests
{
    [TestCase(MoveKind.Elicit, 0.35)]
    [TestCase(MoveKind.Clarify, 0.4)]
    [TestCase(MoveKind.Probe, 0.5)]
    [TestCase(MoveKind.Counterexample, 0.6)]
    [TestCase(MoveKind.Synthesize, 0.65)]
    [TestCase(MoveKind.Apply, 0.7)]
    [TestCase(MoveKind.Contradiction, 0.7)]
    [TestCase(MoveKind.Close, 0.5)]
    public void QuestionDifficulty_AtPolicyDifficulty3(MoveKind move, double expected) =>
        Assert.That(MasteryModel.QuestionDifficulty(move, 3), Is.EqualTo(expected).Within(1e-12));

    [TestCase(1, 0.25)]
    [TestCase(2, 0.30)]
    [TestCase(4, 0.40)]
    [TestCase(5, 0.45)]
    [TestCase(0, 0.25)]
    [TestCase(-7, 0.25)]
    [TestCase(9, 0.45)]
    public void QuestionDifficulty_ShiftsAndClampsPolicyDifficulty(int policy, double expected) =>
        Assert.That(MasteryModel.QuestionDifficulty(MoveKind.Elicit, policy), Is.EqualTo(expected).Within(1e-12));

    [Test]
    public void QuestionDifficulty_StaysInsideBounds()
    {
        foreach (var move in Enum.GetValues<MoveKind>())
        foreach (var p in Enumerable.Range(-2, 10))
            Assert.That(MasteryModel.QuestionDifficulty(move, p), Is.InRange(0.05, 0.95));
    }

    [Test]
    public void Expected_IsHalfAtEqualAbility_AndIncreasing()
    {
        Assert.That(MasteryModel.Expected(0.6, 0.6), Is.EqualTo(0.5).Within(1e-12));
        double last = -1;
        for (var e = 0.0; e <= 1.0001; e += 0.05)
        {
            var x = MasteryModel.Expected(e, 0.5);
            Assert.That(x, Is.GreaterThan(last));
            Assert.That(x, Is.InRange(0, 1));
            last = x;
        }
    }

    [Test]
    public void Update_StaysWithinZeroAndOne_ForEveryInput()
    {
        foreach (var est in new[] { 0.0, 0.01, 0.5, 0.99, 1.0 })
        foreach (var score in new[] { -1.0, 0.0, 0.3, 1.0, 2.0 })
        foreach (var diff in new[] { 0.05, 0.5, 0.95 })
        foreach (var prior in new[] { -3, 0, 1, 10, 1000 })
            Assert.That(MasteryModel.Update(est, score, diff, prior), Is.InRange(0.0, 1.0), $"{est} {score} {diff} {prior}");
    }

    [Test]
    public void Update_IsMonotonicInTheScore()
    {
        double last = -1;
        for (var score = 0.0; score <= 1.0001; score += 0.1)
        {
            var next = MasteryModel.Update(0.5, score, 0.5, 1);
            Assert.That(next, Is.GreaterThanOrEqualTo(last));
            last = next;
        }
    }

    [Test]
    public void Update_IsMonotonicInThePriorEstimate()
    {
        double last = -1;
        for (var est = 0.0; est <= 1.0001; est += 0.1)
        {
            var next = MasteryModel.Update(est, 0.7, 0.5, 2);
            Assert.That(next, Is.GreaterThanOrEqualTo(last - 1e-12));
            last = next;
        }
    }

    [Test]
    public void StrongAnswerRaises_WeakAnswerLowers()
    {
        Assert.That(MasteryModel.Update(0.5, 1, 0.5, 0), Is.GreaterThan(0.5));
        Assert.That(MasteryModel.Update(0.5, 0, 0.5, 0), Is.LessThan(0.5));
        Assert.That(MasteryModel.Update(0.5, MasteryModel.Expected(0.5, 0.5), 0.5, 0), Is.EqualTo(0.5).Within(1e-12));
    }

    [Test]
    public void StepShrinksAsEvidenceAccumulates()
    {
        var first = MasteryModel.Update(0.5, 1, 0.5, 0) - 0.5;
        var later = MasteryModel.Update(0.5, 1, 0.5, 5) - 0.5;
        Assert.That(later, Is.LessThan(first).And.GreaterThan(0));
    }

    [Test]
    public void NegativePriorTurns_CountAsZero() =>
        Assert.That(MasteryModel.Update(0.5, 1, 0.5, -4), Is.EqualTo(MasteryModel.Update(0.5, 1, 0.5, 0)));

    [Test]
    public void HardQuestionAnsweredWell_MovesMoreThanAnEasyOne()
    {
        var hard = MasteryModel.Update(0.5, 1, 0.7, 0) - 0.5;
        var easy = MasteryModel.Update(0.5, 1, 0.35, 0) - 0.5;
        Assert.That(hard, Is.GreaterThan(easy));
    }

    [Test]
    public void FailingAnEasyQuestion_CostsMoreThanFailingAHardOne()
    {
        var easy = 0.5 - MasteryModel.Update(0.5, 0, 0.35, 0);
        var hard = 0.5 - MasteryModel.Update(0.5, 0, 0.7, 0);
        Assert.That(easy, Is.GreaterThan(hard));
    }

    [Test]
    public void ScoreIsClamped() =>
        Assert.That(MasteryModel.Update(0.5, 5, 0.5, 0), Is.EqualTo(MasteryModel.Update(0.5, 1, 0.5, 0)));

    [Test]
    public void RepeatedStrongAnswers_ConvergeUpward_NeverPastOne()
    {
        var est = MasteryModel.Prior;
        for (var i = 0; i < 50; i++)
        {
            var next = MasteryModel.Update(est, 1, 0.6, i);
            Assert.That(next, Is.GreaterThanOrEqualTo(est));
            est = next;
        }
        Assert.That(est, Is.LessThanOrEqualTo(1).And.GreaterThan(0.8));
    }

    [Test]
    public void Prior_IsHalf() => Assert.That(MasteryModel.Prior, Is.EqualTo(0.5));
}

[TestFixture]
public class SessionPolicyTests
{
    [Test]
    public void Defaults_AreValid() => Assert.DoesNotThrow(new SessionPolicy().Validate);

    public static IEnumerable<TestCaseData> Invalid()
    {
        yield return new TestCaseData(new SessionPolicy { TimeBudget = TimeSpan.Zero }).SetName("Policy_ZeroTime");
        yield return new TestCaseData(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(-1) }).SetName("Policy_NegativeTime");
        yield return new TestCaseData(new SessionPolicy { MaxTurns = 0 }).SetName("Policy_NoTurns");
        yield return new TestCaseData(new SessionPolicy { MaxTurnsPerConcept = 0 }).SetName("Policy_NoTurnsPerConcept");
        yield return new TestCaseData(new SessionPolicy { MasteryThreshold = 0 }).SetName("Policy_MasteryZero");
        yield return new TestCaseData(new SessionPolicy { MasteryThreshold = 1.01 }).SetName("Policy_MasteryOverOne");
        yield return new TestCaseData(new SessionPolicy { AporiaThreshold = -0.1 }).SetName("Policy_AporiaNegative");
        yield return new TestCaseData(new SessionPolicy { AporiaThreshold = 0.8, MasteryThreshold = 0.8 }).SetName("Policy_AporiaEqualsMastery");
        yield return new TestCaseData(new SessionPolicy { MinTurnsBeforeAporia = 0 }).SetName("Policy_MinAporiaTurnsZero");
        yield return new TestCaseData(new SessionPolicy { Difficulty = 0 }).SetName("Policy_DifficultyZero");
        yield return new TestCaseData(new SessionPolicy { Difficulty = 6 }).SetName("Policy_DifficultySix");
        yield return new TestCaseData(new SessionPolicy { MasteryThreshold = double.NaN }).SetName("Policy_MasteryNaN");
        yield return new TestCaseData(new SessionPolicy { MasteryThreshold = double.PositiveInfinity }).SetName("Policy_MasteryInfinity");
        yield return new TestCaseData(new SessionPolicy { AporiaThreshold = double.NaN }).SetName("Policy_AporiaNaN");
        yield return new TestCaseData(new SessionPolicy { AporiaThreshold = double.NegativeInfinity }).SetName("Policy_AporiaNegativeInfinity");
        yield return new TestCaseData(new SessionPolicy { TimeMultiplier = double.PositiveInfinity }).SetName("Policy_TimeMultiplierInfinity");
    }

    [TestCaseSource(nameof(Invalid))]
    public void InvalidPolicies_AreRefused(SessionPolicy p) => Assert.Throws<ArgumentException>(p.Validate);

    [Test]
    public void Boundaries_AreValid()
    {
        Assert.DoesNotThrow(new SessionPolicy { MasteryThreshold = 1, AporiaThreshold = 0, Difficulty = 1, MaxTurns = 1, MaxTurnsPerConcept = 1, MinTurnsBeforeAporia = 1 }.Validate);
        Assert.DoesNotThrow(new SessionPolicy { Difficulty = 5 }.Validate);
    }
}

[TestFixture]
public class SocraticSessionTests
{
    [Test]
    public void Constructor_RecordsThePromptSet()
    {
        var s = Session();
        Assert.That(s.PromptSetId, Is.EqualTo(ExamPrompts.SetId));
        Assert.That(s.PromptSetVersion, Is.EqualTo(ExamPrompts.Version));
        Assert.That(s.PromptSetSha256, Is.EqualTo(ExamPrompts.Set.Sha256));
        Assert.That(s.StartedAt, Is.EqualTo(T0));
        Assert.That(s.IsEnded, Is.False);
        Assert.That(s.EndReason, Is.EqualTo(EndReason.None));
        Assert.That(s.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void Constructor_UsesAGivenId()
    {
        var id = Guid.NewGuid();
        Assert.That(new SocraticSession("s", new SessionPolicy(), ExamPrompts.Set, T0, id).Id, Is.EqualTo(id));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_NeedsASubject(string subject) =>
        Assert.Throws<ArgumentException>(() => new SocraticSession(subject, new SessionPolicy(), ExamPrompts.Set, T0));

    [Test]
    public void Constructor_ValidatesThePolicy() =>
        Assert.Throws<ArgumentException>(() => new SocraticSession("s", new SessionPolicy { MaxTurns = 0 }, ExamPrompts.Set, T0));

    [Test]
    public void TurnLifecycle_OpenThenAwaitingJudgmentThenJudged()
    {
        var s = Session();
        Assert.That(s.OpenTurn, Is.Null);
        Assert.That(s.AwaitingJudgment, Is.Null);
        var t = s.Ask("a", MoveKind.Elicit, "What is a?", T0, reason: "opening");
        Assert.That(s.OpenTurn, Is.EqualTo(t));
        Assert.That(t.Index, Is.EqualTo(0));
        Assert.That(t.Reason, Is.EqualTo("opening"));
        s.RecordAnswer(new Answer { Text = "x", CompletedAt = T0 });
        Assert.That(s.OpenTurn, Is.Null);
        Assert.That(s.AwaitingJudgment, Is.Not.Null);
        var judged = s.RecordJudgment(Judgment(0.7), 0.6);
        Assert.That(s.AwaitingJudgment, Is.Null);
        Assert.That(judged.MasteryAfter, Is.EqualTo(new MasterySnapshot("a", 0.6, 1)));
        Assert.That(s.Mastery["a"].Estimate, Is.EqualTo(0.6));
    }

    [Test]
    public void CannotAskWhileATurnIsOpen()
    {
        var s = Session();
        s.Ask("a", MoveKind.Elicit, "Q?", T0);
        Assert.Throws<InvalidOperationException>(() => s.Ask("a", MoveKind.Probe, "Q2?", T0));
    }

    [TestCase("")]
    [TestCase("  ")]
    public void AQuestionIsRequired(string q) => Assert.Throws<ArgumentException>(() => Session().Ask("a", MoveKind.Elicit, q, T0));

    [Test]
    public void CannotAnswerWithoutAnOpenTurn() =>
        Assert.Throws<InvalidOperationException>(() => Session().RecordAnswer(new Answer { Text = "x", CompletedAt = T0 }));

    [Test]
    public void CannotJudgeAnUnansweredTurn()
    {
        var s = Session();
        Assert.Throws<InvalidOperationException>(() => s.RecordJudgment(Judgment(1), 1));
        s.Ask("a", MoveKind.Elicit, "Q?", T0);
        Assert.Throws<InvalidOperationException>(() => s.RecordJudgment(Judgment(1), 1));
    }

    [Test]
    public void CannotJudgeTwice()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.6);
        Assert.Throws<InvalidOperationException>(() => s.RecordJudgment(Judgment(0), 0));
    }

    [Test]
    public void AnsweredButUnjudgedTurn_DoesNotBlockAsk_ThePlannerDoes()
    {
        // The open-turn rule only blocks unanswered turns; the planner refuses unjudged ones.
        var s = Session();
        s.AskAnswer("a", MoveKind.Elicit);
        Assert.DoesNotThrow(() => s.Ask("a", MoveKind.Probe, "Q2?", T0));
    }

    [TestCase(1.7, 1.0)]
    [TestCase(-0.3, 0.0)]
    public void JudgmentEstimate_IsClamped(double given, double stored)
    {
        var s = Session();
        s.AskAnswer("a", MoveKind.Elicit);
        s.RecordJudgment(Judgment(1), given);
        Assert.That(s.Mastery["a"].Estimate, Is.EqualTo(stored));
    }

    [Test]
    public void MasterySnapshot_CountsTurnsOnTheConcept()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 1, 0.6);
        s.Judged("b", MoveKind.Elicit, 1, 0.6);
        s.Judged("a", MoveKind.Probe, 1, 0.7);
        Assert.That(s.Mastery["a"].TurnsOnConcept, Is.EqualTo(2));
        Assert.That(s.Mastery["b"].TurnsOnConcept, Is.EqualTo(1));
        Assert.That(s.TurnsOn("a"), Is.EqualTo(2));
        Assert.That(s.Turns.Select(t => t.Index), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void End_NeedsARealReason() => Assert.Throws<ArgumentException>(() => Session().End(EndReason.None, T0));

    [Test]
    public void EndedSession_RefusesEveryMutation()
    {
        var s = Session();
        s.Ask("a", MoveKind.Elicit, "Q?", T0);
        s.End(EndReason.TimeUp, T0.AddMinutes(1));
        Assert.That(s.IsEnded, Is.True);
        Assert.That(s.EndedAt, Is.EqualTo(T0.AddMinutes(1)));
        Assert.That(s.Turns, Has.Count.EqualTo(1), "the unanswered open turn is kept as asked");
        Assert.Throws<InvalidOperationException>(() => s.RecordAnswer(new Answer { Text = "x", CompletedAt = T0 }));
        Assert.Throws<InvalidOperationException>(() => s.Ask("a", MoveKind.Probe, "Q?", T0));
        Assert.Throws<InvalidOperationException>(() => s.End(EndReason.Abandoned, T0));
    }

    [Test]
    public void Remaining_NeverNegative()
    {
        var s = Session(new SessionPolicy { TimeBudget = TimeSpan.FromMinutes(10) });
        Assert.That(s.Remaining(T0), Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(s.Remaining(T0.AddMinutes(4)), Is.EqualTo(TimeSpan.FromMinutes(6)));
        Assert.That(s.Remaining(T0.AddHours(2)), Is.EqualTo(TimeSpan.Zero));
    }
}

[TestFixture]
public class SessionTranscriptSerializerTests
{
    private static SocraticSession Full()
    {
        var s = Session(new SessionPolicy { ConceptIds = ["a", "b"], Seed = 42, Difficulty = 4 });
        s.Ask("a", MoveKind.Elicit, "What is a?", T0, reason: "Opening the next concept.");
        s.RecordAnswer(new Answer
        {
            Text = "It is the thing.",
            Mode = "voice",
            Words = [new TimedWord("It", TimeSpan.Zero, TimeSpan.FromMilliseconds(200), 0.9)],
            StartedAt = T0.AddSeconds(3),
            CompletedAt = T0.AddSeconds(9),
            SpeechMetrics = new Dictionary<string, double> { ["latencyMs"] = 3000 },
        });
        s.RecordJudgment(new AnswerJudgment
        {
            Criteria = [new CriterionScore { Criterion = "accuracy", Score = 0.8, Rationale = "fine", Citations = [new Citation("d:0", "q")] }],
            OverallScore = 0.8,
            Votes = [new JudgeVote("alpha", "m1", 0.8, "good"), new JudgeVote("beta", "m2", 0.7, "ok")],
            Agreement = 0.9,
            NeedsHumanReview = true,
            RubricVersion = 3,
            TextStyle = new TextStyleAssessment(0.2, "spoken", 2),
            Summary = "good",
        }, 0.65);
        s.Ask("a", MoveKind.Contradiction, "How do both hold?", T0.AddMinutes(1), ["c1", "c2"]);
        s.End(EndReason.Abandoned, T0.AddMinutes(2));
        return s;
    }

    [Test]
    public void RoundTrip_IsLossless()
    {
        var original = Full();
        var json = SessionTranscriptSerializer.Serialize(original);
        var back = SessionTranscriptSerializer.Deserialize(json);
        Assert.That(SessionTranscriptSerializer.Serialize(back), Is.EqualTo(json));
        Assert.Multiple(() =>
        {
            Assert.That(back.Id, Is.EqualTo(original.Id));
            Assert.That(back.SubjectId, Is.EqualTo(original.SubjectId));
            Assert.That(back.PromptSetSha256, Is.EqualTo(original.PromptSetSha256));
            Assert.That(back.EndReason, Is.EqualTo(EndReason.Abandoned));
            Assert.That(back.EndedAt, Is.EqualTo(original.EndedAt));
            Assert.That(back.Policy.Seed, Is.EqualTo(42));
            Assert.That(back.Policy.Difficulty, Is.EqualTo(4));
            Assert.That(back.Policy.ConceptIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(back.Turns, Has.Count.EqualTo(2));
            Assert.That(back.Turns[0].Answer!.Words[0].Confidence, Is.EqualTo(0.9));
            Assert.That(back.Turns[0].Answer!.SpeechMetrics["latencyMs"], Is.EqualTo(3000));
            Assert.That(back.Turns[0].Judgment!.Votes.Select(v => v.Model), Is.EqualTo(new[] { "m1", "m2" }));
            Assert.That(back.Turns[0].Judgment!.TextStyle!.Score, Is.EqualTo(0.2));
            Assert.That(back.Turns[1].TargetClaimIds, Is.EqualTo(new[] { "c1", "c2" }));
            Assert.That(back.Turns[1].Answer, Is.Null);
            Assert.That(back.Mastery["a"].Estimate, Is.EqualTo(0.65));
        });
    }

    [Test]
    public void RoundTrippedRunningSession_KeepsWorking()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5);
        var back = SessionTranscriptSerializer.Deserialize(SessionTranscriptSerializer.Serialize(s));
        Assert.That(back.IsEnded, Is.False);
        Assert.DoesNotThrow(() => back.Ask("a", MoveKind.Probe, "Why?", T0));
        Assert.That(back.Turns[^1].Index, Is.EqualTo(1));
    }

    [Test]
    public void EnumsAreWrittenAsNames()
    {
        var json = SessionTranscriptSerializer.Serialize(Full());
        Assert.That(json, Does.Contain("\"Elicit\"").And.Contain("\"Abandoned\"").And.Contain("\"formatVersion\": 1"));
    }

    private static string Mutate(string json, Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    [Test]
    public void UnknownFormatVersion_IsRefused()
    {
        var json = Mutate(SessionTranscriptSerializer.Serialize(Full()), o => o["formatVersion"] = 2);
        Assert.Throws<JsonException>(() => SessionTranscriptSerializer.Deserialize(json));
    }

    [TestCase("subjectId", "")]
    [TestCase("promptSetSha256", "")]
    public void MissingSubjectOrHash_IsRefused(string field, string value)
    {
        var json = Mutate(SessionTranscriptSerializer.Serialize(Full()), o => o[field] = value);
        Assert.Throws<JsonException>(() => SessionTranscriptSerializer.Deserialize(json));
    }

    [Test]
    public void MissingPolicy_IsRefused()
    {
        var json = Mutate(SessionTranscriptSerializer.Serialize(Full()), o => o.Remove("policy"));
        Assert.Throws<JsonException>(() => SessionTranscriptSerializer.Deserialize(json));
    }

    [Test]
    public void OutOfOrderTurnIndexes_AreRefused()
    {
        var json = Mutate(SessionTranscriptSerializer.Serialize(Full()), o => o["turns"]![1]!["index"] = 5);
        Assert.Throws<JsonException>(() => SessionTranscriptSerializer.Deserialize(json));
    }

    [Test]
    public void NullJson_IsRefused() => Assert.Throws<JsonException>(() => SessionTranscriptSerializer.Deserialize("null"));
}
