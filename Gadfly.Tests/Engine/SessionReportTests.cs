using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>The AI recommendation: per-concept mastery, recommendation boundaries, evidence and redaction.</summary>
[TestFixture]
public class SessionReportTests
{
    // Default policy: mastery 0.8, aporia 0.25, so "partial" starts at (0.8 + 0.25) / 2 = 0.525.

    private static SessionReport WithEstimates(params (string Concept, double Estimate)[] concepts)
    {
        var s = Session();
        foreach (var (c, e) in concepts) s.Judged(c, MoveKind.Elicit, 0.5, e);
        return SessionReport.From(s);
    }

    [Test]
    public void EveryConceptAtThreshold_IsMastered() =>
        Assert.That(WithEstimates(("a", 0.8), ("b", 0.95)).Recommendation, Is.EqualTo(SessionReport.Mastered));

    [Test]
    public void OneConceptJustBelowThreshold_IsNotMastered() =>
        Assert.That(WithEstimates(("a", 0.799), ("b", 0.95)).Recommendation, Is.EqualTo(SessionReport.Partial));

    [Test]
    public void OverallAtTheMidpoint_IsPartial() =>
        Assert.That(WithEstimates(("a", 0.525)).Recommendation, Is.EqualTo(SessionReport.Partial));

    [Test]
    public void OverallJustBelowTheMidpoint_IsNotDemonstrated() =>
        Assert.That(WithEstimates(("a", 0.524)).Recommendation, Is.EqualTo(SessionReport.NotDemonstrated));

    [Test]
    public void NothingExamined_IsNotDemonstrated()
    {
        var r = SessionReport.From(Session(new SessionPolicy { ConceptIds = ["a"] }));
        Assert.That(r.Recommendation, Is.EqualTo(SessionReport.NotDemonstrated));
        Assert.That(r.OverallScore, Is.EqualTo(0));
        Assert.That(r.Concepts.Single().Status, Is.EqualTo("not-examined"));
        Assert.That(r.Concepts.Single().Mastery, Is.EqualTo(0));
    }

    [Test]
    public void AnUnexaminedPolicyConcept_BlocksMastered_ButNotTheOverall()
    {
        var s = Session(new SessionPolicy { ConceptIds = ["a", "b"] });
        s.Judged("a", MoveKind.Elicit, 1, 0.9);
        s.Judged("a", MoveKind.Probe, 1, 0.9);
        var r = SessionReport.From(s);
        Assert.That(r.Recommendation, Is.EqualTo(SessionReport.Partial));
        Assert.That(r.OverallScore, Is.EqualTo(0.9));
        Assert.That(r.Concepts.Select(c => c.ConceptId), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void AverageScore_ExcludesTurnsNoJudgeCouldGrade()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.8, 0.8);
        s.AskAnswer("a", MoveKind.Probe);
        // What the engine records when the panel failed: no criteria, zero score, flagged for a human.
        s.RecordJudgment(new AnswerJudgment { Criteria = [], OverallScore = 0, NeedsHumanReview = true }, 0.8);
        var a = SessionReport.From(s).Concepts.Single(c => c.ConceptId == "a");
        Assert.That(a.AverageScore, Is.EqualTo(0.8));
        Assert.That(a.DisputedTurns, Is.EqualTo(new[] { 1 }), "the unjudged turn still needs a human look");
    }

    [Test]
    public void ConceptRows_CarryStatusTurnsAverageAndTitles()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.6, 0.6);
        s.Judged("a", MoveKind.Probe, 0.8, 0.85, needsReview: true);
        s.AskAnswer("b", MoveKind.Elicit);   // answered, not judged
        var r = SessionReport.From(s, new Dictionary<string, string> { ["a"] = "Alpha" });
        var a = r.Concepts.Single(c => c.ConceptId == "a");
        Assert.That(a.Title, Is.EqualTo("Alpha"));
        Assert.That(a.Status, Is.EqualTo(ConceptStatusNames.ReportName(QuestionPlanner.StatusOf(s, "a"))), "the report mirrors the planner");
        Assert.That(a.Turns, Is.EqualTo(2));
        Assert.That(a.AverageScore, Is.EqualTo(0.7));
        Assert.That(a.DisputedTurns, Is.EqualTo(new[] { 1 }));
        var b = r.Concepts.Single(c => c.ConceptId == "b");
        Assert.That(b.Title, Is.EqualTo("b"), "a missing title shows the id");
        Assert.That(b.AverageScore, Is.Null);
        Assert.That(b.Mastery, Is.EqualTo(0));
        Assert.That(b.Status, Is.EqualTo("open"));
        Assert.That(r.TurnsJudged, Is.EqualTo(2));
        Assert.That(r.TurnsNeedingReview, Is.EqualTo(1));
    }

    [Test]
    public void Evidence_ShowsTheExtremes_InTurnOrder_Capped()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.5, 0.5, answer: "middle");
        s.Judged("a", MoveKind.Probe, 0.95, 0.6, answer: "best");
        s.Judged("a", MoveKind.Probe, 0.05, 0.5, answer: "worst");
        s.Judged("a", MoveKind.Probe, 0.6, 0.5, answer: "near middle");
        var r = SessionReport.From(s, evidencePerConcept: 2);
        var e = r.Concepts.Single().Evidence;
        Assert.That(e.Select(x => x.Quote), Is.EqualTo(new[] { "best", "worst" }));
        Assert.That(e[0].Citations.Single().ChunkId, Is.EqualTo("d1:0"));
        Assert.That(SessionReport.From(s, evidencePerConcept: 0).Concepts.Single().Evidence, Is.Empty);
        Assert.That(SessionReport.From(s, evidencePerConcept: -3).Concepts.Single().Evidence, Is.Empty);
    }

    [Test]
    public void WithoutStudentWords_RemovesQuotesAndSummaries_KeepsScores()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.7, answer: "my private words");
        var r = SessionReport.From(s);
        var redacted = r.WithoutStudentWords();
        var ev = redacted.Concepts.Single().Evidence.Single();
        Assert.That(ev.Quote, Is.EqualTo("[redacted after the retention period]"));
        Assert.That(ev.JudgeSummary, Is.Empty);
        Assert.That(ev.Score, Is.EqualTo(0.9));
        Assert.That(redacted.OverallScore, Is.EqualTo(r.OverallScore));
        Assert.That(redacted.Recommendation, Is.EqualTo(r.Recommendation));
        Assert.That(redacted.Concepts.Single().Mastery, Is.EqualTo(r.Concepts.Single().Mastery));
        Assert.That(redacted.ToJson(), Does.Not.Contain("my private words"));
        Assert.That(r.Concepts.Single().Evidence.Single().Quote, Is.EqualTo("my private words"), "the original is not mutated");
    }

    [Test]
    public void Json_RoundTrips()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.7);
        s.End(EndReason.TimeUp, T0.AddMinutes(20));
        var r = SessionReport.From(s);
        var back = SessionReport.FromJson(r.ToJson())!;
        Assert.That(back.ToJson(), Is.EqualTo(r.ToJson()));
        Assert.That(back.EndReason, Is.EqualTo(EndReason.TimeUp));
        Assert.That(back.PromptSetSha256, Is.EqualTo(s.PromptSetSha256));
    }

    [Test]
    public void Markdown_ReportsLevelsNotTheRunningEstimate_AndDisputes()
    {
        var s = Session();
        s.Judged("a", MoveKind.Elicit, 0.9, 0.7, needsReview: true, answer: "words | with pipe");
        var md = SessionReport.From(s, new Dictionary<string, string> { ["a"] = "A | B" }).ToMarkdown();
        // GAD-T425: the running estimate is labelled a steering aid and never stated as the result.
        Assert.That(md, Does.Not.Contain("Recommendation:"));
        Assert.That(md, Does.Not.Contain("overall mastery"));
        Assert.That(md, Does.Contain("Running estimate (steering only)"));
        Assert.That(md, Does.Contain("the instructor decides the grade"));
        Assert.That(md, Does.Contain("Judges disagreed on turn(s) 1"));
        Assert.That(md, Does.Contain("A \\| B"));
    }

    [Test]
    public void Mastery_IsRoundedToThreePlaces()
    {
        var r = WithEstimates(("a", 0.123456));
        Assert.That(r.Concepts.Single().Mastery, Is.EqualTo(0.123));
    }
}
