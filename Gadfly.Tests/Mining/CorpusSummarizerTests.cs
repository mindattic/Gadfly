using Gadfly.Ingestion;
using Gadfly.Mining;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Mining;

[TestFixture]
public class CorpusSummarizerTests
{
    private static CorpusMessage Msg(string text) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = CorpusSource.ClaudeCodeCli,
        Role = CorpusRole.Human,
        SessionId = "s1",
        Text = text,
        TimestampUtc = DateTimeOffset.UtcNow,
    };

    [Test]
    public void ChunkByCharBudget_SplitsWhenBudgetExceeded()
    {
        var messages = new[] { Msg(new string('a', 5000)), Msg(new string('b', 5000)), Msg(new string('c', 100)) };

        var chunks = CorpusSummarizer.ChunkByCharBudget(messages, charBudget: 7000);

        Assert.That(chunks, Has.Count.EqualTo(2));
        Assert.That(chunks[0], Has.Count.EqualTo(1));
        Assert.That(chunks[1], Has.Count.EqualTo(2));
    }

    [Test]
    public void ChunkByCharBudget_ReturnsEmptyForNoMessages()
    {
        Assert.That(CorpusSummarizer.ChunkByCharBudget(Array.Empty<CorpusMessage>(), charBudget: 100), Is.Empty);
    }

    private sealed class DownPanel : Gadfly.Socrates.ISocraticVotingClient
    {
        public int Calls;
        public Task<MindAttic.Legion.VotingResult> AskAsync(string question, string context, MindAttic.Legion.Quorum quorum, CancellationToken ct = default)
        {
            Calls++;
            // What Legion returns when every voter fails.
            return Task.FromResult(new MindAttic.Legion.VotingResult { Question = question, QuorumReached = false, Consensus = "All voters failed." });
        }
        public Task<MindAttic.Legion.VotingResult> AskWithProfilesAsync(string q, string c, IReadOnlyList<MindAttic.Legion.VoterProfile> v, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MindAttic.Legion.DecisionResult> DecideAsync(string q, IReadOnlyList<string> o, string c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Gadfly.Socrates.PanelReply>> FanOutAsync(string s, string u, int m = 4096, double t = 0.2, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Test]
    public async Task SummarizeAsync_DoesNotCacheOrReturnTheFailureWording_WhenThePanelIsDown()
    {
        var panel = new DownPanel();
        var cachePath = Path.Combine(Path.GetTempPath(), "gadfly-digest-cache-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var summarizer = new CorpusSummarizer(panel, cachePath);
            var digest = await summarizer.SummarizeAsync(new[] { Msg("hello") }, "hash-down", Array.Empty<string>());
            await summarizer.SummarizeAsync(new[] { Msg("hello") }, "hash-down", Array.Empty<string>());

            Assert.That(digest, Does.Not.Contain("All voters failed"));
            Assert.That(File.Exists(cachePath), Is.False, "a failed digest must not be cached");
            Assert.That(panel.Calls, Is.EqualTo(4), "the second run asked the panel again instead of reusing a poisoned cache");
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }

    [Test]
    public async Task SummarizeAsync_CachesByCorpusHash_SkippingFurtherCallsOnRepeat()
    {
        var client = new FakeSocraticVotingClient(new[] { "chunk summary", "digest text" });
        var cachePath = Path.Combine(Path.GetTempPath(), "gadfly-digest-cache-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var summarizer = new CorpusSummarizer(client, cachePath);
            var messages = new[] { Msg("hello") };

            var first = await summarizer.SummarizeAsync(messages, "hash-1", Array.Empty<string>());
            var callsAfterFirst = client.Calls.Count;
            var second = await summarizer.SummarizeAsync(messages, "hash-1", Array.Empty<string>());

            Assert.That(first, Is.EqualTo("digest text"));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(client.Calls.Count, Is.EqualTo(callsAfterFirst));
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }
}
