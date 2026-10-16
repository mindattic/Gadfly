using Gadfly.Corpus;
using Gadfly.Ingestion;
using NUnit.Framework;

namespace Gadfly.Tests.Corpus;

[TestFixture]
public class CorpusRepositoryTests
{
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

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "gadfly-corpus-repo-test-" + Guid.NewGuid().ToString("N"));
        roots.Add(root);
        return root;
    }

    private CorpusRepository RepositoryWith(params CorpusMessage[] messages)
    {
        var store = new CorpusStore(NewRoot());
        store.AppendNew(messages);
        return new CorpusRepository(store);
    }

    private static CorpusMessage Message(
        string id,
        DateTimeOffset timestampUtc,
        CorpusRole role = CorpusRole.Human,
        string text = "hello") => new()
    {
        Id = id,
        Source = CorpusSource.ClaudeCodeCli,
        Role = role,
        SessionId = "s1",
        Text = text,
        TimestampUtc = timestampUtc,
    };

    [Test]
    public void GetAll_OrdersByTimestampUtc()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Appended out of chronological order.
        var repository = RepositoryWith(
            Message("a", baseTime.AddMinutes(2)),
            Message("b", baseTime),
            Message("c", baseTime.AddMinutes(1)));

        Assert.That(repository.GetAll().Select(m => m.Id), Is.EqualTo(new[] { "b", "c", "a" }));
    }

    [Test]
    public void GetHumanMessages_DropsNonHumanRoles()
    {
        var baseTime = DateTimeOffset.UtcNow;

        var repository = RepositoryWith(
            Message("a", baseTime, CorpusRole.Human),
            Message("b", baseTime.AddMinutes(1), CorpusRole.Assistant),
            Message("c", baseTime.AddMinutes(2), CorpusRole.Human));

        Assert.That(repository.GetHumanMessages().Select(m => m.Id), Is.EqualTo(new[] { "a", "c" }));
    }

    [Test]
    public void GetSince_IsInclusiveOfBoundary()
    {
        var boundary = DateTimeOffset.UtcNow;

        var repository = RepositoryWith(
            Message("before", boundary.AddMinutes(-1)),
            Message("on", boundary),
            Message("after", boundary.AddMinutes(1)));

        Assert.That(repository.GetSince(boundary).Select(m => m.Id), Is.EqualTo(new[] { "on", "after" }));
    }

    [Test]
    public void CorpusHash_IsIdenticalRegardlessOfStoreReadOrder()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var a = Message("a", baseTime);
        var b = Message("b", baseTime.AddMinutes(1));
        var c = Message("c", baseTime.AddMinutes(2));

        var appendedAscending = RepositoryWith(a, b, c);
        var appendedDescending = RepositoryWith(c, b, a);

        Assert.That(appendedAscending.CorpusHash(), Is.EqualTo(appendedDescending.CorpusHash()));
    }

    [Test]
    public void CorpusHash_ChangesWhenMessageIsAdded()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var a = Message("a", baseTime);
        var b = Message("b", baseTime.AddMinutes(1));

        var withoutB = RepositoryWith(a);
        var withB = RepositoryWith(a, b);

        Assert.That(withB.CorpusHash(), Is.Not.EqualTo(withoutB.CorpusHash()));
    }

    [Test]
    public void CorpusHash_ChangesWhenMessageIsRemoved()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var a = Message("a", baseTime);
        var b = Message("b", baseTime.AddMinutes(1));

        var full = RepositoryWith(a, b);
        var withoutB = RepositoryWith(a);

        Assert.That(withoutB.CorpusHash(), Is.Not.EqualTo(full.CorpusHash()));
    }
}
