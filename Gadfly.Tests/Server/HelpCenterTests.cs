using System.Text;
using System.Text.RegularExpressions;
using Gadfly.Server.Help;
using Gadfly.Server.Tenancy;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The in-app help center (GAD-T292): the catalogue, who may read what, search, and the Markdown renderer.</summary>
[TestFixture]
public class HelpCatalogTests
{
    [Test]
    public void Slugs_AndFiles_AreUnique()
    {
        Assert.That(HelpCatalog.Topics.Select(t => t.Slug.ToLowerInvariant()), Is.Unique);
        Assert.That(HelpCatalog.Topics.Select(t => t.File.ToLowerInvariant()), Is.Unique);
    }

    [Test]
    public void Slugs_AreUrlSafe() =>
        Assert.That(HelpCatalog.Topics.Where(t => !Regex.IsMatch(t.Slug, "^[a-z0-9]+(-[a-z0-9]+)*$")).Select(t => t.Slug), Is.Empty);

    [Test]
    public void EverySection_HasAPublishedTopic() =>
        Assert.That(HelpCatalog.Sections.Where(s => !HelpCatalog.Topics.Any(t => t.Section == s.Id && !t.IsStub)).Select(s => s.Title), Is.Empty);

    [Test]
    public void EveryTopic_HasATitleAndSummary() =>
        Assert.That(HelpCatalog.Topics.Where(t => string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Summary)), Is.Empty);

    [Test]
    public void Stubs_NameTheirTicket() =>
        Assert.That(HelpCatalog.Topics.Where(t => t.IsStub && !Regex.IsMatch(t.PendingTicket!, @"^GAD-T\d+$")).Select(t => t.Slug), Is.Empty);

    [Test]
    public void Find_IgnoresCase_AndUnknownIsNull()
    {
        Assert.That(HelpCatalog.Find("FAQ")?.Slug, Is.EqualTo(HelpTopics.Faq));
        Assert.That(HelpCatalog.Find("no-such-topic"), Is.Null);
        Assert.That(HelpCatalog.Find(null), Is.Null);
        Assert.That(HelpCatalog.FindByFile("student_guide.md")?.Slug, Is.EqualTo(HelpTopics.StudentGuide));
    }

    [Test]
    public void TheTroubleshootingAndPrivacyTopics_ArePublic()
    {
        // A student whose microphone won't work may never get past the sign-in page.
        foreach (var slug in new[] { HelpTopics.Microphone, HelpTopics.Camera, HelpTopics.Browser, HelpTopics.Privacy, HelpTopics.HowTheExamWorks, HelpTopics.BeforeYourExam })
            Assert.That(HelpCatalog.Find(slug)!.Audience, Is.EqualTo(HelpAudience.Public), slug);
    }

    [Test]
    public void EveryTopicsFile_IsInDocsGuides_AndEveryGuideIsATopic()
    {
        var guides = RepoFiles.GuidesFolder();
        if (guides is null) Assert.Ignore("docs/guides not found next to the test output");
        var onDisk = Directory.GetFiles(guides!, "*.md").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(HelpCatalog.Topics.Where(t => !onDisk.Contains(t.File)).Select(t => t.File), Is.Empty, "catalogue entries without a file");
        Assert.That(onDisk.Where(f => HelpCatalog.FindByFile(f) is null), Is.Empty, "guides that no topic shows");
    }

    [Test]
    public void TheGuides_AreEmbeddedInTheApp()
    {
        var library = new HelpLibrary();
        Assert.That(library.Missing.Select(t => t.File), Is.Empty, "Gadfly.Server.csproj must embed docs/guides (EmbeddedResource, LogicalName Gadfly.Help/...)");
        Assert.That(library.Get(HelpTopics.StudentGuide)!.Document!.Headings, Is.Not.Empty);
    }

    [Test]
    public void EveryHelpLinkOnAPage_NamesARealTopic()
    {
        var server = RepoFiles.ServerFolder();
        if (server is null) Assert.Ignore("Gadfly.Server not found next to the test output");
        var bad = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(server!, "Components"), "*.razor", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "<HelpLink\\s+Topic=\"([^\"@]+)\""))
                if (HelpCatalog.Find(m.Groups[1].Value) is null) bad.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        Assert.That(bad, Is.Empty);
    }
}

[TestFixture]
public class HelpRoleFilteringTests
{
    private static HashSet<HelpAudience> Sees(bool signedIn, string? role, bool superAdmin = false) =>
        HelpCatalog.VisibleTo(HelpViewer.For(signedIn, role, superAdmin)).Select(t => t.Audience).ToHashSet();

    [Test]
    public void SignedOut_SeesOnlyPublicTopics() =>
        Assert.That(Sees(false, null), Is.EquivalentTo(new[] { HelpAudience.Public }));

    [Test]
    public void SignedOut_IgnoresAnyRole() =>
        Assert.That(Sees(false, OrgRoles.Owner), Is.EquivalentTo(new[] { HelpAudience.Public }));

    [Test]
    public void Student_SeesPublicAndStudentTopicsOnly() =>
        Assert.That(Sees(true, OrgRoles.Student), Is.EquivalentTo(new[] { HelpAudience.Public, HelpAudience.Student }));

    [TestCase(OrgRoles.Examiner)]
    [TestCase(OrgRoles.SecondReviewer)]
    public void Graders_SeeGradingButNotInstructorOrAdminTopics(string role) =>
        Assert.That(Sees(true, role), Is.EquivalentTo(new[] { HelpAudience.Public, HelpAudience.Student, HelpAudience.Grader }));

    [Test]
    public void Instructor_SeesEverythingButAdminTopics() =>
        Assert.That(Sees(true, OrgRoles.Instructor), Is.EquivalentTo(new[] { HelpAudience.Public, HelpAudience.Student, HelpAudience.Grader, HelpAudience.Instructor }));

    [TestCase(OrgRoles.Admin)]
    [TestCase(OrgRoles.Owner)]
    public void AdminsAndOwners_SeeEverything(string role) =>
        Assert.That(Sees(true, role), Is.EquivalentTo(Enum.GetValues<HelpAudience>()));

    [Test]
    public void SuperAdmin_SeesEverything_EvenWithoutAnOrganization() =>
        Assert.That(Sees(true, null, superAdmin: true), Is.EquivalentTo(Enum.GetValues<HelpAudience>()));

    [Test]
    public void SignedInWithoutAnOrganization_SeesPublicTopics() =>
        Assert.That(Sees(true, null), Is.EquivalentTo(new[] { HelpAudience.Public }));

    [Test]
    public void StartTopic_FollowsTheRole()
    {
        Assert.That(HelpCatalog.StartFor(HelpViewer.Anonymous).Slug, Is.EqualTo(HelpTopics.HowTheExamWorks));
        Assert.That(HelpCatalog.StartFor(HelpViewer.For(true, OrgRoles.Student, false)).Slug, Is.EqualTo(HelpTopics.QuickStartStudent));
        Assert.That(HelpCatalog.StartFor(HelpViewer.For(true, OrgRoles.Examiner, false)).Slug, Is.EqualTo(HelpTopics.QuickStartGrader));
        Assert.That(HelpCatalog.StartFor(HelpViewer.For(true, OrgRoles.Instructor, false)).Slug, Is.EqualTo(HelpTopics.QuickStartInstructor));
        Assert.That(HelpCatalog.StartFor(HelpViewer.For(true, OrgRoles.Admin, false)).Slug, Is.EqualTo(HelpTopics.QuickStartAdmin));
    }

    [Test]
    public void GetFor_SaysForbidden_ForAnotherRolesTopic_AndNotFoundForNoTopic()
    {
        var library = HelpFixtures.Library();
        var student = HelpViewer.For(true, OrgRoles.Student, false);

        Assert.That(library.GetFor(HelpTopics.AdminGuide, student, out var forbidden), Is.Null);
        Assert.That(forbidden, Is.True);
        Assert.That(library.GetFor("no-such-topic", student, out forbidden), Is.Null);
        Assert.That(forbidden, Is.False);
        Assert.That(library.GetFor(HelpTopics.Microphone, HelpViewer.Anonymous, out forbidden), Is.Not.Null);
        Assert.That(library.GetFor(HelpTopics.StudentGuide, HelpViewer.Anonymous, out forbidden), Is.Null);
        Assert.That(forbidden, Is.True);
    }
}

[TestFixture]
public class HelpSearchTests
{
    private HelpLibrary _library = null!;

    [OneTimeSetUp]
    public void Build() => _library = HelpFixtures.Library();

    [Test]
    public void FindsATopicByAWordInItsText()
    {
        var hits = _library.Search("headset", HelpViewer.Anonymous);
        Assert.That(hits.Select(h => h.Topic.Slug), Does.Contain(HelpTopics.Microphone));
    }

    [Test]
    public void TitleMatches_RankFirst()
    {
        var hits = _library.Search("camera problems", HelpViewer.Anonymous);
        Assert.That(hits.First().Topic.Slug, Is.EqualTo(HelpTopics.Camera));
    }

    [Test]
    public void APrefix_MatchesTheWholeWord() =>
        Assert.That(_library.Search("micro", HelpViewer.Anonymous).Select(h => h.Topic.Slug), Does.Contain(HelpTopics.Microphone));

    [Test]
    public void Plurals_MatchTheSingular() =>
        Assert.That(_library.Search("microphones", HelpViewer.Anonymous).Select(h => h.Topic.Slug), Does.Contain(HelpTopics.Microphone));

    [Test]
    public void EveryWord_MustMatch() =>
        Assert.That(_library.Search("headset zebracorn", HelpViewer.Anonymous), Is.Empty);

    [Test]
    public void AnEmptyOrStopWordQuery_FindsNothing()
    {
        Assert.That(_library.Search("", HelpViewer.Anonymous), Is.Empty);
        Assert.That(_library.Search(null, HelpViewer.Anonymous), Is.Empty);
        Assert.That(_library.Search("how do I", HelpViewer.Anonymous), Is.Empty);
    }

    [Test]
    public void AReader_NeverFindsTopicsTheyMayNotOpen()
    {
        var student = HelpViewer.For(true, OrgRoles.Student, false);
        var hits = _library.Search("webhook", student);
        Assert.That(hits.Where(h => !student.CanRead(h.Topic)), Is.Empty);
        Assert.That(hits.Select(h => h.Topic.Slug), Does.Not.Contain(HelpTopics.AdminGuide));
        Assert.That(_library.Search("webhook", HelpViewer.For(true, OrgRoles.Admin, false)).Select(h => h.Topic.Slug), Does.Contain(HelpTopics.AdminGuide));
    }

    [Test]
    public void AHit_PointsAtTheSectionItWasFoundIn()
    {
        var hits = _library.Search("bluetooth", HelpViewer.Anonymous).Where(h => h.Topic.Slug == HelpTopics.Microphone).ToList();
        Assert.That(hits.Any(h => !string.IsNullOrEmpty(h.SectionAnchor)), Is.True);
    }

    [Test]
    public void Excerpts_MarkTheWords_AndAreEncoded()
    {
        var library = new HelpLibrary(["faq.md"], _ => Stream("# FAQ\n\n## Odd\n\nA <script>alert(1)</script> and the headset text."));
        var hit = library.Search("headset", HelpViewer.Anonymous).First();
        Assert.That(hit.ExcerptHtml, Does.Contain("<mark>headset</mark>"));
        Assert.That(hit.ExcerptHtml, Does.Not.Contain("<script>"));
    }

    [Test]
    public void EachTopic_AppearsAtMostThreeTimes() =>
        Assert.That(_library.Search("exam", HelpViewer.Everything, max: 100).GroupBy(h => h.Topic.Slug).Max(g => g.Count()), Is.LessThanOrEqualTo(3));

    private static Stream Stream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
}

[TestFixture]
public class HelpMarkdownTests
{
    [Test]
    public void TheFirstHeading_IsTheTitle_AndTheRestGetAnchors()
    {
        var doc = HelpMarkdown.Render("# Guide\n\nIntro.\n\n## Before you start\n\nText.\n\n### Step one\n\nMore.");
        Assert.That(doc.Title, Is.EqualTo("Guide"));
        Assert.That(doc.Headings.Select(h => (h.Level, h.Anchor)), Is.EqualTo(new[] { (2, "before-you-start"), (3, "step-one") }));
        Assert.That(doc.Html, Does.Contain("<h2 id=\"before-you-start\">"));
        Assert.That(doc.Html, Does.Not.Contain("<h1"));
    }

    [Test]
    public void RepeatedHeadings_GetDistinctAnchors()
    {
        var doc = HelpMarkdown.Render("# T\n\n## Tips\n\na\n\n## Tips\n\nb");
        Assert.That(doc.Headings.Select(h => h.Anchor), Is.EqualTo(new[] { "tips", "tips-1" }));
    }

    [Test]
    public void RawHtml_IsShownAsText()
    {
        var doc = HelpMarkdown.Render("# T\n\nHello <img src=x onerror=alert(1)> **bold**");
        Assert.That(doc.Html, Does.Not.Contain("<img"));
        Assert.That(doc.Html, Does.Contain("&lt;img"));
        Assert.That(doc.Html, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Tables_HaveAHeaderRow()
    {
        var doc = HelpMarkdown.Render("# T\n\n| A | B |\n|---|---|\n| 1 | `x|y` |\n");
        Assert.That(doc.Html, Does.Contain("<th scope=\"col\">A</th>"));
        Assert.That(doc.Html, Does.Contain("<td>1</td>"));
    }

    [Test]
    public void NestedAndNumberedLists_AndChecklists()
    {
        var doc = HelpMarkdown.Render("# T\n\n1. One\n   - inner\n2. Two\n\n- [ ] Tick me\n- [x] Done");
        Assert.That(doc.Html, Does.Contain("<ol>"));
        Assert.That(doc.Html, Does.Contain("<ul>\n<li>inner</li>"));
        Assert.That(doc.Html, Does.Contain("help-checklist"));
        Assert.That(doc.Html, Does.Contain("type=\"checkbox\" checked"));
    }

    [Test]
    public void FencedCode_IsKeptVerbatimAndEncoded()
    {
        var doc = HelpMarkdown.Render("# T\n\n```\n<b>**not bold**</b>\n```\n");
        Assert.That(doc.Html, Does.Contain("<pre><code>&lt;b&gt;**not bold**&lt;/b&gt;</code></pre>"));
    }

    [Test]
    public void LinksBetweenGuides_BecomeHelpPages_AndOutsideDocsAreText()
    {
        var library = new HelpLibrary(["faq.md", "STUDENT_GUIDE.md", "images/check.png"], name => name switch
        {
            "faq.md" => new MemoryStream(Encoding.UTF8.GetBytes(
                "# FAQ\n\nSee [the guide](STUDENT_GUIDE.md#your-data), [privacy](../PRIVACY.md), [web](https://example.org), " +
                "[top](#faq) and ![check](images/check.png).")),
            "STUDENT_GUIDE.md" => new MemoryStream(Encoding.UTF8.GetBytes("# Student guide\n\n## Your data\n\nx")),
            _ => new MemoryStream([1, 2, 3]),
        });
        var html = library.Get(HelpTopics.Faq)!.Document!.Html;
        Assert.That(html, Does.Contain("href=\"help/student-guide#your-data\""));
        Assert.That(html, Does.Not.Contain("PRIVACY.md"));
        Assert.That(html, Does.Contain("href=\"https://example.org\" target=\"_blank\""));
        Assert.That(html, Does.Contain("href=\"help/faq#faq\""));
        Assert.That(html, Does.Contain("src=\"help/media/images/check.png\""));
        Assert.That(library.OpenMedia("images/check.png"), Is.Not.Null);
        Assert.That(library.OpenMedia("../appsettings.json"), Is.Null);
        Assert.That(library.OpenMedia("faq.md"), Is.Null);
    }

    [Test]
    public void Callouts_GetAClass()
    {
        var doc = HelpMarkdown.Render("# T\n\n> **Coming soon.** Being built.\n");
        Assert.That(doc.Html, Does.Contain("class=\"help-callout help-coming\""));
    }

    [Test]
    public void Sections_CarryPlainText_ForSearch()
    {
        var doc = HelpMarkdown.Render("# T\n\nLead.\n\n## Fix **it**\n\nUse a [headset](x.md).");
        Assert.That(doc.Sections.Last().Heading, Is.EqualTo("Fix it"));
        Assert.That(doc.Sections.Last().Text, Is.EqualTo("Use a headset."));
    }
}

/// <summary>Test helpers: the guides from the repository, and where the repository is.</summary>
internal static class HelpFixtures
{
    /// <summary>The real guides from docs/guides when the repository is next to the test output, else the embedded copies.</summary>
    public static HelpLibrary Library()
    {
        var guides = RepoFiles.GuidesFolder();
        if (guides is null) return new HelpLibrary();
        var files = Directory.GetFiles(guides, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(guides, f).Replace('\\', '/')).ToList();
        return new HelpLibrary(files, name => File.OpenRead(Path.Combine(guides, name)));
    }
}

internal static class RepoFiles
{
    public static string? Root()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Gadfly.slnx"))) return dir.FullName;
        return null;
    }

    public static string? GuidesFolder() => Root() is { } r && Directory.Exists(Path.Combine(r, "docs", "guides")) ? Path.Combine(r, "docs", "guides") : null;
    public static string? ServerFolder() => Root() is { } r && Directory.Exists(Path.Combine(r, "Gadfly.Server")) ? Path.Combine(r, "Gadfly.Server") : null;
}
