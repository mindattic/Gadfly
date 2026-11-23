using Gadfly.Engine;
using Gadfly.Server.Analytics;
using Gadfly.Server.Data;
using Gadfly.Server.Translation;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Spanish as an exam variant (GAD-T611): offered by default with English and Korean, staff choose from it like any language, its
/// machine-drafted texts are written in neutral Latin American Spanish with usted, and the fairness views name it.
/// </summary>
[TestFixture]
public class SpanishExamVariantTests
{
    [Test]
    public void New_exams_offer_english_korean_and_spanish_in_display_order()
    {
        Assert.That(ExamLanguage.DefaultOffered, Is.EqualTo("en,ko,es"));
        Assert.That(new Exam().Languages, Is.EqualTo("en,ko,es"));
        Assert.That(ExamLanguageService.Offered(ExamLanguage.DefaultOffered), Is.EqualTo(new[] { "en", "ko", "es" }));
        Assert.That(ExamLanguageService.Offered("es,en"), Is.EqualTo(new[] { "en", "es" }), "display order, whatever the stored order");
        Assert.That(ExamLanguageService.Offered("en,ko"), Is.EqualTo(new[] { "en", "ko" }), "an exam staff left without Spanish keeps its list");
        Assert.That(ExamLanguageService.Names(["en", "ko", "es"]), Is.EqualTo("English, Korean and Spanish"));
    }

    [Test]
    public void Machine_drafts_into_spanish_use_neutral_latin_american_spanish_and_usted_and_other_prompts_are_unchanged()
    {
        var es = MachineTranslator.SystemPrompt("en", "es");
        Assert.That(es, Does.Contain("into Spanish").And.Contain("usted").And.Contain("Latin American"));
        foreach (var (from, to) in new[] { ("en", "ko"), ("ko", "en"), ("es", "en") })
            Assert.That(MachineTranslator.SystemPrompt(from, to), Does.Not.Contain("usted"), $"{from} to {to}");
        Assert.That(MachineTranslator.MockTranslate(" Photosynthesis ", "es"), Is.EqualTo("[Spanish, mock translation] Photosynthesis"));
    }

    [Test]
    public void Fairness_views_name_spanish()
    {
        Assert.That(GroupFairness.LanguageLabel("es-MX"), Is.EqualTo("Spanish"));
        Assert.That(GroupFairness.LanguageLabel("ko"), Is.EqualTo("Korean"));
    }
}
