using Gadfly.Server.Student;
using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The examiners a student can choose on the get-ready page (GAD-T595), and an exam or organization can fix (GAD-T504).</summary>
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
    [Test]
    public void TheMaleExaminer_HasHisOwnPortraitMouthAndVoice_AndSettingsOverrideThem()
    {
        var male = ExaminerCatalog.For(Voice, webRoot: null)[1];
        Assert.That(male.PortraitMouth, Is.EqualTo(ExaminerCatalog.MalePortraitMouth));
        Assert.That(male.VoiceIdFor("en"), Is.EqualTo(ExaminerCatalog.MaleDefaultVoiceId));
        Assert.That(male.VoiceIdFor("ko"), Is.EqualTo(ExaminerCatalog.MaleDefaultVoiceId), "the premade voice speaks Korean too");
        Assert.That(ExaminerCatalog.For(Voice, webRoot: null)[0].VoiceIdFor("en"), Is.Null, "hers stays the platform voice");

        var configured = Voice with
        {
            Examiners = new Dictionary<string, ExaminerSettings>
            {
                [ExaminerCatalog.Male] = new("https://cdn.example/m.glb", "https://cdn.example/m.png", null, "maleVoice0123456789",
                    new Dictionary<string, string> { ["ko"] = "koreanMaleVoice0123" }),
            },
        };
        var m = ExaminerCatalog.For(configured, webRoot: null)[1];
        Assert.That((m.ModelUrl, m.PortraitUrl, m.Available), Is.EqualTo(("https://cdn.example/m.glb", "https://cdn.example/m.png", true)));
        Assert.That(m.PortraitMouth, Is.Null, "a hosted portrait's mouth is unknown unless configured");
        Assert.That((m.VoiceIdFor("en"), m.VoiceIdFor("ko")), Is.EqualTo(("maleVoice0123456789", "koreanMaleVoice0123")));
    }

    [Test]
    public void AnExamOrOrganizationExaminer_IsFixed_OtherwiseTheStudentsChoiceStands()
    {
        var empty = Path.Combine(Path.GetTempPath(), "gadfly-examiners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "assets"));
        try
        {
            File.WriteAllBytes(Path.Combine(empty, ExaminerCatalog.MalePortraitPath), [1]);
            var options = ExaminerCatalog.For(Voice, empty);
            Assert.That(ExaminerCatalog.Fixed(options, null, null), Is.Null);
            Assert.That(ExaminerCatalog.Resolve(options, null, null, ExaminerCatalog.Male).Id, Is.EqualTo(ExaminerCatalog.Male));
            Assert.That(ExaminerCatalog.Resolve(options, " EXAMINER-M ", ExaminerCatalog.Female, ExaminerCatalog.Female).Id, Is.EqualTo(ExaminerCatalog.Male), "the exam's wins");
            Assert.That(ExaminerCatalog.Resolve(options, null, ExaminerCatalog.Female, ExaminerCatalog.Male).Id, Is.EqualTo(ExaminerCatalog.Female), "then the organization's");
            Assert.That(ExaminerCatalog.Resolve(options, "avatar-1", null, ExaminerCatalog.Male).Id, Is.EqualTo(ExaminerCatalog.Male), "an unknown id fixes nothing");
            Assert.That(ExaminerCatalog.Fixed(ExaminerCatalog.For(Voice, webRoot: null), ExaminerCatalog.Male, null), Is.Null, "not installed");
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Test]
    public void TheExaminersOwnVoice_ComesAfterTheExamsAndBeforeTheOrganizations()
    {
        const string exam = "examVoice012345", examiner = "examinerVoice012", org = "orgVoice0123456", lang = "langVoice012345";
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, exam, examiner, org, lang), Is.EqualTo(exam));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, null, examiner, org, lang), Is.EqualTo(examiner));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, null, null, org, lang), Is.EqualTo(org));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Live, null, null, null, lang), Is.EqualTo(lang));
        Assert.That(ExaminerVoice.Choose(VoiceMode.Mock, exam, examiner, org, lang), Is.Null, "no provider voice outside live mode");
    }
}
