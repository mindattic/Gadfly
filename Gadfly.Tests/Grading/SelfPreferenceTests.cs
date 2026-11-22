using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>GAD-T431: the self-preference check and question authorship.</summary>
[TestFixture]
public class SelfPreferenceTests
{
    private static IEnumerable<SelfPreferenceObservation> Observations(string member, int n, double lift)
    {
        for (var i = 0; i < n; i++)
        {
            var peer = 2 + i % 3;
            yield return new SelfPreferenceObservation(member, (int)Math.Round(peer + lift), peer, peer, 1.0);
            yield return new SelfPreferenceObservation(member, peer, peer, peer, 0.0);
        }
    }

    [Test]
    public void AMemberThatGradesItsOwnFamilysQuestionsHigher_IsFlagged()
    {
        var rows = SelfPreference.Analyze(Observations("openai", 20, 1));
        var row = rows.Single();
        Assert.That(row.Member, Is.EqualTo("openai"));
        Assert.That(row.LiftVsPeers, Is.GreaterThan(0.5));
        Assert.That(row.Flagged, Is.True, row.Finding);
    }

    [Test]
    public void NoDifference_IsNotFlagged()
    {
        var row = SelfPreference.Analyze(Observations("gemini", 20, 0)).Single();
        Assert.That(row.Flagged, Is.False);
        Assert.That(row.Finding, Does.Contain("No self-preference detected"));
    }

    [Test]
    public void TooFewAnswers_SaysSoInsteadOfFlagging()
    {
        var row = SelfPreference.Analyze(Observations("claude", 3, 2)).Single();
        Assert.That(row.Flagged, Is.False);
        Assert.That(row.Finding, Does.StartWith("Not enough answers"));
    }

    [Test]
    public void MixedAuthorship_IsLeftOut()
    {
        var rows = SelfPreference.Analyze([new SelfPreferenceObservation("deepseek", 5, 2, null, 0.25)]);
        Assert.That(rows.Single().OwnAnswers + rows.Single().OtherAnswers, Is.EqualTo(0));
    }

    [Test]
    public void PeerMedian_IsTheMiddleOfTheOthers()
    {
        Assert.That(SelfPreference.PeerMedian([1, 4, 2]), Is.EqualTo(2));
        Assert.That(SelfPreference.PeerMedian([1, 4]), Is.EqualTo(2.5));
        Assert.That(SelfPreference.PeerMedian([]), Is.Null);
    }

    [Test]
    public void TheAuthorOfATurn_SurvivesTheTranscriptRoundTrip()
    {
        var s = new SocraticSession("subject", new SessionPolicy { ConceptIds = ["a"] }, ExamPrompts.Set, DateTimeOffset.UnixEpoch);
        s.Ask("a", MoveKind.Elicit, "What is a?", DateTimeOffset.UnixEpoch, author: new QuestionAuthorship(QuestionAuthorship.PanelDraft, ["openai"], ["gpt-x"]));
        var back = SessionTranscriptSerializer.Deserialize(SessionTranscriptSerializer.Serialize(s));
        var author = back.Turns.Single().QuestionAuthor;
        Assert.That(author, Is.Not.Null);
        Assert.That(author!.How, Is.EqualTo(QuestionAuthorship.PanelDraft));
        Assert.That(author.Families, Is.EqualTo(new[] { "openai" }));
        Assert.That(author.Models, Is.EqualTo(new[] { "gpt-x" }));
        Assert.That(QuestionAuthorship.Fixed(QuestionAuthorship.InstructorText).ModelWritten, Is.False);
    }
}
