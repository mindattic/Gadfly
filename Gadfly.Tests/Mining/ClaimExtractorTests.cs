using Gadfly.Ingestion;
using Gadfly.Mining;
using NUnit.Framework;

namespace Gadfly.Tests.Mining;

[TestFixture]
public class ClaimExtractorTests
{
    private static CorpusMessage Human(string sessionId, string day, string text) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Source = CorpusSource.ClaudeCodeCli,
        Role = CorpusRole.Human,
        SessionId = sessionId,
        TimestampUtc = DateTimeOffset.Parse(day),
        Text = text,
    };

    [Test]
    public void ExtractRecurringPhrases_FindsPhraseRepeatedAcrossDistinctSessions()
    {
        var messages = new[]
        {
            Human("s1", "2026-01-01", "I always have to build everything myself from scratch"),
            Human("s2", "2026-01-05", "I always have to build everything myself from scratch, it's exhausting"),
            Human("s3", "2026-01-09", "completely unrelated message about lunch"),
        };

        var phrases = ClaimExtractor.ExtractRecurringPhrases(messages);

        Assert.That(phrases.Any(p => p.Contains("always have to build everything")), Is.True);
    }

    [Test]
    public void ExtractRecurringPhrases_IgnoresPhraseThatOnlyAppearsOnce()
    {
        var messages = new[] { Human("s1", "2026-01-01", "a sentence that never recurs anywhere else at all") };

        Assert.That(ClaimExtractor.ExtractRecurringPhrases(messages), Is.Empty);
    }

    [Test]
    public void ExtractRecurringPhrases_DoesNotCountRepetitionWithinTheSameSessionAndDay()
    {
        var messages = new[]
        {
            Human("s1", "2026-01-01", "the same exact phrase repeated here"),
            Human("s1", "2026-01-01", "the same exact phrase repeated here"),
        };

        // Both messages share one occurrence key (same session + day), so this must not count as "recurring".
        Assert.That(ClaimExtractor.ExtractRecurringPhrases(messages), Is.Empty);
    }

    [Test]
    public void Normalize_LowercasesAndStripsPunctuation()
    {
        Assert.That(ClaimExtractor.Normalize("Hello, World! It's great."), Is.EqualTo("hello world it s great"));
    }
}
