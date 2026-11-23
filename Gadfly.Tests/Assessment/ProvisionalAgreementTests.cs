using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Assessment;

/// <summary>The fast-versus-panel disagreement analysis (GAD-T644): counts, rate with interval, score gaps, grouping.</summary>
[TestFixture]
public class ProvisionalAgreementTests
{
    private static ProvisionalObservation Obs(double fast, double? full, bool disagreed = false, string subject = "Biology",
        MoveKind move = MoveKind.Probe, string provider = "anthropic", string model = "fast-1") =>
        new(subject, move, provider, model, fast, full, disagreed);

    [Test]
    public void Empty_GivesAnEmptyOverallRowFlaggedTooFew()
    {
        var r = ProvisionalAgreement.Analyze(Array.Empty<ProvisionalObservation>());
        Assert.Multiple(() =>
        {
            Assert.That(r.Overall.Turns, Is.EqualTo(0));
            Assert.That(r.Overall.DisagreementRate, Is.Null);
            Assert.That(r.Overall.RateLow, Is.Null);
            Assert.That(r.Overall.MeanAbsoluteGap, Is.Null);
            Assert.That(r.Overall.TooFew, Is.True);
            Assert.That(r.BySubject, Is.Empty);
        });
    }

    [Test]
    public void PendingTurns_AreCountedButLeftOutOfTheFigures()
    {
        var r = ProvisionalAgreement.Analyze([Obs(0.8, 0.6, disagreed: true), Obs(0.4, 0.5), Obs(0.9, null)]);
        Assert.Multiple(() =>
        {
            Assert.That(r.Overall.Turns, Is.EqualTo(3));
            Assert.That(r.Overall.Compared, Is.EqualTo(2));
            Assert.That(r.Overall.Disagreements, Is.EqualTo(1));
            Assert.That(r.Overall.DisagreementRate, Is.EqualTo(0.5));
            Assert.That(r.Overall.MeanSignedGap, Is.EqualTo(0.05).Within(1e-9));    // (0.2 + -0.1) / 2
            Assert.That(r.Overall.MeanAbsoluteGap, Is.EqualTo(0.15).Within(1e-9));  // (0.2 + 0.1) / 2
        });
    }

    [Test]
    public void Groups_BySubjectMoveAndProviderModel()
    {
        var r = ProvisionalAgreement.Analyze([
            Obs(0.5, 0.5, subject: "Biology", move: MoveKind.Elicit, provider: "Anthropic", model: "a"),
            Obs(0.5, 0.5, subject: "History", move: MoveKind.Probe, provider: "anthropic", model: "a"),
            Obs(0.5, 0.5, subject: " ", move: MoveKind.Probe, provider: "openai", model: "b"),
        ]);
        Assert.Multiple(() =>
        {
            Assert.That(r.BySubject.Select(x => x.Key), Is.EqualTo(new[] { "(none)", "Biology", "History" }));
            Assert.That(r.ByMove.Select(x => (x.Key, x.Turns)), Is.EqualTo(new[] { ("Elicit", 1), ("Probe", 2) }));
            Assert.That(r.ByProviderModel.Select(x => (x.Key, x.Turns)), Is.EqualTo(new[] { ("anthropic/a", 2), ("openai/b", 1) }));
        });
    }

    [Test]
    public void EnoughComparedTurns_IsNotFlaggedTooFew()
    {
        var obs = Enumerable.Range(0, ProvisionalAgreement.MinTurns).Select(i => Obs(0.5, 0.5, disagreed: i < 2)).ToList();
        obs.Add(Obs(0.5, 0.5, subject: "History"));
        var r = ProvisionalAgreement.Analyze(obs);
        Assert.Multiple(() =>
        {
            Assert.That(r.BySubject.Single(x => x.Key == "Biology").TooFew, Is.False);
            Assert.That(r.BySubject.Single(x => x.Key == "History").TooFew, Is.True);
            Assert.That(r.Overall.TooFew, Is.False);
        });
    }

    [Test]
    public void WilsonInterval_MatchesKnownValuesAndStaysInRange()
    {
        var mid = ProvisionalAgreement.Wilson(5, 10)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(mid.Low, Is.EqualTo(0.2366).Within(1e-4));
            Assert.That(mid.High, Is.EqualTo(0.7634).Within(1e-4));
            Assert.That(ProvisionalAgreement.Wilson(0, 10)!.Value.Low, Is.EqualTo(0));
            Assert.That(ProvisionalAgreement.Wilson(10, 10)!.Value.High, Is.EqualTo(1).Within(1e-12));
            Assert.That(ProvisionalAgreement.Wilson(0, 0), Is.Null);
        });
    }

    [Test]
    public void FromTurn_ReadsTheProvisionalJudgment()
    {
        var provisional = new ProvisionalJudgment
        {
            Judgment = new AnswerJudgment { Criteria = [], OverallScore = 0.7 },
            ProviderId = "anthropic", Model = "fast-1", FullScore = 0.3, Disagreement = "panel found a misconception",
        };
        var turn = new Turn { Index = 0, ConceptId = "c", Move = MoveKind.Apply, Question = "q", AskedAt = DateTimeOffset.UnixEpoch, Provisional = provisional };
        var bare = turn with { Provisional = null };

        var r = ProvisionalAgreement.Analyze([(turn, "Physics"), (bare, "Physics")]);
        Assert.Multiple(() =>
        {
            Assert.That(ProvisionalObservation.From(bare, "Physics"), Is.Null);
            Assert.That(r.Overall.Turns, Is.EqualTo(1));
            Assert.That(r.Overall.Disagreements, Is.EqualTo(1));
            Assert.That(r.Overall.MeanSignedGap, Is.EqualTo(0.4).Within(1e-9));
            Assert.That(r.ByMove.Single().Key, Is.EqualTo("Apply"));
        });
    }
}
