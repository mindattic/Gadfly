using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

[TestFixture]
public class InterrogationSessionTests
{
    private sealed class DownPanel : ISocraticVotingClient
    {
        // What Legion returns when every voter fails: status wording as the consensus, quorum not reached.
        public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
            Task.FromResult(new VotingResult { Question = question, QuorumReached = false, Consensus = "All voters failed." });
        public Task<VotingResult> AskWithProfilesAsync(string q, string c, IReadOnlyList<VoterProfile> v, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DecisionResult> DecideAsync(string q, IReadOnlyList<string> o, string c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PanelReply>> FanOutAsync(string s, string u, int m = 4096, double t = 0.2, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Test]
    public async Task RunAsync_NeverShowsThePanelFailureWording_AsTheQuestion()
    {
        var session = new InterrogationSession(new DownPanel(), Quorum.Plurality, maxTurns: 1);

        var transcript = await session.RunAsync("digest", _ => Task.CompletedTask, () => Task.FromResult<string?>("Aporia"));

        Assert.That(transcript[0].Text, Does.Not.Contain("All voters failed"));
    }

    [Test]
    public async Task Reflection_NeverReturnsThePanelFailureWording()
    {
        var text = await new ReflectionRunner(new DownPanel()).RunAsync("digest");

        Assert.That(text, Does.Not.Contain("All voters failed"));
    }

    [Test]
    public async Task RunAsync_StopsWhenHumanDeclaresAporia()
    {
        var client = new FakeSocraticVotingClient(new[] { "Why do you believe that?" });
        var session = new InterrogationSession(client, Quorum.Plurality, maxTurns: 10);
        var replies = new Queue<string>(new[] { "Aporia" });

        var transcript = await session.RunAsync(
            corpusDigest: "digest",
            writeQuestion: _ => Task.CompletedTask,
            readHumanReply: () => Task.FromResult<string?>(replies.Dequeue()));

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
            readHumanReply: () => Task.FromResult<string?>("I don't know, but not that word"));

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
