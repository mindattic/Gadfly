using Gadfly.Server.Localization;
using Gadfly.Server.Student;
using Microsoft.JSInterop;

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
    /// Whether the examiner the student sees is the male one: an exam's examiner (its AvatarId, else its organization's
    /// DefaultAvatarId) when it names an available one, else the student's choice remembered on this device on the get-ready page
    /// (device-prefs.js getExaminer, GAD-T595), else the first available. False on any failure: the female examiner is the default.
    /// </summary>
    public static async Task<bool> MaleExaminerAsync(IJSRuntime js, IReadOnlyList<ExaminerOption> options, string? examAvatarId, string? organizationAvatarId,
        CancellationToken ct)
    {
        try
        {
            // The same resolution VoiceRoom uses to show the examiner (GAD-T504).
            if (ExaminerCatalog.Fixed(options, examAvatarId, organizationAvatarId) is { } fixedExaminer) return fixedExaminer.Id == ExaminerCatalog.Male;
            await using var prefs = await js.InvokeAsync<IJSObjectReference>("import", ct, "./js/device-prefs.js");
            var remembered = await prefs.InvokeAsync<RememberedExaminer?>("getExaminer", ct);
            return ExaminerCatalog.Resolve(options, examAvatarId, organizationAvatarId, remembered?.Id).Id == ExaminerCatalog.Male;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException
                                       or InvalidOperationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private sealed record RememberedExaminer(string? Id);
}
