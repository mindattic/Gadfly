using Gadfly.Server.Localization;
using Gadfly.Server.Student;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The exam room's text (GAD-T606): the strings of Localization/Strings/examRoom.&lt;lang&gt;.json in the attempt's language
/// (<c>ExamSession.Language</c>, never the site's language switch), with the examiner's pronouns following the examiner on screen.
/// The exam room cascades one to everything it hosts (VoiceRoom, StaffNoticeBanner, WebcamIntegrity, ExaminerGaze); a component
/// outside an exam (the practice) has none and uses the page's own language.
/// <para>
/// Pronouns: a sentence that names the examiner with a pronoun ("the screen around her") has a male variant under the same key plus
/// <c>.male</c>; <see cref="this[string]"/> picks it when the examiner is the male one. Korean names the examiner 시험관 instead of
/// using a pronoun, so its two variants read the same.
/// </para>
/// </summary>
public sealed class ExamRoomText(UiText text, bool maleExaminer = false)
{
    /// <summary>The suffix of a sentence's male-examiner variant.</summary>
    public const string MaleSuffix = ".male";

    /// <summary>The underlying text, in the attempt's language.</summary>
    public UiText Text { get; } = text;

    /// <summary>The attempt's language ("en", "ko").</summary>
    public string Language => Text.Language;

    /// <summary>The examiner on screen is the male one (GAD-T504): pronoun-bearing sentences use their male variant.</summary>
    public bool MaleExaminer { get; } = maleExaminer;

    public string this[string key] => Pick(key, k => Text[k]);

    public string this[string key, params object[] args] => Pick(key, k => Text[k, args]);

    private string Pick(string key, Func<string, Microsoft.Extensions.Localization.LocalizedString> find)
    {
        if (MaleExaminer && find(key + MaleSuffix) is { ResourceNotFound: false } male) return male.Value;
        return find(key).Value;
    }

    /// <summary>
    /// Whether the examiner the student sees is a man (GAD-T726, <see cref="ExaminerOption.IsMale"/>): an exam's examiner (its AvatarId, else its organization's
    /// DefaultAvatarId) when it names an available one, else the first available (GAD-T839: the student no longer chooses one).
    /// </summary>
    public static bool IsMaleExaminer(IReadOnlyList<ExaminerOption> options, string? examAvatarId, string? organizationAvatarId) =>
        // The same resolution VoiceRoom uses for the examiner's voice (GAD-T504).
        ExaminerCatalog.Resolve(options, examAvatarId, organizationAvatarId, null).IsMale;
}
