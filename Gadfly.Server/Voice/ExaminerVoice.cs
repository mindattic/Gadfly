using System.Text.RegularExpressions;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// Which ElevenLabs voice the examiner speaks with for one session (GAD-T475): the exam's own voice id, else the
/// organization's default voice id (Organization settings), else the deployment's <c>MindAttic:Avatar:ElevenLabs:VoiceId</c>
/// (returned as null, so the library uses its configured voice).
/// <para>
/// Decided on the server when the exam room arms the student and kept on their <see cref="VoiceGrants"/> grant;
/// <see cref="VoiceHubFilter"/> writes it into every <c>Speak</c> call, overwriting whatever the browser sent, so the browser
/// can never pick the voice. Only in <see cref="VoiceMode.Live"/>: in mock mode the fake voice is used as before and no id is
/// passed on. An id that isn't shaped like an ElevenLabs voice id (letters and digits, 10 to 64 of them) is ignored with a
/// warning rather than sent, so a typo falls back to the platform voice instead of silencing the examiner.
/// </para>
/// </summary>
public static partial class ExaminerVoice
{
    [GeneratedRegex("^[A-Za-z0-9]{10,64}$")]
    private static partial Regex Shape();

    /// <summary>True when <paramref name="voiceId"/> looks like an ElevenLabs voice id.</summary>
    public static bool IsWellFormed(string? voiceId) => voiceId is not null && Shape().IsMatch(voiceId.Trim());

    /// <summary>The exam's voice, else the organization's, else null (the platform voice). Always null outside live mode.</summary>
    public static string? Choose(VoiceMode mode, string? examVoiceId, string? organizationVoiceId)
    {
        if (mode != VoiceMode.Live) return null;
        foreach (var id in new[] { examVoiceId, organizationVoiceId })
            if (!string.IsNullOrWhiteSpace(id) && IsWellFormed(id)) return id.Trim();
        return null;
    }

    /// <summary>
    /// The exam's voice, else the organization's, else the platform voice configured for the exam language (GAD-T530,
    /// <see cref="VoiceSettings.VoiceIdFor"/>), else null (the platform voice). Always null outside live mode.
    /// </summary>
    public static string? Choose(VoiceMode mode, string? examVoiceId, string? organizationVoiceId, string? languageVoiceId) =>
        mode != VoiceMode.Live ? null : Choose(mode, examVoiceId, organizationVoiceId) ?? (IsWellFormed(languageVoiceId) ? languageVoiceId!.Trim() : null);

    /// <summary>
    /// With the attempt's examiner (GAD-T504): the exam's voice, else the examiner's own voice for the exam language
    /// (<see cref="Gadfly.Server.Student.ExaminerOption.VoiceIdFor"/>; the male examiner has one, so an organization's default voice,
    /// chosen for her, never speaks for him), else the organization's, else the platform voice for the language, else null.
    /// Always null outside live mode.
    /// </summary>
    public static string? Choose(VoiceMode mode, string? examVoiceId, string? examinerVoiceId, string? organizationVoiceId, string? languageVoiceId) =>
        mode != VoiceMode.Live ? null
        : Choose(mode, examVoiceId, null) ?? Choose(mode, examinerVoiceId, null) ?? Choose(mode, organizationVoiceId, languageVoiceId);

    /// <summary>Reads the session's exam voice and its organization's default, tenant-scoped to <paramref name="org"/>; the examiner's
    /// own voice (<paramref name="examinerVoiceId"/>, GAD-T504) comes between them; with none, <paramref name="languageVoiceId"/> (the
    /// platform voice for the attempt's language, GAD-T530).</summary>
    public static async Task<string?> ForSessionAsync(GadflyDbContext db, VoiceMode mode, Guid org, Guid session, ILogger log, CancellationToken ct,
        string? languageVoiceId = null, string? examinerVoiceId = null)
    {
        if (mode != VoiceMode.Live) return null;
        var examVoice = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == session && s.OrganizationId == org)
            .Join(db.Exams.AsNoTracking().Where(e => e.OrganizationId == org), s => s.ExamId, e => e.Id, (s, e) => e.VoiceId)
            .FirstOrDefaultAsync(ct);
        var orgVoice = await db.OrganizationSettings.AsNoTracking()
            .Where(o => o.OrganizationId == org).Select(o => o.DefaultVoiceId).FirstOrDefaultAsync(ct);
        foreach (var (where, id) in new[] { ("exam", examVoice), ("organization default", orgVoice) })
            if (!string.IsNullOrWhiteSpace(id) && !IsWellFormed(id))
                log.LogWarning("The {Where} examiner voice id for session {SessionId} is not a valid voice id; it is skipped.", where, session);
        return Choose(mode, examVoice, examinerVoiceId, orgVoice, languageVoiceId);
    }
}
