using System.Reflection;
using System.Text.RegularExpressions;
using Gadfly.Server.Localization;
using Gadfly.Server.Sessions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Step notices are keyed (GAD-T629): every notice the orchestrator sets has a key in every UI language, and the English text stays word for word.</summary>
[TestFixture]
public class StepNoticeLocalizationTests
{
    private static readonly UiCatalog Catalog = new();

    private static string Source()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Gadfly.Server", "Sessions", "SessionOrchestrator.cs"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "Gadfly.Server", "Sessions", "SessionOrchestrator.cs"));
    }

    private static List<(string Key, string English)> Notices() =>
        Regex.Matches(Source(), @"live\.SetNotice\(""([^""]+)"", ""([^""]+)""\)").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();

    [Test]
    public void EveryNoticeKey_ExistsInEveryLanguage_AndKeepsTheEnglishAsIs()
    {
        var notices = Notices();
        Assert.That(notices, Has.Count.EqualTo(5));
        Assert.That(typeof(SessionStep).GetProperty("NoticeKey"), Is.Not.Null);
        foreach (var (key, english) in notices)
        {
            Assert.That(new UiText(Catalog, "en")[key].Value, Is.EqualTo(english), key);
            foreach (var lang in UiCatalog.Languages.Where(l => l != UiCatalog.English))
            {
                var text = new UiText(Catalog, lang)[key];
                Assert.That(text.ResourceNotFound, Is.False, $"{key} in {lang}");
                Assert.That(text.Value, Is.Not.EqualTo(english), $"{key} in {lang} is still English");
            }
        }
    }
}
