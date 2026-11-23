using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>One examiner a student can choose on the get-ready page (GAD-T595), and an exam or organization can fix (GAD-T504).</summary>
/// <param name="Id">Stable id, also what an exam's AvatarId and an organization's DefaultAvatarId name.</param>
/// <param name="NameKey">UI string key of the examiner's description ("getReady.examiner.choiceFemale").</param>
/// <param name="ModelUrl">The 3D model, or null for the portrait only.</param>
/// <param name="PortraitUrl">The 2D portrait (always shown first, and on devices without 3D).</param>
/// <param name="Available">False while the examiner's model or portrait isn't installed: listed as coming soon, not choosable.</param>
public sealed record ExaminerOption(string Id, string NameKey, string? ModelUrl, string PortraitUrl, bool Available)
{
    /// <summary>Where the portrait's mouth is ("x,y,w" as fractions of the image), so it can move while the examiner speaks; null: none drawn.</summary>
    public string? PortraitMouth { get; init; }

    /// <summary>The examiner's own ElevenLabs voice for every language (null: the usual platform voice).</summary>
    public string? VoiceId { get; init; }

    /// <summary>The examiner's own voice for one exam language (normalized tag), where it differs from <see cref="VoiceId"/>.</summary>
    public IReadOnlyDictionary<string, string> VoiceIds { get; init; } = new Dictionary<string, string>();

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
/// tools/examiner-m), each with its portrait and its own voice. A list rather than one hard-coded examiner, so adding one is a row
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

    /// <summary>The male examiner's files (relative to wwwroot), committed with GAD-T504.</summary>
    public const string MaleModelPath = "assets/avatars/examiner-m.glb";
    public const string MalePortraitPath = "assets/portrait-m.png";

    /// <summary>The male portrait's mouth (the bundled <c>/assets/portrait-m.png</c>, 480 x 600).</summary>
    public const string MalePortraitMouth = "0.51,0.725,0.19";

    /// <summary>
    /// The male examiner's default ElevenLabs voice: "Brian", a premade voice described as deep, resonant and comforting (calm and
    /// warm, an American man). Premade voices speak every language of the multilingual models, Korean included with
    /// <c>eleven_flash_v2_5</c> (the default) and <c>eleven_multilingual_v2</c>. Change it with
    /// <c>Gadfly:Voice:Examiners:examiner-m:VoiceId</c> (or <c>:VoiceIds:ko</c> for one language). Used in live mode only.
    /// </summary>
    public const string MaleDefaultVoiceId = "nPczCjzI2devNBz1zQrb";

    /// <param name="voice">The site's voice settings: the female examiner is the configured one; <see cref="VoiceSettings.Examiners"/> overrides.</param>
    /// <param name="webRoot">wwwroot on disk, to see whether an examiner's files are installed (null: only configured URLs count).</param>
    public static IReadOnlyList<ExaminerOption> For(VoiceSettings voice, string? webRoot)
    {
        var f = voice.Examiners.GetValueOrDefault(Female);
        var m = voice.Examiners.GetValueOrDefault(Male);
        string? Local(string path) => webRoot is not null && File.Exists(Path.Combine(webRoot, path)) ? "/" + path : null;
        var malePortrait = m?.PortraitUrl ?? Local(MalePortraitPath);
        return
        [
            new(Female, "getReady.examiner.choiceFemale", f?.ModelUrl ?? voice.AvatarModelUrl, f?.PortraitUrl ?? voice.PortraitUrl, true)
            {
                PortraitMouth = f?.PortraitMouth ?? voice.PortraitMouth,
                VoiceId = f?.VoiceId,
                VoiceIds = f?.VoiceIds ?? new Dictionary<string, string>(),
            },
            new(Male, "getReady.examiner.choiceMale", m?.ModelUrl ?? Local(MaleModelPath), malePortrait ?? "/" + MalePortraitPath, malePortrait is not null)
            {
                PortraitMouth = m?.PortraitMouth ?? (m?.PortraitUrl is null ? MalePortraitMouth : null),
                VoiceId = m?.VoiceId ?? MaleDefaultVoiceId,
                VoiceIds = m?.VoiceIds ?? new Dictionary<string, string>(),
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
