using Gadfly.Engine;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// How one exam attempt is spoken and heard (GAD-T528, GAD-T554, GAD-T555): the attempt's exam language (<c>ExamSession.Language</c>)
/// and the student's primary language at the start (<c>ExamSession.PrimaryLanguage</c>, GAD-T534). Decided on the server when the exam
/// room arms the student, kept on their <see cref="VoiceGrants"/> grant, never taken from the browser.
/// <para>
/// <see cref="IsDefault"/> (an English exam by a student whose primary language is English or unknown) is exactly the behavior from
/// before localization, by the owner's rule: the recognizer language is the exam policy's as before, the examiner speaks through the
/// avatar library's own voice path, the fillers are the English lines and the endpointing is unchanged.
/// </para>
/// </summary>
public sealed record SessionSpeech(string ExamLanguage, string? PrimaryLanguage, VoiceCalibration? Calibration = null)
{
    /// <summary>English exam, English (or unknown) primary language: today's behavior, unchanged.</summary>
    public static readonly SessionSpeech Default = new(Engine.ExamLanguage.English, null);

    /// <summary>The exam language, normalized ("en", "ko").</summary>
    public string Language => Engine.ExamLanguage.Normalize(ExamLanguage);

    /// <summary>
    /// The student speaks the exam language as a second language (GAD-T554, GAD-T555): their primary language is known and is another
    /// one. A student with no language profile is treated as a native speaker of the exam language.
    /// </summary>
    public bool NonNative => !string.IsNullOrWhiteSpace(PrimaryLanguage) && Engine.ExamLanguage.Normalize(PrimaryLanguage) != Language;

    /// <summary>The primary language for grouping (monitoring, GAD-T554): the profile's, else the exam language.</summary>
    public string Primary => string.IsNullOrWhiteSpace(PrimaryLanguage) ? Language : Engine.ExamLanguage.Normalize(PrimaryLanguage);

    /// <summary>True for the unchanged, pre-localization behavior (see the type's remarks).</summary>
    public bool IsDefault => Engine.ExamLanguage.IsEnglish(Language) && !NonNative;

    /// <summary>
    /// The language the recognizer is told to expect (GAD-T529, GAD-T554). A non-English exam: the exam language (Scribe
    /// <c>language_code</c>, Whisper <c>language</c>, e.g. "ko"). An English exam by a non-native speaker: "en" always, so a Korean
    /// accent, or a Korean word or two, never makes the recognizer switch to transcribing Korean (auto-detection is what mishears an
    /// accent as another language). Otherwise the exam policy's own spoken language (null = detect), as before.
    /// </summary>
    public string? RecognizerLanguage(string? policyLanguage) =>
        !Engine.ExamLanguage.IsEnglish(Language) ? Language
        : NonNative ? Engine.ExamLanguage.English
        : policyLanguage;

    /// <summary>
    /// The examiner's voice speed (GAD-T555): <see cref="VoiceSettings.PlainSpeechSpeed"/> (slightly slower than normal) for a non-native
    /// speaker, else null (the voice's own speed, as before).
    /// </summary>
    public double? Speed(VoiceSettings settings) => NonNative && Math.Abs(settings.PlainSpeechSpeed - 1) > 0.001 ? settings.PlainSpeechSpeed : null;

    /// <summary>
    /// The attempt's languages, tenant-scoped; <see cref="Default"/> when the session can't be found. Outside the default case the
    /// student's own voice calibration in the exam language (GAD-T594, <see cref="StudentLanguageProfile.ReadVoiceCalibration"/>) is
    /// read too, to start the end-of-answer silence from their own pauses (<see cref="VoiceSettings.EndCuesFor"/>).
    /// </summary>
    public static async Task<SessionSpeech> ForSessionAsync(GadflyDbContext db, Guid org, Guid session, CancellationToken ct)
    {
        var row = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == session && s.OrganizationId == org)
            .Select(s => new { s.Language, s.PrimaryLanguage, s.StudentUserId }).FirstOrDefaultAsync(ct);
        if (row is null) return Default;
        var speech = new SessionSpeech(Engine.ExamLanguage.Normalize(row.Language),
            string.IsNullOrWhiteSpace(row.PrimaryLanguage) ? null : Engine.ExamLanguage.Normalize(row.PrimaryLanguage));
        if (speech.IsDefault) return speech;
        var profile = await db.StudentLanguageProfiles.AsNoTracking()
            .Where(p => p.OrganizationId == org && p.UserId == row.StudentUserId).FirstOrDefaultAsync(ct);
        return profile?.ReadVoiceCalibration().TryGetValue(speech.Language, out var calibration) == true && calibration.Readings > 0
            ? speech with { Calibration = calibration } : speech;
    }
}
