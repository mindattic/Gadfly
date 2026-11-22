using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Engine;

/// <summary>Ledger-based coherence measures for the relay arm of the pilot (GAD-T449, hypothesis H1). Pure: no model calls.</summary>
[TestFixture]
public class CoherenceMeasuresTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private static AnswerJudgment Judged(double score, double? consistency = null) => new()
    {
        Criteria = consistency is { } c
            ? [new CriterionScore { Criterion = "consistency", Score = c }]
            : [new CriterionScore { Criterion = "accuracy", Score = score }],
        OverallScore = score,
    };

    private static void Turn(SocraticSession s, MoveKind move, AnswerJudgment judgment, IReadOnlyList<string>? targets = null)
    {
        s.Ask("c1", move, "Question?", T0, targetClaimIds: targets, objectiveKey: "o1");
        s.RecordAnswer(new Answer { Text = "An answer.", CompletedAt = T0 });
        s.RecordJudgment(judgment, 0.5);
    }

    [Test]
    public void Counts_contradictions_confrontations_and_failed_resolutions()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        s.StartClock(T0);
        var ledger = new ClaimLedger();
        Turn(s, MoveKind.Elicit, Judged(0.8));
        var a = ledger.Add(0, "c1", "heat rises", "Heat rises.");
        Turn(s, MoveKind.Probe, Judged(0.6));
        var b = ledger.Add(1, "c1", "heat sinks", "Heat sinks.");
        var c = ledger.Add(1, "c1", "it is cold", "It is cold.");
        ledger.AddContradiction(new ClaimContradiction(a.Id, b.Id, "c1", 0.9));
        ledger.AddContradiction(new ClaimContradiction(a.Id, c.Id, "c1", 0.5));   // split panel: possible only
        Turn(s, MoveKind.Contradiction, Judged(0.9, consistency: 0.2), [a.Id, b.Id]);

        var m = CoherenceMeasures.Measure(s, ledger, "one", "relay");
        Assert.That(m.Answers, Is.EqualTo(2), "the confrontation is not an answer for a level");
        Assert.That(m.MeanAnswerScore, Is.EqualTo(0.7).Within(1e-9));
        Assert.That(m.Claims, Is.EqualTo(3));
        Assert.That(m.Contradictions, Is.EqualTo(1));
        Assert.That(m.PossibleContradictions, Is.EqualTo(1));
        Assert.That(m.Confronted, Is.EqualTo(1));
        Assert.That(m.FailedResolutions, Is.EqualTo(1), "settled by the consistency score, not the overall score");
        Assert.That(m.ContradictionsPerTenClaims, Is.EqualTo(10.0 / 3).Within(1e-9));
    }

    [Test]
    public void An_empty_session_has_no_rates()
    {
        var s = new SocraticSession("x", new SessionPolicy(), ExamPrompts.Set, T0);
        var m = CoherenceMeasures.Measure(s, new ClaimLedger(), "empty");
        Assert.That(m.MeanAnswerScore, Is.Null);
        Assert.That(m.ContradictionsPerTenClaims, Is.Null);
        Assert.That(m.ResolutionRate, Is.Null);
    }

    [Test]
    public void Summaries_are_per_condition_and_the_csv_has_one_row_per_session()
    {
        var rows = new[]
        {
            new SessionCoherence { Session = "a", Condition = "honest", Claims = 10, Contradictions = 1, MeanAnswerScore = 0.7 },
            new SessionCoherence { Session = "b", Condition = "relay", Claims = 10, Contradictions = 3, MeanAnswerScore = 0.7, Confronted = 2, FailedResolutions = 1 },
            new SessionCoherence { Session = "c", Condition = "relay", Claims = 0, MeanAnswerScore = 0.5 },
        };
        var summary = CoherenceMeasures.Summarise(rows);
        Assert.That(summary.Select(x => x.Condition), Is.EqualTo(new[] { "honest", "relay" }));
        var relay = summary[1];
        Assert.That(relay.Sessions, Is.EqualTo(2));
        Assert.That(relay.SessionsWithClaims, Is.EqualTo(1));
        Assert.That(relay.MeanContradictionsPerTenClaims, Is.EqualTo(3.0).Within(1e-9));
        Assert.That(relay.ResolutionRate, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(CoherenceMeasures.ToCsv(rows).Split("\r\n", StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(4));
    }
}
