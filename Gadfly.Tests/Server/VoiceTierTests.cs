using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using VoiceSettings = Gadfly.Server.Voice.VoiceSettings;

namespace Gadfly.Tests.Server;

/// <summary>
/// The examiner's voice quality/speed tier (GAD-T415, GAD-T616): the catalog, its precedence (exam, then organization, then the
/// deployment's per-language model, then the library's), the fallback for a tier whose model does not speak the exam language, and the
/// owner's rule that an English exam by an English speaker with nothing set sounds exactly as before. No provider: profiles only.
/// </summary>
[TestFixture]
public class VoiceTierTests
{
    private const string Flash = "eleven_flash_v2_5";

    private static VoiceSettings Settings(params (string Key, string Value)[] values) =>
        VoiceSettings.From(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => "Gadfly:Voice:" + v.Key, v => (string?)v.Value)).Build(), true, null);

    private static readonly SessionSpeech EnglishNative = new(ExamLanguage.English, ExamLanguage.English);
    private static readonly SessionSpeech KoreanNative = new(ExamLanguage.Korean, ExamLanguage.Korean);
    private static readonly SessionSpeech SpanishNative = new(ExamLanguage.Spanish, ExamLanguage.Spanish);
    private static readonly SessionSpeech EnglishByKorean = new(ExamLanguage.English, ExamLanguage.Korean);

    [Test]
    public void The_catalog_is_fast_by_default_then_quality_and_every_tier_speaks_every_exam_language()
    {
        Assert.That(VoiceTier.All.Select(t => t.Id), Is.EqualTo(new[] { "fast", "quality" }));
        Assert.That(VoiceTier.Fast.ModelId, Is.EqualTo(Settings().TtsModel), "the fast tier is the deployment default");
        Assert.That(VoiceTier.Quality.ModelId, Is.EqualTo("eleven_multilingual_v2"));
        Assert.That(VoiceTier.All.All(t => t.Id.Length <= VoiceTier.MaxIdLength), Is.True);
        foreach (var tier in VoiceTier.All)
            foreach (var language in ExamLanguage.Supported)
                Assert.That(VoiceTier.Speaks(tier.ModelId, language), Is.True, $"{tier.Id} speaks {language}");
    }

    [Test]
    public void Ids_are_found_without_regard_to_case_or_spaces_and_unknown_ids_are_none()
    {
        Assert.That(VoiceTier.Find(" Quality "), Is.SameAs(VoiceTier.Quality));
        Assert.That(VoiceTier.Find("expressive"), Is.Null);
        Assert.That(VoiceTier.Find(null), Is.Null);
        Assert.That(VoiceTier.Normalize("FAST"), Is.EqualTo("fast"));
        Assert.That(VoiceTier.Normalize("turbo"), Is.Null);
        Assert.That(VoiceTier.ForModel("eleven_multilingual_v2"), Is.SameAs(VoiceTier.Quality));
        Assert.That(VoiceTier.ForModel("my_custom_model"), Is.Null);
    }

    [Test]
    public void The_exams_tier_wins_over_the_organizations_and_an_unknown_one_is_skipped()
    {
        Assert.That(VoiceTier.Resolve("quality", "fast"), Is.SameAs(VoiceTier.Quality));
        Assert.That(VoiceTier.Resolve(null, "quality"), Is.SameAs(VoiceTier.Quality));
        Assert.That(VoiceTier.Resolve("gone", "quality"), Is.SameAs(VoiceTier.Quality));
        Assert.That(VoiceTier.Resolve(null, null), Is.Null);
    }

    [Test]
    public void Model_languages_follow_elevenlabs_lists_and_unknown_models_are_trusted()
    {
        Assert.That(VoiceTier.Speaks("eleven_flash_v2", "ko"), Is.False, "the v2 English models speak only English");
        Assert.That(VoiceTier.Speaks("eleven_turbo_v2", "es"), Is.False);
        Assert.That(VoiceTier.Speaks("eleven_multilingual_v2", "vi"), Is.False, "29 languages: not Vietnamese");
        Assert.That(VoiceTier.Speaks(Flash, "vi"), Is.True, "flash v2.5 adds Hungarian, Norwegian and Vietnamese");
        Assert.That(VoiceTier.Speaks("eleven_multilingual_v2", "es-MX"), Is.True);
        Assert.That(VoiceTier.Speaks("a_model_set_in_config", "ko"), Is.True);
    }

    [Test]
    public void An_english_exam_by_an_english_speaker_with_nothing_set_is_unchanged_and_the_fast_tier_equals_it()
    {
        Assert.That(ExaminerTts.Choose(EnglishNative, Settings(), Flash, null), Is.Null, "the owner's rule");
        Assert.That(ExaminerTts.Choose(EnglishNative, Settings(("TtsModels:en", "eleven_multilingual_v2")), Flash, null), Is.Null,
            "with no tier the default case never looks at the per-language setting, as before");
        Assert.That(ExaminerTts.Choose(EnglishNative with { VoiceTier = "fast" }, Settings(), Flash, null), Is.Null,
            "an explicit fast tier on a default deployment is today's voice");
        Assert.That(ExaminerTts.Choose(EnglishNative with { VoiceTier = "unknown" }, Settings(), Flash, null), Is.Null);
    }

    [Test]
    public void A_tier_applies_to_an_english_exam_too_and_keeps_the_plain_speech_speed()
    {
        Assert.That(ExaminerTts.Choose(EnglishNative with { VoiceTier = "quality" }, Settings(), Flash, null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", null)));
        Assert.That(ExaminerTts.Choose(EnglishByKorean with { VoiceTier = "quality" }, Settings(), Flash, null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", 0.9)));
        Assert.That(ExaminerTts.Choose(EnglishByKorean with { VoiceTier = "fast" }, Settings(), Flash, null),
            Is.EqualTo(new ExaminerTts.Profile(Flash, 0.9)), "the speed still differs");
    }

    [Test]
    public void A_tier_wins_over_the_per_language_deployment_model_which_wins_over_the_librarys()
    {
        var koreanOnTurbo = Settings(("TtsModels:ko", "eleven_turbo_v2_5"));
        Assert.That(ExaminerTts.Choose(KoreanNative, koreanOnTurbo, Flash, null), Is.EqualTo(new ExaminerTts.Profile("eleven_turbo_v2_5", null)));
        Assert.That(ExaminerTts.Choose(KoreanNative with { VoiceTier = "quality" }, koreanOnTurbo, Flash, null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_multilingual_v2", null)));
        Assert.That(ExaminerTts.Choose(KoreanNative with { VoiceTier = "fast" }, koreanOnTurbo, Flash, null), Is.Null,
            "an explicit fast tier is the library's own model here");
        Assert.That(ExaminerTts.Choose(SpanishNative with { VoiceTier = "quality" }, Settings(), "eleven_multilingual_v2", null), Is.Null,
            "a deployment already on the quality model needs no client of its own");
    }

    [Test]
    public void A_tier_whose_model_does_not_speak_the_exam_language_falls_back_to_the_deployments_model()
    {
        // No exam language is outside both tiers today, so the fallback is shown on a language the quality model lacks.
        var vietnamese = new SessionSpeech("vi", "vi") { VoiceTier = "quality" };
        Assert.That(ExaminerTts.TierFallsBack(vietnamese), Is.SameAs(VoiceTier.Quality));
        Assert.That(ExaminerTts.Choose(vietnamese, Settings(), Flash, null), Is.Null, "the library's model speaks it");
        Assert.That(ExaminerTts.Choose(vietnamese, Settings(("TtsModels:vi", "eleven_turbo_v2_5")), Flash, null),
            Is.EqualTo(new ExaminerTts.Profile("eleven_turbo_v2_5", null)));
        Assert.That(ExaminerTts.TierFallsBack(KoreanNative with { VoiceTier = "quality" }), Is.Null);
        Assert.That(ExaminerTts.TierFallsBack(KoreanNative), Is.Null);
    }

    // ------------------------------------------------------------------------------------------ admin (GAD-T617)

    [Test]
    public void The_exam_definition_carries_the_tier_and_rejects_an_unknown_one()
    {
        var exam = new Exam { Title = "Cells", VoiceTier = "quality", PolicyJson = "{}" };
        var json = Gadfly.Server.Admin.ExamDefinitionJson.Serialize(
            Gadfly.Server.Admin.ExamDefinitionJson.Build(exam, [], null, null, null, DateTimeOffset.UtcNow));
        var read = Gadfly.Server.Admin.ExamDefinitionJson.Parse(json);
        Assert.That(read.Errors, Is.Empty);
        Assert.That(read.Definition!.VoiceTier, Is.EqualTo("quality"));

        var bad = Gadfly.Server.Admin.ExamDefinitionJson.Parse(json.Replace("\"quality\"", "\"expressive\""));
        Assert.That(bad.Errors, Has.Some.Contains("voice tier"));
    }

    [Test]
    public void A_copy_keeps_the_exams_tier()
    {
        var exam = new Exam { Title = "Cells", VoiceTier = "quality", PolicyJson = "{}" };
        var copy = Gadfly.Server.Admin.ExamAdminService.CopyOf(exam, [], null, "Cells (copy)", Guid.NewGuid(), DateTimeOffset.UtcNow, seed: 1);
        Assert.That(copy.Exam.VoiceTier, Is.EqualTo("quality"));
    }

    [Test]
    public void The_median_is_the_middle_value_or_the_mean_of_the_middle_two()
    {
        Assert.That(VoiceTierLatency.Median([]), Is.Null);
        Assert.That(VoiceTierLatency.Median([900, 100, 300]), Is.EqualTo(300));
        Assert.That(VoiceTierLatency.Median([100, 200, 300, 401]), Is.EqualTo(251));
    }

    [Test]
    public void The_per_model_timing_name_fits_the_timing_table_for_every_tier()
    {
        foreach (var tier in VoiceTier.All)
            Assert.That((QuestionVoiceStream.ModelTimingPrefix + tier.ModelId).Length, Is.LessThanOrEqualTo(80));
        Assert.That(QuestionVoiceStream.ModelTimingPrefix, Does.StartWith("voice.question.firstAudio"));
    }

    // ------------------------------------------------------------------------------------------ read from the database

    [Test]
    public async Task A_sessions_tier_is_the_exams_else_the_organizations_also_for_an_english_exam()
    {
        if (ServerFixture.Unavailable is { } why) Assert.Ignore($"Server tests unavailable: {why}");
        var world = await Seed.ExamAsync();
        var session = await SessionIdAsync(world);

        Assert.That((await ReadAsync(world, session)).VoiceTier, Is.Null, "nothing set");

        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org)))
        {
            await db.OrganizationSettings.Where(o => o.OrganizationId == world.Org).ExecuteUpdateAsync(u => u.SetProperty(o => o.VoiceTier, "quality"));
        }
        var speech = await ReadAsync(world, session);
        Assert.That(speech.IsDefault && speech.VoiceTier == "quality", Is.True, "read before the default case returns");

        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org)))
        {
            await db.Exams.Where(e => e.Id == world.ExamId).ExecuteUpdateAsync(u => u.SetProperty(e => e.VoiceTier, "fast"));
        }
        Assert.That((await ReadAsync(world, session)).VoiceTier, Is.EqualTo("fast"), "the exam's tier wins");
    }

    private static async Task<SessionSpeech> ReadAsync(Seed.ExamWorld world, Guid session)
    {
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        return await SessionSpeech.ForSessionAsync(db, world.Org, session, CancellationToken.None);
    }

    private static async Task<Guid> SessionIdAsync(Seed.ExamWorld world)
    {
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        var session = new ExamSession
        {
            OrganizationId = world.Org, AssignmentId = world.AssignmentId, ExamId = world.ExamId, StudentUserId = world.StudentId,
            State = SessionState.Running, CreatedAt = DateTimeOffset.UtcNow, TranscriptJson = "{}", LedgerJson = "{}",
        };
        db.ExamSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}

/// <summary>The exam's voice tier saved from the exams manager (GAD-T617): stored normalized and audited on its own.</summary>
[TestFixture]
public class VoiceTierAdminTests : ServerTest
{
    [Test]
    public async Task Saving_an_exams_tier_stores_it_and_audits_exam_voice_tier_changed_and_an_unknown_one_is_refused()
    {
        var w = await Seed.ExamAsync();
        var admin = Member(w.Org, Guid.NewGuid(), OrgRoles.Admin);
        await using (var scope = Scope(admin))
        {
            var svc = ActivatorUtilities.CreateInstance<Gadfly.Server.Admin.ExamAdminService>(scope.ServiceProvider);
            var form = (await svc.SettingsAsync(w.ExamId))!.Form;
            Assert.That(form.VoiceTier, Is.Empty, "nothing set: the organization's default");
            form.VoiceTier = "turbo";
            Assert.That((await svc.SaveSettingsAsync(form)).Ok, Is.False);
            form.VoiceTier = " Quality ";
            var saved = await svc.SaveSettingsAsync(form);
            Assert.That(saved.Ok, Is.True, saved.Message);
        }
        await using var check = SystemDb();
        Assert.That((await check.Exams.SingleAsync(e => e.Id == w.ExamId)).VoiceTier, Is.EqualTo("quality"));
        var audit = await check.AuditEvents.SingleAsync(a => a.Action == "exam.voice-tier.changed" && a.EntityId == w.ExamId.ToString());
        Assert.That((audit.BeforeJson, audit.AfterJson), Is.EqualTo(("{\"voiceTier\":null}", "{\"voiceTier\":\"quality\"}")));
    }
}
