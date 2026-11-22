using Gadfly.Server.Student;
using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The examiners a student can choose on the get-ready page (GAD-T595).</summary>
[TestFixture]
public class ExaminerCatalogTests
{
    private static readonly VoiceSettings Voice = new(VoiceMode.Mock, "/assets/avatars/mpfb.glb", "/assets/portrait.png", 4);

    [Test]
    public void TheConfiguredExaminer_IsOffered_AndTheMaleOneOnlyOnceInstalled()
    {
        var empty = Path.Combine(Path.GetTempPath(), "gadfly-examiners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var options = ExaminerCatalog.For(Voice, empty);
            Assert.That(options.Select(o => o.Id), Is.EqualTo(new[] { ExaminerCatalog.Female, ExaminerCatalog.Male }));
            Assert.That(options[0].Available, Is.True);
            Assert.That(options[0].PortraitUrl, Is.EqualTo("/assets/portrait.png"));
            Assert.That(options[1].Available, Is.False, "no portrait installed yet");

            Directory.CreateDirectory(Path.Combine(empty, "assets"));
            File.WriteAllBytes(Path.Combine(empty, ExaminerCatalog.MalePortraitPath), [1]);
            Assert.That(ExaminerCatalog.For(Voice, empty)[1].Available, Is.True);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Test]
    public void Pick_KeepsARememberedExaminer_OnlyWhileOffered()
    {
        var options = ExaminerCatalog.For(Voice, webRoot: null);
        Assert.That(ExaminerCatalog.Pick(options, ExaminerCatalog.Male).Id, Is.EqualTo(ExaminerCatalog.Female), "not installed");
        Assert.That(ExaminerCatalog.Pick(options, "nobody").Id, Is.EqualTo(ExaminerCatalog.Female));
        Assert.That(ExaminerCatalog.Pick(options, ExaminerCatalog.Female).Id, Is.EqualTo(ExaminerCatalog.Female));
    }
}
