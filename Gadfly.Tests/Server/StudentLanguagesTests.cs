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
