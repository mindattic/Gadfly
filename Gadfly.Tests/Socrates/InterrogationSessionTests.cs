using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

[TestFixture]
public class InterrogationSessionTests
{
    [Test]
    public async Task RunAsync_StopsWhenHumanDeclaresAporia()
    {
        var client = new FakeSocraticVotingClient(new[] { "Why do you believe that?" });
        var session = new InterrogationSession(client, Quorum.Plurality, maxTurns: 10);
        var replies = new Queue<string>(new[] { "Aporia" });

        var transcript = await session.RunAsync(
            corpusDigest: "digest",
            writeQuestion: _ => Task.CompletedTask,
            readHumanReply: () => Task.FromResult(replies.Dequeue()));

        Assert.That(transcript, Has.Count.EqualTo(2));
        Assert.That(transcript[0].Speaker, Is.EqualTo("Socrates"));
        Assert.That(transcript[1].Text, Is.EqualTo("Aporia"));
    }

    [Test]
    public async Task RunAsync_StopsAtMaxTurns_WhenAporiaNeverDeclared()
    {
        var client = new FakeSocraticVotingClient(new[] { "Q1", "Q2", "Q3" });
        var session = new InterrogationSession(client, Quorum.Plurality, maxTurns: 3);

        var transcript = await session.RunAsync(
            corpusDigest: "digest",
            writeQuestion: _ => Task.CompletedTask,
            readHumanReply: () => Task.FromResult("I don't know, but not that word"));

        Assert.That(transcript, Has.Count.EqualTo(6)); // 3 turns * (question + reply)
    }

    [TestCase("Aporia")]
    [TestCase("aporia")]
    [TestCase("  APORIA  ")]
    public void IsAporia_IsCaseAndWhitespaceInsensitive(string input)
    {
        Assert.That(InterrogationSession.IsAporia(input), Is.True);
    }

    [Test]
    public void IsAporia_IsFalseForOtherText()
    {
        Assert.That(InterrogationSession.IsAporia("I'm not sure yet"), Is.False);
    }

    [Test]
    public void SaveTranscript_ThenLoadLastTranscript_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gadfly-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new FakeSocraticVotingClient(new[] { "Why?" });
            var session = new InterrogationSession(client, Quorum.Plurality, maxTurns: 1);
            session.Transcript.Add(new InterrogationTurn { Speaker = "Socrates", Text = "Why?" });
            session.Transcript.Add(new InterrogationTurn { Speaker = "You", Text = "Aporia" });

            session.SaveTranscript(dir);
            var loaded = InterrogationSession.LoadLastTranscript(dir);

            Assert.That(loaded, Has.Count.EqualTo(2));
            Assert.That(loaded[1].Text, Is.EqualTo("Aporia"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void LoadLastTranscript_ReturnsEmpty_WhenDirectoryMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gadfly-session-missing-" + Guid.NewGuid().ToString("N"));
        Assert.That(InterrogationSession.LoadLastTranscript(dir), Is.Empty);
    }
}
