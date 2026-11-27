using Gadfly.Server.Student;
using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The examiners a student can choose on the get-ready page (GAD-T595), and an exam or organization can fix (GAD-T504).</summary>
[TestFixture]
public class ExaminerCatalogTests
{
    private static readonly VoiceSettings Voice = new(VoiceMode.Mock, "/assets/avatars/mpfb.glb", 4);

    private static ExaminerOption Of(IReadOnlyList<ExaminerOption> options, string id) => options.Single(o => o.Id == id);

    [Test]
    public void TheConfiguredExaminer_IsOffered_AndTheMaleOneOnlyOnceInstalled()
    {
        var empty = Path.Combine(Path.GetTempPath(), "gadfly-examiners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var options = ExaminerCatalog.For(Voice, empty);
            Assert.That(options.Select(o => o.Id), Is.EqualTo(new[] { ExaminerCatalog.Female, ExaminerCatalog.Bust, ExaminerCatalog.Male, ExaminerCatalog.Socrates, ExaminerCatalog.BustBody }));
            Assert.That(Of(options, ExaminerCatalog.Female).Available, Is.True);
            Assert.That(Of(options, ExaminerCatalog.Female).ModelUrl, Is.EqualTo("/assets/avatars/mpfb.glb"));
            Assert.That(Of(options, ExaminerCatalog.Male).Available, Is.False, "no model installed yet");

            Directory.CreateDirectory(Path.Combine(empty, "assets", "avatars"));
            File.WriteAllBytes(Path.Combine(empty, ExaminerCatalog.MaleModelPath), [1]);
            Assert.That(Of(ExaminerCatalog.For(Voice, empty), ExaminerCatalog.Male).Available, Is.True);
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
    public void TheMaleExaminer_HasHisOwnVoice_AndSettingsOverrideThem()
    {
        var male = Of(ExaminerCatalog.For(Voice, webRoot: null), ExaminerCatalog.Male);
        Assert.That(male.VoiceIdFor("en"), Is.EqualTo(ExaminerCatalog.MaleDefaultVoiceId));
        Assert.That(male.VoiceIdFor("ko"), Is.EqualTo(ExaminerCatalog.MaleDefaultVoiceId), "the premade voice speaks Korean too");
        Assert.That(Of(ExaminerCatalog.For(Voice, webRoot: null), ExaminerCatalog.Female).VoiceIdFor("en"), Is.Null, "hers stays the platform voice");

        var configured = Voice with
        {
            Examiners = new Dictionary<string, ExaminerSettings>
            {
                [ExaminerCatalog.Male] = new("https://cdn.example/m.glb", "maleVoice0123456789",
                    new Dictionary<string, string> { ["ko"] = "koreanMaleVoice0123" }),
            },
        };
        var m = Of(ExaminerCatalog.For(configured, webRoot: null), ExaminerCatalog.Male);
        Assert.That((m.ModelUrl, m.Available), Is.EqualTo(("https://cdn.example/m.glb", true)));
        Assert.That((m.VoiceIdFor("en"), m.VoiceIdFor("ko")), Is.EqualTo(("maleVoice0123456789", "koreanMaleVoice0123")));
    }

    [Test]
    public void AnExamOrOrganizationExaminer_IsFixed_OtherwiseTheStudentsChoiceStands()
    {
        var empty = Path.Combine(Path.GetTempPath(), "gadfly-examiners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "assets", "avatars"));
        try
        {
            File.WriteAllBytes(Path.Combine(empty, ExaminerCatalog.MaleModelPath), [1]);
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

    [Test]
    public void Customization_Validate_KeepsOnlyOfferedValues()
    {
        var wild = new ExaminerCustomization(Voice: " nPczCjzI2devNBz1zQrb ", SkinTone: "MEDIUM-dark", Hair: "green", Attire: "clown-suit", Look: "close");
        var male = wild.Validate(ExaminerCatalog.Male);
        Assert.That(male, Is.EqualTo(new ExaminerCustomization("nPczCjzI2devNBz1zQrb", "medium-dark", null, null, "close")), "canonical form, unknowns dropped");

        Assert.That(wild.Validate(ExaminerCatalog.Female).Voice, Is.Null, "his voice is not on her list");
        Assert.That(new ExaminerCustomization(Voice: "21m00Tcm4TlvDq8ikWAM").Validate(ExaminerCatalog.Female).Voice, Is.EqualTo("21m00Tcm4TlvDq8ikWAM"));
        Assert.That(new ExaminerCustomization(Voice: "someOtherVoiceId0123").Validate(ExaminerCatalog.Male).Voice, Is.Null, "well formed but not allowlisted");
        Assert.That(new ExaminerCustomization(Voice: "<script>").Validate(ExaminerCatalog.Male).Voice, Is.Null);
        Assert.That(wild.Validate("examiner-x"), Is.EqualTo(new ExaminerCustomization(null, "medium-dark", null, null, "close")), "an unknown examiner has no voices");
        Assert.That(new ExaminerCustomization().Validate(ExaminerCatalog.Male).IsEmpty, Is.True);
    }

    [Test]
    public void Customization_EveryPaletteValue_IsAccepted()
    {
        foreach (var tone in ExaminerCustomization.SkinTones)
            Assert.That(new ExaminerCustomization(SkinTone: tone).Validate(ExaminerCatalog.Female).SkinTone, Is.EqualTo(tone));
        foreach (var hair in ExaminerCustomization.HairColours)
            Assert.That(new ExaminerCustomization(Hair: hair).Validate(ExaminerCatalog.Female).Hair, Is.EqualTo(hair));
        foreach (var attire in ExaminerCustomization.Attires)
            Assert.That(new ExaminerCustomization(Attire: attire).Validate(ExaminerCatalog.Male).Attire, Is.EqualTo(attire));
        foreach (var look in ExaminerCustomization.Looks)
            Assert.That(new ExaminerCustomization(Look: look).Validate(ExaminerCatalog.Male).Look, Is.EqualTo(look));
        foreach (var voice in ExaminerCustomization.FemaleVoices.Concat(ExaminerCustomization.MaleVoices))
            Assert.That(ExaminerVoice.IsWellFormed(voice), Is.True, voice);
    }

    [Test]
    public void Resolve_CarriesTheValidatedCustomization_UnlessTheExaminerIsFixed()
    {
        var empty = Path.Combine(Path.GetTempPath(), "gadfly-examiners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(empty, "assets", "avatars"));
        try
        {
            File.WriteAllBytes(Path.Combine(empty, ExaminerCatalog.MaleModelPath), [1]);
            var options = ExaminerCatalog.For(Voice, empty);
            var chosen = new ExaminerCustomization(Voice: "onwK4e9ZLuTAKqWW03F9", Hair: "grey", Attire: "spacesuit");

            var free = ExaminerCatalog.Resolve(options, null, null, ExaminerCatalog.Male, chosen);
            Assert.That(free.Id, Is.EqualTo(ExaminerCatalog.Male));
            Assert.That(free.Customization, Is.EqualTo(new ExaminerCustomization(Voice: "onwK4e9ZLuTAKqWW03F9", Hair: "grey")));
            Assert.That(free.VoiceIdFor("en"), Is.EqualTo("onwK4e9ZLuTAKqWW03F9"), "the chosen voice, not his default");
            Assert.That(free.VoiceIdFor("ko"), Is.EqualTo("onwK4e9ZLuTAKqWW03F9"), "premade voices speak Korean");

            var byExam = ExaminerCatalog.Resolve(options, ExaminerCatalog.Male, null, ExaminerCatalog.Male, chosen);
            Assert.That(byExam.Customization.IsEmpty, Is.True, "an exam's examiner is not customized");
            Assert.That(byExam.VoiceIdFor("en"), Is.EqualTo(ExaminerCatalog.MaleDefaultVoiceId));
            Assert.That(ExaminerCatalog.Resolve(options, null, ExaminerCatalog.Female, null, chosen).Customization.IsEmpty, Is.True, "nor an organization's");

            var fellBack = ExaminerCatalog.Resolve(options, null, null, "nobody", chosen);
            Assert.That((fellBack.Id, fellBack.Customization.Voice, fellBack.Customization.Hair), Is.EqualTo((ExaminerCatalog.Female, (string?)null, "grey")),
                "his voice is dropped when the female examiner is shown instead");
            Assert.That(ExaminerCatalog.Resolve(options, null, null, ExaminerCatalog.Male, null).Customization.IsEmpty, Is.True);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Test]
    public void ResolvedExaminer_RechecksTheVoiceForTheLanguage()
    {
        var female = Of(ExaminerCatalog.For(Voice, webRoot: null), ExaminerCatalog.Female);
        var tampered = new ResolvedExaminer(female, new ExaminerCustomization(Voice: ExaminerCatalog.MaleDefaultVoiceId));
        Assert.That(tampered.VoiceIdFor("en"), Is.Null, "a voice not on her list is never spoken with, even if it reaches here unvalidated");
        Assert.That(new ResolvedExaminer(female, new ExaminerCustomization(Voice: "XB0fDUnXU5powFXDhCwa")).VoiceIdFor("es"), Is.EqualTo("XB0fDUnXU5powFXDhCwa"));
    }
}
