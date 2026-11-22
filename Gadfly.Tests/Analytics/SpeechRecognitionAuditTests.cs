using Gadfly.Server.Analytics;
using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// The speech-recognition audit's rules (GAD-T265): a pasted grouping is matched to members without echoing unmatched lines,
/// and groups are compared only when at least two are big enough, with a warning when their rates differ by more than 1.25x.
/// </summary>
[TestFixture]
public class SpeechRecognitionAuditTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Ben = Guid.NewGuid(), Cy = Guid.NewGuid();

    private static readonly (Guid, string, string?)[] Members =
    [
        (Ana, "ana", "ana@school.example"),
        (Ben, "ben", null),
        (Cy, "cy", "CY@school.example"),
    ];

    private static SpeechGroupStats Group(string name, double? wer, double? low = 0.05, int corrected = 1, int responses = 20, bool reportable = true) =>
        new(name, 6, responses, responses, corrected, 500, (int)((wer ?? 0) * 500), wer, 0.9, low, reportable);

    [Test]
    public void Grouping_MatchesByEmailOrUserName_WithAnySeparator()
    {
        var (map, unmatched) = SpeechRecognitionAudit.ParseGrouping(
            "ana@school.example, North\nben\tSouth\n\"cy@school.example\";\"East\"\n", Members);
        Assert.That(unmatched, Is.Zero);
        Assert.That(map[Ana], Is.EqualTo("North"));
        Assert.That(map[Ben], Is.EqualTo("South"));
        Assert.That(map[Cy], Is.EqualTo("East"), "email match ignores case and quotes");
    }

    [Test]
    public void Grouping_SkipsBlankAndCommentLines_AndCountsUnmatchedOnes()
    {
        var (map, unmatched) = SpeechRecognitionAudit.ParseGrouping("# who, group\n\nstranger, West\nana\nben, \n", Members);
        Assert.That(map, Is.Empty);
        Assert.That(unmatched, Is.EqualTo(3));   // unknown person, no separator, empty group
    }

    [Test]
    public void Grouping_NullText_IsEmpty()
    {
        var (map, unmatched) = SpeechRecognitionAudit.ParseGrouping(null, Members);
        Assert.That(map, Is.Empty);
        Assert.That(unmatched, Is.Zero);
    }

    [Test]
    public void Grouping_LongGroupNamesAreCutAndControlCharactersRemoved()
    {
        var (map, _) = SpeechRecognitionAudit.ParseGrouping("ana," + new string('x', 100) + "\nben,So\u0007uth", Members);
        Assert.That(map[Ana], Has.Length.EqualTo(60));
        Assert.That(map[Ben], Is.EqualTo("South"));
    }

    [Test]
    public void FewerThanTwoReportableGroups_CannotBeCompared()
    {
        var w = SpeechRecognitionAudit.Warnings([Group("A", 0.1), Group("B", 0.3, reportable: false)]);
        Assert.That(w, Has.Count.EqualTo(1));
        Assert.That(w[0], Does.Contain("can't be compared yet"));
    }

    [Test]
    public void SimilarRates_GiveNoWarning()
    {
        Assert.That(SpeechRecognitionAudit.Warnings([Group("A", 0.10), Group("B", 0.12)]), Is.Empty);
    }

    [Test]
    public void ADisparityAboveTheRatio_IsNamedWithBothGroups()
    {
        var w = SpeechRecognitionAudit.Warnings([Group("Fluent", 0.08), Group("Frequent", 0.20)]);
        Assert.That(w, Has.Count.EqualTo(1));
        Assert.That(w[0], Does.StartWith("Word error rate differs between groups: Frequent"));
        Assert.That(w[0], Does.Contain("Fluent"));
    }

    [Test]
    public void AZeroRateAgainstAPositiveOne_IsADisparity()
    {
        var w = SpeechRecognitionAudit.Warnings([Group("A", 0.0, low: 0.05), Group("B", 0.1, low: 0.05)]);
        Assert.That(w.Any(x => x.StartsWith("Word error rate", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AllZeroRates_AreNotADisparity()
    {
        Assert.That(SpeechRecognitionAudit.Warnings([Group("A", 0.0, low: 0, corrected: 0), Group("B", 0.0, low: 0, corrected: 0)]), Is.Empty);
    }

    [Test]
    public void GroupsWithoutAMeasuredRate_AreLeftOutOfThatComparison()
    {
        // Word error rate known for only one group: no WER warning, but the corrected share still compares.
        var w = SpeechRecognitionAudit.Warnings([Group("A", null, corrected: 1), Group("B", 0.1, corrected: 10)]);
        Assert.That(w.Any(x => x.StartsWith("Word error rate", StringComparison.Ordinal)), Is.False);
        Assert.That(w.Any(x => x.StartsWith("The share of answers the student had to correct", StringComparison.Ordinal)), Is.True);
    }
}
