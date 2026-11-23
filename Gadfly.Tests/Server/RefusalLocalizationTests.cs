using System.Text.RegularExpressions;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Localization;
using Gadfly.Server.Sessions;
using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Student refusals are keyed (GAD-T628/T637): each refusal.* key's English is byte for byte the literal it replaced (logs, the
/// staff live monitor and SessionHub keep reading it), and every language with a refusal file words every key with the same slots.
/// </summary>
[TestFixture]
public class RefusalLocalizationTests
{
    private static readonly UiCatalog Catalog = UiCatalog.Embedded;

    private static string Root()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Gadfly.Server", "Sessions", "SessionOrchestrator.cs"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static List<string> RefusalKeys() => Catalog.Keys.Where(k => k.StartsWith("refusal.", StringComparison.Ordinal)).ToList();

    [Test]
    public void TheKeyedEnglish_IsTheEarlierLiteral()
    {
        Assert.That(new StudentText("refusal.heldByStaff").English, Is.EqualTo(SessionOrchestrator.HeldByStaffMessage));
        var opens = new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);
        Assert.That(new StudentText("refusal.opensAt", opens).English, Is.EqualTo($"That exam opens at {opens:u}."));
        Assert.That(new StudentText("refusal.changeLimit", 3).English, Is.EqualTo("You've already changed this answer 3 times."));
        Assert.That(new StudentText("refusal.notAssigned").English, Is.EqualTo("That exam is not assigned to you."));
        Assert.That(new StudentText("refusal.alreadyConfirmed").English,
            Is.EqualTo("That answer was already confirmed or clarified, so it can't be edited. Use \"I want to change my answer\" instead."));
        Assert.That(new SessionRefusedException(new StudentText("refusal.closed")).Message, Is.EqualTo("That exam has closed."));

        var approval = new SessionPolicy { RubricApproval = new RubricApproval(2, Guid.NewGuid(), DateTimeOffset.UnixEpoch) };
        Assert.That(RubricApprovalService.AttemptRefusal(AssessmentMode.Examination, approval, 3)!.English, Is.EqualTo(
            "This exam's marking guide was changed and is waiting for your instructor to approve it again. Please try again later."));
    }

    [TestCase("The cell wall protects the cell.", "", false)]
    [TestCase("The cell wall protects the cell.", "The cell wall protects the cell.", false)]
    [TestCase("The cell wall protects the cell.", "A membrane surrounds every plant and stores energy.", false)]
    [TestCase("The cell wall protects the cell.", "A membrane surrounds every plant and stores energy.", true)]
    [TestCase("The cell wall protects the cell.", "The cell wall does not protect the cell.", true)]
    [TestCase("The cell wall protects the cell.", "The cell door protects the cell.", false)]
    public void EveryCorrectionRefusal_KeysItsReason(string original, string corrected, bool voice)
    {
        var check = CorrectionRules.Check(original, corrected, voice);
        Assert.That(check.Allowed, Is.False);
        Assert.That(check.ReasonKey, Does.StartWith("refusal.correction."));
        Assert.That(new StudentText(check.ReasonKey!, check.ReasonArgs).English, Is.EqualTo(check.Reason));
    }

    [Test]
    public void EveryKeyTheServerThrows_IsDefined_AndNoStudentRefusalIsLeftAsALiteral()
    {
        var keys = RefusalKeys().ToHashSet();
        foreach (var file in new[] { "Sessions/SessionOrchestrator.cs", "Platform/PlanEnforcement.cs", "Assessments/RubricApprovalService.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Gadfly.Server", file));
            foreach (Match m in Regex.Matches(source, @"""(refusal\.[A-Za-z.]+)"""))
                Assert.That(keys, Does.Contain(m.Groups[1].Value), $"{m.Groups[1].Value} in {file}");
        }
        var orchestrator = File.ReadAllText(Path.Combine(Root(), "Gadfly.Server", "Sessions", "SessionOrchestrator.cs"));
        Assert.That(Regex.Matches(orchestrator, @"SessionRefusedException\(\$?""").Count, Is.Zero, "a literal refusal in SessionOrchestrator");
    }

    [Test]
    public void EveryLanguageWithARefusalFile_WordsEveryKey_WithTheSameSlots()
    {
        var dir = Path.Combine(Root(), "Gadfly.Server", "Localization", "Strings");
        var languages = Directory.GetFiles(dir, "refusal.*.json").Select(f => Path.GetFileNameWithoutExtension(f)["refusal.".Length..])
            .Where(l => l != UiCatalog.English).ToList();
        Assert.That(languages, Does.Contain(UiCatalog.Korean));
        foreach (var lang in languages)
        {
            var table = Catalog.Table(lang);
            foreach (var key in RefusalKeys())
            {
                Assert.That(table.TryGetValue(key, out var text), Is.True, $"{key} in {lang}");
                Assert.That(Slots(text!), Is.EqualTo(Slots(Catalog.Find(UiCatalog.English, key)!)), $"{key} slots in {lang}");
            }
        }
    }

    private static List<string> Slots(string s) =>
        Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order().ToList();
}
