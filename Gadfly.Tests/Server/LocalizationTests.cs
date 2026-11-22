using System.Text.RegularExpressions;
using Gadfly.Server.Localization;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The UI localization framework (GAD-T543) and the string files (GAD-T544): English is the fallback, every key used in the code
/// exists, every translation keeps the English text's placeholders, and nothing translated is orphaned.
/// </summary>
[TestFixture]
public class LocalizationTests
{
    private static readonly UiCatalog Catalog = new();

    private static UiCatalog Small() => new(new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["en"] = new Dictionary<string, string> { ["a.hello"] = "Hello", ["a.count"] = "{0} attempts", ["b.only"] = "English only" },
        ["ko"] = new Dictionary<string, string> { ["a.hello"] = "안녕하세요", ["a.count"] = "{0}회 응시" },
    });

    [Test]
    public void Text_IsInTheLanguage_WithEnglishFallback_AndTheKeyOnlyWhenUndefined()
    {
        var t = new UiText(Small(), "ko-KR");
        Assert.That(t.Language, Is.EqualTo("ko"));
        Assert.That(t["a.hello"].Value, Is.EqualTo("안녕하세요"));
        Assert.That(t["b.only"].Value, Is.EqualTo("English only"));
        Assert.That(t["no.such"].ResourceNotFound, Is.True);
        Assert.That(t["a.count", 3].Value, Is.EqualTo("3회 응시"));
    }

    [Test]
    public void Use_SwitchesLive_AndRaisesChangedOnce()
    {
        var t = new UiText(Small(), "en");
        var changes = 0;
        t.Changed += () => changes++;
        t.Use("ko");
        t.Use("ko");
        Assert.That(t["a.hello"].Value, Is.EqualTo("안녕하세요"));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(t.For("en")["a.hello"].Value, Is.EqualTo("Hello"), "a fixed-language view ignores the switch");
    }

    [TestCase(null, "en")]
    [TestCase("", "en")]
    [TestCase("ko", "ko")]
    [TestCase("ko-KR", "ko")]
    [TestCase("KO_kr", "ko")]
    [TestCase("en-GB", "en")]
    [TestCase("fr", "en")]
    public void Normalize_KeepsSupportedLanguages_OnlyElseEnglish(string? tag, string expected) =>
        Assert.That(UiCatalog.Normalize(tag), Is.EqualTo(expected));

    [Test]
    public void Dictionary_FillsUntranslatedKeysWithEnglish_UnderThePrefix()
    {
        var d = Small().Dictionary("ko", "a.");
        Assert.That(d.Keys, Is.EquivalentTo(new[] { "a.hello", "a.count" }));
        Assert.That(Small().Dictionary("ko", "b.")["b.only"], Is.EqualTo("English only"));
    }

    [Test]
    public void Dates_FollowTheLanguage()
    {
        var at = new DateTimeOffset(2026, 3, 5, 14, 30, 0, TimeSpan.Zero);
        Assert.That(new UiText(Small(), "en").DateTime(at), Is.EqualTo("Thu 5 Mar 2026, 14:30 UTC"));
        Assert.That(new UiText(Small(), "ko").DateTime(at), Is.EqualTo("2026년 3월 5일 (목) 14:30 UTC"));
    }

    [Test]
    public void EmbeddedFiles_Load_ForEveryLanguage()
    {
        foreach (var lang in UiCatalog.Languages)
            Assert.That(Catalog.Table(lang), Is.Not.Empty, lang);
    }

    [Test]
    public void EveryTranslatedKey_ExistsInEnglish() =>
        Assert.That(Catalog.Table("ko").Keys.Except(Catalog.Keys), Is.Empty, "a Korean key with no English source is orphaned");

    [Test]
    public void EveryKorean_EnglishKey_IsTranslated() =>
        Assert.That(Catalog.Keys.Except(Catalog.Table("ko").Keys), Is.Empty, "the student flow is fully translated (GAD-T544)");

    [Test]
    public void Translations_KeepTheEnglishPlaceholders()
    {
        var placeholder = new Regex(@"\{(\d+)(?:[:,][^}]*)?\}");
        string Slots(string s) => string.Join(",", placeholder.Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
        var bad = Catalog.Table("ko")
            .Where(kv => Catalog.Find("en", kv.Key) is { } en && Slots(en) != Slots(kv.Value))
            .Select(kv => kv.Key);
        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void EveryKeyTheCodeUses_IsDefinedInEnglish()
    {
        var server = RepoFiles.ServerFolder();
        if (server is null) { Assert.Ignore("Gadfly.Server sources not found"); return; }
        var use = new Regex(@"\bT\[""([a-zA-Z0-9_.]+)""");
        var missing = Directory.EnumerateFiles(server, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => use.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(u => Catalog.Find("en", u.Key) is null)
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct();
        Assert.That(missing, Is.Empty);
    }

    /// <summary>GAD-T544: the Korean notice says the same things, paragraph for paragraph, and its hash records the language read.</summary>
    [Test]
    public void KoreanConsentNotice_MirrorsTheEnglish_AndHashesDifferently()
    {
        foreach (var level in Enum.GetValues<Gadfly.Server.Data.IntegrityLevel>())
            foreach (var typed in new[] { false, true })
            {
                var en = Gadfly.Server.Student.ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text, level, typed);
                var ko = Gadfly.Server.Student.ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text, level, typed, "ko");
                Assert.That(ko.Paragraphs, Has.Count.EqualTo(en.Paragraphs.Count), $"{level} typed={typed}");
                Assert.That(ko.Sha256, Is.Not.EqualTo(en.Sha256));
                Assert.That(string.Join("", ko.Paragraphs), Does.Contain("90일"));
            }
        Assert.That(Gadfly.Server.Student.ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text, language: "en").Sha256,
            Is.EqualTo(Gadfly.Server.Student.ConsentNotice.For("Org", "Exam", 90, Gadfly.Server.Data.ExamMode.Text).Sha256), "English is unchanged");
    }

    [Test]
    public void CookieValue_NamesTheFullCulture() =>
        Assert.That(LocalizationSetup.CookieValue("ko"), Is.EqualTo("c=ko-KR|uic=ko-KR"));
}
