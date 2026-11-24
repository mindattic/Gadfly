using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>One examiner a student can choose on the get-ready page (GAD-T595), and an exam or organization can fix (GAD-T504).</summary>
/// <param name="Id">Stable id, also what an exam's AvatarId and an organization's DefaultAvatarId name.</param>
/// <param name="NameKey">UI string key of the examiner's description ("getReady.examiner.choiceFemale").</param>
/// <param name="ModelUrl">The 3D model, or null when none is installed (the exam room then shows her live presence).</param>
/// <param name="Available">False while the examiner's model isn't installed: listed as coming soon, not choosable.</param>
public sealed record ExaminerOption(string Id, string NameKey, string? ModelUrl, bool Available)
{
    /// <summary>The examiner's own ElevenLabs voice for every language (null: the usual platform voice).</summary>
    public string? VoiceId { get; init; }

    /// <summary>The examiner's own voice for one exam language (normalized tag), where it differs from <see cref="VoiceId"/>.</summary>
    public IReadOnlyDictionary<string, string> VoiceIds { get; init; } = new Dictionary<string, string>();

    /// <summary>A man (GAD-T726): male pronouns in the room's lines, and TalkingHead's male body for the lip-sync and posture.</summary>
    public bool IsMale { get; init; }

    /// <summary>
    /// The examiner's own speaking speed (GAD-T726, ElevenLabs <c>voice_settings.speed</c>, 0.7 to 1.2), or null for the voice's own.
    /// A session's own speed (<see cref="Voice.SessionSpeech.Speed"/>, a second-language speaker's plainer pace) wins over it.
    /// </summary>
    public double? Speed { get; init; }

    /// <summary>The examiner's own voice for <paramref name="language"/>, or null for the platform's. Only well-formed ids.</summary>
    public string? VoiceIdFor(string? language)
    {
        var id = VoiceIds.TryGetValue(Gadfly.Engine.ExamLanguage.Normalize(language), out var v) ? v : VoiceId;
        return ExaminerVoice.IsWellFormed(id) ? id!.Trim() : null;
    }
}

/// <summary>
/// The examiners Gadfly offers (GAD-T595, GAD-T504): the female examiner (TalkingHead's CC0 mpfb.glb, the deployment's
/// <c>Gadfly:Voice:AvatarModelUrl</c>) and the male counterpart (<c>wwwroot/assets/avatars/examiner-m.glb</c>, CC0, built by
/// tools/examiner-m), each with its own voice. A list rather than one hard-coded examiner, so adding one is a row
/// here. Any part can be overridden per examiner with <c>Gadfly:Voice:Examiners:&lt;id&gt;</c> (<see cref="ExaminerSettings"/>).
/// <para>
/// Who chooses: an exam's <c>AvatarId</c>, else its organization's <c>DefaultAvatarId</c>, fixes the examiner for everyone taking it;
/// with neither, the student chooses on the get-ready page (remembered on the device, device-prefs.js <c>getExaminer</c>), else the
/// first available (<see cref="Resolve"/>). The server resolves it again in the exam room, so the browser's choice is only a
/// preference among the examiners the exam allows.
/// </para>
/// </summary>
public static class ExaminerCatalog
{
    public const string Female = "examiner-f";
    public const string Male = "examiner-m";

    /// <summary>The male examiner's model (relative to wwwroot), committed with GAD-T504.</summary>
    public const string MaleModelPath = "assets/avatars/examiner-m.glb";

    /// <summary>
    /// The male examiner's default ElevenLabs voice: "Brian", a premade voice described as deep, resonant and comforting (calm and
    /// warm, an American man). Premade voices speak every language of the multilingual models, Korean included with
    /// <c>eleven_flash_v2_5</c> (the default) and <c>eleven_multilingual_v2</c>. Change it with
    /// <c>Gadfly:Voice:Examiners:examiner-m:VoiceId</c> (or <c>:VoiceIds:ko</c> for one language). Used in live mode only.
    /// </summary>
    public const string MaleDefaultVoiceId = "nPczCjzI2devNBz1zQrb";

    public const string Socrates = "examiner-socrates";

    /// <summary>Socrates' model (relative to wwwroot), built by tools/examiner-socrates (GAD-T724).</summary>
    public const string SocratesModelPath = "assets/avatars/examiner-socrates.glb";

    /// <summary>
    /// Socrates' default ElevenLabs voice: "Bill", a premade voice described as an old American man, wise, mature and balanced, the
    /// oldest-sounding of the premade male voices. Premade, so it speaks every language of the multilingual models like Brian.
    /// Change it with <c>Gadfly:Voice:Examiners:examiner-socrates:VoiceId</c>. Used in live mode only.
    /// </summary>
    public const string SocratesDefaultVoiceId = "pqHfZKP75CvOlQylNhV4";

    /// <summary>
    /// Socrates' default speaking speed: a little slower than Bill's own pace, so he sounds unhurried. Change it with
    /// <c>Gadfly:Voice:Examiners:examiner-socrates:Speed</c> (0.7 to 1.2; 1 is the voice's own pace). Used in live mode only.
    /// </summary>
    public const double SocratesDefaultSpeed = 0.9;

    /// <param name="voice">The site's voice settings: the female examiner is the configured one; <see cref="VoiceSettings.Examiners"/> overrides.</param>
    /// <param name="webRoot">wwwroot on disk, to see whether an examiner's files are installed (null: only configured URLs count).</param>
    public static IReadOnlyList<ExaminerOption> For(VoiceSettings voice, string? webRoot)
    {
        var f = voice.Examiners.GetValueOrDefault(Female);
        var m = voice.Examiners.GetValueOrDefault(Male);
        string? Local(string path) => webRoot is not null && File.Exists(Path.Combine(webRoot, path)) ? "/" + path : null;
        var maleModel = m?.ModelUrl ?? Local(MaleModelPath);
        var s = voice.Examiners.GetValueOrDefault(Socrates);
        var socratesModel = s?.ModelUrl ?? Local(SocratesModelPath);
        return
        [
            new(Female, "getReady.examiner.choiceFemale", f?.ModelUrl ?? voice.AvatarModelUrl, true)
            {
                VoiceId = f?.VoiceId,
                VoiceIds = f?.VoiceIds ?? new Dictionary<string, string>(),
                Speed = f?.Speed,
            },
            new(Male, "getReady.examiner.choiceMale", maleModel, maleModel is not null)
            {
                VoiceId = m?.VoiceId ?? MaleDefaultVoiceId,
                VoiceIds = m?.VoiceIds ?? new Dictionary<string, string>(),
                IsMale = true,
                Speed = m?.Speed,
            },
            new(Socrates, "getReady.examiner.choiceSocrates", socratesModel, socratesModel is not null)
            {
                VoiceId = s?.VoiceId ?? SocratesDefaultVoiceId,
                VoiceIds = s?.VoiceIds ?? new Dictionary<string, string>(),
                IsMale = true,
                Speed = s?.Speed ?? SocratesDefaultSpeed,
            },
        ];
    }

    /// <summary>The examiner to show first: the remembered one if it is still offered, else the first available.</summary>
    public static ExaminerOption Pick(IReadOnlyList<ExaminerOption> options, string? rememberedId) =>
        options.FirstOrDefault(o => o.Available && o.Id == rememberedId) ?? options.First(o => o.Available);

    /// <summary>
    /// The examiner an exam or its organization fixes (GAD-T504): the exam's AvatarId if it names an available examiner, else the
    /// organization's DefaultAvatarId if it does, else null (the student chooses). Ids are compared without regard to case or spaces.
    /// </summary>
    public static ExaminerOption? Fixed(IReadOnlyList<ExaminerOption> options, string? examAvatarId, string? organizationAvatarId)
    {
        foreach (var id in new[] { examAvatarId, organizationAvatarId })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var o = options.FirstOrDefault(x => x.Available && string.Equals(x.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (o is not null) return o;
        }
        return null;
    }

    /// <summary>The examiner for an attempt: fixed by the exam or organization, else the student's remembered choice, else the first available.</summary>
    public static ExaminerOption Resolve(IReadOnlyList<ExaminerOption> options, string? examAvatarId, string? organizationAvatarId, string? rememberedId) =>
        Fixed(options, examAvatarId, organizationAvatarId) ?? Pick(options, rememberedId);

    /// <summary>
    /// The examiner for an attempt with the student's customization of it (GAD-T646): as <see cref="Resolve(IReadOnlyList{ExaminerOption}, string?, string?, string?)"/>,
    /// plus <paramref name="customization"/> (remembered in the browser, so untrusted) passed through
    /// <see cref="ExaminerCustomization.Validate"/> for the examiner it resolved to. Customization is off when the exam or organization
    /// fixes the examiner (GAD-T504): that examiner is shown and heard exactly as configured.
    /// </summary>
    public static ResolvedExaminer Resolve(IReadOnlyList<ExaminerOption> options, string? examAvatarId, string? organizationAvatarId,
        string? rememberedId, ExaminerCustomization? customization)
    {
        if (Fixed(options, examAvatarId, organizationAvatarId) is { } fixedExaminer) return new(fixedExaminer, ExaminerCustomization.None);
        var option = Pick(options, rememberedId);
        return new(option, customization?.Validate(option.Id) ?? ExaminerCustomization.None);
    }

    /// <summary>The exam's AvatarId and its organization's DefaultAvatarId for one assignment, tenant-scoped (both null when unknown).</summary>
    public static async Task<(string? Exam, string? Organization)> AvatarIdsForAssignmentAsync(
        IServiceScopeFactory scopes, Guid organizationId, Guid assignmentId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var exam = await (from a in db.ExamAssignments.AsNoTracking()
                          join e in db.Exams.AsNoTracking() on a.ExamId equals e.Id
                          where a.Id == assignmentId && a.OrganizationId == organizationId && e.OrganizationId == organizationId
                          select e.AvatarId).FirstOrDefaultAsync(ct);
        return (exam, await OrganizationAvatarIdAsync(db, organizationId, ct));
    }

    /// <summary>The exam's AvatarId and its organization's DefaultAvatarId for one session, tenant-scoped.</summary>
    public static async Task<(string? Exam, string? Organization)> AvatarIdsForSessionAsync(
        GadflyDbContext db, Guid organizationId, Guid sessionId, CancellationToken ct = default)
    {
        var exam = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId)
            .Join(db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId), s => s.ExamId, e => e.Id, (s, e) => e.AvatarId)
            .FirstOrDefaultAsync(ct);
        return (exam, await OrganizationAvatarIdAsync(db, organizationId, ct));
    }

    private static Task<string?> OrganizationAvatarIdAsync(GadflyDbContext db, Guid organizationId, CancellationToken ct) =>
        db.OrganizationSettings.AsNoTracking().Where(o => o.OrganizationId == organizationId).Select(o => o.DefaultAvatarId).FirstOrDefaultAsync(ct);
}
