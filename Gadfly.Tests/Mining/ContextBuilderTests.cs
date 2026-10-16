using Gadfly.Corpus;
using Gadfly.Ingestion;
using Gadfly.Mining;
using Gadfly.Tests.Fakes;
using NUnit.Framework;

namespace Gadfly.Tests.Mining;

[TestFixture]
public class ContextBuilderTests
{
    private const string DigestHeader =
        "CORPUS DIGEST (recurring beliefs/opinions from the person's own conversation history):";

    private readonly List<string> roots = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var root in roots)
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        roots.Clear();
    }

    private CorpusRepository RepositoryWith(params CorpusMessage[] messages)
    {
        var root = Path.Combine(Path.GetTempPath(), "gadfly-context-builder-test-" + Guid.NewGuid().ToString("N"));
        roots.Add(root);
        var store = new CorpusStore(root);
        store.AppendNew(messages);
        return new CorpusRepository(store);
    }

    private static CorpusMessage Message(string id, CorpusRole role, string text, DateTimeOffset? timestampUtc = null) => new()
    {
        Id = id,
        Source = CorpusSource.ClaudeCodeCli,
        Role = role,
        SessionId = "s1",
        Text = text,
        TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow,
    };

    [Test]
    public void BuildInterrogationContext_EmitsDigestHeader_AndOmitsDialogueBlock_WhenNoTurns()
    {
        var context = ContextBuilder.BuildInterrogationContext("digest text", Array.Empty<(string Speaker, string Text)>());

        Assert.That(context, Is.EqualTo($"{DigestHeader}\ndigest text"));
        Assert.That(context, Does.Not.Contain("DIALOGUE SO FAR"));
    }

    [Test]
    public void BuildInterrogationContext_RendersSpeakerTextLines_InOrder_WhenTurnsPresent()
    {
        var dialogue = new (string Speaker, string Text)[]
        {
            ("Human", "I think productivity tools are overrated."),
            ("Gadfly", "What makes you say that?"),
            ("Human", "Because nobody finishes what they start."),
        };

        var context = ContextBuilder.BuildInterrogationContext("digest text", dialogue);

        var expected = $"{DigestHeader}\ndigest text\n\nDIALOGUE SO FAR THIS SESSION:\n" +
            "Human: I think productivity tools are overrated.\n" +
            "Gadfly: What makes you say that?\n" +
            "Human: Because nobody finishes what they start.";
        Assert.That(context, Is.EqualTo(expected));
    }

    [Test]
    public async Task BuildCorpusDigestAsync_SummarizesOnlyHumanMessages()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var repository = RepositoryWith(
            Message("h1", CorpusRole.Human, "I think remote work is better for focus.", baseTime),
            Message("a1", CorpusRole.Assistant, "ASSISTANT_TEXT_SHOULD_NOT_APPEAR", baseTime.AddMinutes(1)));

        var client = new FakeSocraticVotingClient(new[] { "chunk summary", "digest text" });
        var cachePath = Path.Combine(Path.GetTempPath(), "gadfly-digest-cache-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var builder = new ContextBuilder(repository, new CorpusSummarizer(client, cachePath));

            var digest = await builder.BuildCorpusDigestAsync();

            Assert.That(digest, Is.EqualTo("digest text"));
            Assert.That(client.Calls[0].Context, Does.Contain("I think remote work is better for focus."));
            Assert.That(client.Calls.Any(c => c.Context.Contains("ASSISTANT_TEXT_SHOULD_NOT_APPEAR")), Is.False);
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
    }

    [Test]
    public async Task BuildCorpusDigestAsync_ReusesCachedDigest_WhenCorpusHashUnchanged()
    {
        var repository = RepositoryWith(Message("h1", CorpusRole.Human, "hello there"));
        var client = new FakeSocraticVotingClient(new[] { "chunk summary", "digest text" });
        var cachePath = Path.Combine(Path.GetTempPath(), "gadfly-digest-cache-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var builder = new ContextBuilder(repository, new CorpusSummarizer(client, cachePath));

            var first = await builder.BuildCorpusDigestAsync();
            var callsAfterFirst = client.Calls.Count;
            var second = await builder.BuildCorpusDigestAsync();

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
