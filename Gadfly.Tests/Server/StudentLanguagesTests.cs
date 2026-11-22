using Gadfly.Server.Student;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The language questions on the get-ready page (GAD-T552) and the exam language shown first (GAD-T546).</summary>
[TestFixture]
public class StudentLanguagesTests
{
    [Test]
    public void Clean_KeepsKnownTags_InListOrder_AndAlwaysIncludesThePrimary()
    {
        var (spoken, primary) = StudentLanguages.Clean(new LanguageAnswers(["ko-KR", "xx", "EN"], "ja"));
        Assert.That(primary, Is.EqualTo("ja"));
        Assert.That(spoken, Is.EqualTo(new[] { "en", "ko", "ja" }));
    }

    [Test]
    public void Clean_WithAnUnknownPrimary_TakesTheFirstSpoken_AndWithNothingValid_SavesNothing()
    {
        Assert.That(StudentLanguages.Clean(new LanguageAnswers(["ko"], "klingon")).Primary, Is.EqualTo("ko"));
        Assert.That(StudentLanguages.Clean(new LanguageAnswers([], "")).Primary, Is.Null);
    }

    /// <summary>GAD-T594: three readings become a few derived numbers; a reading nobody heard is left out, none heard stores nothing.</summary>
    [Test]
    public void Summarize_TurnsReadingsIntoRateAndPauses_OnlyFromHeardReadings()
    {
        var at = DateTimeOffset.UnixEpoch;
        var readings = new List<StudentLanguages.CalibrationReading>
        {
            new(true, 3000, [300, 500], 0.9, 0.8),
            new(true, 3000, [700], null, 0.6),
            new(false, 0, [], null, 0),
        };
        var c = StudentLanguages.Summarize("en", readings, at)!;
        Assert.That(c.Readings, Is.EqualTo(2));
        Assert.That(c.SpeechRateWordsPerMinute, Is.EqualTo(180));   // 9 words in 3 s
        Assert.That(c.SpeechRateSyllablesPerSecond, Is.EqualTo(3.67));   // 11 syllables in 3 s
        Assert.That(c.MeanPauseMs, Is.EqualTo(500));
        Assert.That(c.LongestPauseMs, Is.EqualTo(700));
        Assert.That(c.RecognitionConfidence, Is.EqualTo(0.75), "its own confidence when given, else the match");
        Assert.That(StudentLanguages.Summarize("ko", [new(false, 0, [], null, 0)], at), Is.Null);
        Assert.That(StudentLanguages.CalibrationFor("ko").Text, Does.StartWith("다람쥐"));
    }

    [TestCase("en", "en", "en", "en")]   // owner rule: English primary, English page, English exam
    [TestCase("ko", "en", "en,ko", "ko")]
    [TestCase(null, "ko", "en,ko", "ko")]
    [TestCase(null, "en", "en,ko", "en")]
    [TestCase("ja", "en", "en,ko", "en")]
    [TestCase("ja", "ko", "en", "en")]
    [TestCase(null, "en", "ko", "ko")]
    public void DefaultExamLanguage_PrefersThePrimary_ThenThePage_ThenEnglish(string? primary, string page, string offered, string expected) =>
        Assert.That(StudentLanguages.DefaultExamLanguage(offered.Split(','), primary, page), Is.EqualTo(expected));
}
