using Gadfly.Corpus;
using Gadfly.Ingestion;
using NUnit.Framework;

namespace Gadfly.Tests.Corpus;

[TestFixture]
public class CorpusStoreTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gadfly-corpus-test-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static CorpusMessage Message(string id, string text = "hello") => new()
    {
        Id = id,
        Source = CorpusSource.ClaudeCodeCli,
        Role = CorpusRole.Human,
        SessionId = "s1",
        Text = text,
        TimestampUtc = DateTimeOffset.UtcNow,
    };

    [Test]
    public void AppendNew_SkipsAlreadyIngestedIds()
    {
        var store = new CorpusStore(root);

        var firstRun = store.AppendNew(new[] { Message("a"), Message("b") });
        var secondRun = store.AppendNew(new[] { Message("b"), Message("c") });

        Assert.That(firstRun, Is.EqualTo(2));
        Assert.That(secondRun, Is.EqualTo(1));
        Assert.That(store.ReadAll().Select(m => m.Id), Is.EquivalentTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void ReadAll_ReturnsEmpty_WhenNothingIngestedYet()
    {
        Assert.That(new CorpusStore(root).ReadAll(), Is.Empty);
    }

    [Test]
    public void AppendNew_PersistsAcrossNewStoreInstances()
    {
        new CorpusStore(root).AppendNew(new[] { Message("a") });

        var reopened = new CorpusStore(root);
        Assert.That(reopened.ReadAll().Select(m => m.Id), Is.EquivalentTo(new[] { "a" }));

        var appendedAgain = reopened.AppendNew(new[] { Message("a") });
        Assert.That(appendedAgain, Is.EqualTo(0), "manifest should persist across instances and still dedupe");
    }
}
