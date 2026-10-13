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
