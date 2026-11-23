using System.Text.RegularExpressions;
using Gadfly.Server.Localization;
using Gadfly.Server.Student;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The student interface in Spanish (GAD-T612), mirroring LocalizationTests' Korean checks: key parity across en, ko and es, the
/// placeholders, the consent notice, the practice, the emails, the culture and the calibration sentence.
/// </summary>
[TestFixture]
public class SpanishLocalizationTests
{
    private static readonly UiCatalog Catalog = new();

    /// <summary>Areas not yet in Spanish: none once the public site's site.es.json exists (GAD-T613).</summary>
    private static bool Covered(string key) => !key.StartsWith("site.", StringComparison.Ordinal) || Catalog.Table("es").ContainsKey("site.skip");

    [Test]
    public void Spanish_is_a_ui_language_with_latin_american_dates()
    {
        Assert.That(UiCatalog.Languages, Is.EqualTo(new[] { "en", "ko", "es" }));
        Assert.That(UiCatalog.Normalize("es-MX"), Is.EqualTo("es"));
        Assert.That(UiCatalog.NativeName("es"), Is.EqualTo("Español"));
        Assert.That(UiCatalog.CultureFor("es").Name, Is.EqualTo("es-419"));
        Assert.That(LocalizationSetup.CookieValue("es"), Is.EqualTo("c=es-419|uic=es-419"));
        Assert.That(Catalog.Table("es"), Is.Not.Empty);
    }

    [Test]
    public void Every_spanish_key_exists_in_english_and_every_english_key_is_in_spanish_and_korean()
    {
        Assert.That(Catalog.Table("es").Keys.Except(Catalog.Keys), Is.Empty, "a Spanish key with no English source is orphaned");
        Assert.That(Catalog.Keys.Where(Covered).Except(Catalog.Table("es").Keys), Is.Empty, "the student flow is fully translated into Spanish");
        Assert.That(Catalog.Keys.Except(Catalog.Table("ko").Keys), Is.Empty, "and into Korean");
    }

    [Test]
    public void Spanish_keeps_the_english_placeholders()
    {
        var placeholder = new Regex(@"\{(\d+)(?:[:,][^}]*)?\}|\{([a-zA-Z]+)\}");
        string Slots(string s) => string.Join(",", placeholder.Matches(s).Select(m => m.Value).Distinct().Order());
        var bad = Catalog.Table("es").Where(kv => Catalog.Find("en", kv.Key) is { } en && Slots(en) != Slots(kv.Value)).Select(kv => kv.Key);
        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void Spanish_addresses_the_student_as_usted()
    {
        var tu = new Regex(@"\b(tú|tu\s+(examen|respuesta|cámara|micrófono)|puedes|tienes|quieres|haz\s+clic|dime|cuéntame|tómate)\b", RegexOptions.IgnoreCase);
        Assert.That(Catalog.Table("es").Where(kv => !kv.Key.StartsWith('_') && tu.IsMatch(kv.Value)).Select(kv => kv.Key), Is.Empty);
    }

    [Test]
    public void The_spanish_consent_notice_mirrors_the_english_and_hashes_differently()
    {
        foreach (var level in Enum.GetValues<Gadfly.Server.Data.IntegrityLevel>())
            foreach (var typed in new[] { false, true })
            {
                var en = ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text, level, typed);
                var es = ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text, level, typed, "es");
                Assert.That(es.Paragraphs, Has.Count.EqualTo(en.Paragraphs.Count), $"{level} typed={typed}");
                Assert.That(es.Sha256, Is.Not.EqualTo(en.Sha256));
                Assert.That(es.Title, Is.EqualTo("Antes de comenzar"));
                Assert.That(string.Join("", es.Paragraphs), Does.Contain("90 días"));
            }
    }

    [Test]
    public void The_spanish_practice_mirrors_the_english_steps_and_hears_no_lo_se()
    {
        var en = PracticeInterview.Steps;
        var es = PracticeInterview.StepsFor("es");
        Assert.That(es.Select(s => (s.Key, s.WantsAnswer)), Is.EqualTo(en.Select(s => (s.Key, s.WantsAnswer))));
        Assert.That(es.Zip(en).All(p => p.First.Points.Count == p.Second.Points.Count), Is.True);
        Assert.That(PracticeInterview.LineFor(PracticeInterview.EyeContact, true, "es"), Does.StartWith(PracticeInterview.SpanishAporiaThanks));
        Assert.That(PracticeInterview.SoundsLikeIDontKnow("Mmm... no lo sé"), Is.True);
        Assert.That(PracticeInterview.SoundsLikeIDontKnow("Es Uagadugú"), Is.False);
        Assert.That(PracticeInterview.AllLines().Distinct().Count(), Is.EqualTo(PracticeInterview.AllLines().Count()), "every line once, per language");
    }

    [Test]
    public void Student_emails_render_in_spanish_only_when_asked()
    {
        var d = new Gadfly.Server.Notifications.EmailData("Org", "Ana", "Ética 101", new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), 2, ActionUrl: "https://x/my-exams");
        var es = Gadfly.Server.Notifications.EmailTemplates.Render(Gadfly.Server.Notifications.EmailKinds.ExamAssigned, d, "es-MX");
        Assert.That(es.Subject, Does.Contain("Ética 101").And.Contain("asignó"));
        Assert.That(es.Html, Does.Contain("lang=\"es\"").And.Contain("1 de diciembre de 2026, 09:00 UTC").And.Contain("Tiene 2 intentos"));
        Assert.That(es.Text, Does.Contain("Abrir Mis exámenes: https://x/my-exams"));
        Assert.That(Gadfly.Server.Notifications.EmailTemplates.Render(Gadfly.Server.Notifications.EmailKinds.ExamAssigned, d).Subject, Is.EqualTo("Ética 101 has been assigned to you"));
        Assert.That(Gadfly.Server.Notifications.EmailTemplates.Render(Gadfly.Server.Notifications.EmailKinds.Test, d, "es").Subject, Is.EqualTo("Gadfly test email"));
    }

    [Test]
    public void The_spanish_calibration_sentence_is_the_murcielago_pangram_read_three_times()
    {
        var c = StudentLanguages.CalibrationFor("es");
        Assert.That(c.Text, Is.EqualTo("El veloz murciélago hindú comía feliz cardillo y kiwi."));
        Assert.That((c.Words, c.Syllables), Is.EqualTo((9, 20)));
        Assert.That(StudentLanguages.CalibrationTerms("es"), Does.Contain("murciélago").And.Contain("kiwi"));
        Assert.That(StudentLanguages.CalibrationReadings, Is.EqualTo(3));
    }
}
