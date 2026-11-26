using Gadfly.Server.Voice;

namespace Gadfly.Server.Student;

/// <summary>
/// How a student has customized their examiner (GAD-T646): a handful of bounded choices, never free text. Each value is one of a
/// fixed list, so whatever the browser sends, the examiner stays a recognizably human face of the kind the gaze check
/// (ExaminerGaze) was built around, and speaks with a voice Gadfly has vetted.
/// <list type="bullet">
/// <item><see cref="Voice"/>: an ElevenLabs premade voice id from <see cref="AllowedVoices"/> (per examiner, and per exam language
/// where a language has its own list). Premade voices speak every language of the multilingual models, Korean included.</item>
/// <item><see cref="SkinTone"/>, <see cref="Hair"/>, <see cref="Attire"/>: from <see cref="SkinTones"/>, <see cref="HairColours"/>
/// and <see cref="Attires"/>.</item>
/// <item><see cref="Look"/>: the framing preset, from <see cref="Looks"/>; every one keeps the face and eyes in view.</item>
/// </list>
/// A null value is the examiner's own default. Customization only applies when the student chooses the examiner: an exam or
/// organization that fixes one (GAD-T504) gets it exactly as configured (<c>ExaminerCatalog.Resolve</c>).
/// The browser remembers the choice (device-prefs.js <c>getExaminer</c>), so the server always passes it through
/// <see cref="Validate"/> again, and the chosen voice once more for the exam language (<see cref="VoiceFor"/>) before it is spoken with.
/// </summary>
public sealed record ExaminerCustomization(string? Voice = null, string? SkinTone = null, string? Hair = null, string? Attire = null, string? Look = null)
{
    /// <summary>No customization: the examiner exactly as the catalog describes it.</summary>
    public static readonly ExaminerCustomization None = new();

    /// <summary>True when nothing is customized.</summary>
    public bool IsEmpty => this == None;

    /// <summary>Skin tones the examiner can be shown with, lightest to darkest.</summary>
    public static readonly IReadOnlyList<string> SkinTones = ["light", "medium-light", "medium", "medium-dark", "dark"];

    /// <summary>Hair colours.</summary>
    public static readonly IReadOnlyList<string> HairColours = ["black", "dark-brown", "brown", "auburn", "blonde", "grey"];

    /// <summary>What the examiner wears: all plain, professional and without logos or text.</summary>
    public static readonly IReadOnlyList<string> Attires = ["navy-blazer", "charcoal-suit", "cardigan", "white-shirt"];

    /// <summary>Framing presets: each keeps the examiner's face and eyes in view, so the gaze check still has a face to hold.</summary>
    public static readonly IReadOnlyList<string> Looks = ["head-and-shoulders", "close", "seated"];

    /// <summary>ElevenLabs premade voices for the female examiner (Rachel, Sarah, Charlotte, Alice, Matilda, Lily).</summary>
    public static readonly IReadOnlyList<string> FemaleVoices =
        ["21m00Tcm4TlvDq8ikWAM", "EXAVITQu4vr4xnSDxMaL", "XB0fDUnXU5powFXDhCwa", "Xb7hH8MSUJpSbSDYk0k2", "XrExE9yKIg1WjnnlVkGX", "pFZP5JQG7iQjIQuC4Bku"];

    /// <summary>ElevenLabs premade voices for the male examiner (Brian, his default, then Daniel, George, Chris, Eric, Liam).</summary>
    public static readonly IReadOnlyList<string> MaleVoices =
        [ExaminerCatalog.MaleDefaultVoiceId, "onwK4e9ZLuTAKqWW03F9", "JBFqnCBsd6RMkjVDRZzb", "iP95p4xoKVk53GoZ742B", "cjVigY5qzO86Huf0OWal", "TX3LPaxmHKxFdv7VOQHJ"];

    /// <summary>ElevenLabs premade voices for Socrates (GAD-T726): Bill, his default, then the older and deeper of the male voices
    /// (George, Daniel, Brian).</summary>
    public static readonly IReadOnlyList<string> SocratesVoices =
        [ExaminerCatalog.SocratesDefaultVoiceId, "JBFqnCBsd6RMkjVDRZzb", "onwK4e9ZLuTAKqWW03F9", ExaminerCatalog.MaleDefaultVoiceId];

    /// <summary>
    /// Per examiner, a language's own voice list where it differs from the examiner's list for every language (none yet: the
    /// premade voices all speak Korean and Spanish with the multilingual models). Keyed by examiner id, then normalized language tag.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> LanguageVoices =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The voices <paramref name="examinerId"/> may be given for <paramref name="language"/> (null: any language the examiner
    /// speaks, the union of its lists). An unknown examiner has none.
    /// </summary>
    public static IReadOnlyList<string> AllowedVoices(string examinerId, string? language = null)
    {
        IReadOnlyList<string> all = examinerId switch
        {
            ExaminerCatalog.Female => FemaleVoices,
            ExaminerCatalog.Male => MaleVoices,
            ExaminerCatalog.Socrates or ExaminerCatalog.Bust or ExaminerCatalog.BustBody => SocratesVoices,
            _ => [],
        };
        if (!LanguageVoices.TryGetValue(examinerId, out var byLanguage)) return all;
        if (language is null) return [.. all.Concat(byLanguage.Values.SelectMany(v => v)).Distinct()];
        return byLanguage.TryGetValue(Gadfly.Engine.ExamLanguage.Normalize(language), out var own) ? own : all;
    }

    /// <summary>
    /// This customization with every value Gadfly doesn't offer dropped (back to the examiner's default): a voice not on
    /// <paramref name="examinerId"/>'s list (for <paramref name="language"/>, when given), or a skin tone, hair colour, attire or look
    /// not on its palette. Values are compared without regard to case or surrounding spaces and returned in their canonical form.
    /// </summary>
    public ExaminerCustomization Validate(string examinerId, string? language = null) => new(
        Voice: VoiceFor(examinerId, language),
        SkinTone: Known(SkinTone, SkinTones),
        Hair: Known(Hair, HairColours),
        Attire: Known(Attire, Attires),
        Look: Known(Look, Looks));

    /// <summary>The chosen voice when it is well formed and on <paramref name="examinerId"/>'s list for <paramref name="language"/>, else null.</summary>
    public string? VoiceFor(string examinerId, string? language) =>
        ExaminerVoice.IsWellFormed(Voice) ? Known(Voice, AllowedVoices(examinerId, language), StringComparison.Ordinal) : null;

    private static string? Known(string? value, IReadOnlyList<string> allowed, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return allowed.FirstOrDefault(a => string.Equals(a, v, comparison));
    }
}

/// <summary>
/// The examiner for one attempt (GAD-T646): the catalog <see cref="Option"/> and the student's <see cref="Customization"/> of it,
/// already validated (<see cref="ExaminerCustomization.None"/> when the exam or organization fixes the examiner).
/// </summary>
public sealed record ResolvedExaminer(ExaminerOption Option, ExaminerCustomization Customization)
{
    public string Id => Option.Id;

    /// <summary>The examiner's voice for <paramref name="language"/>: the student's chosen voice when it is still allowed for that
    /// language (checked again here), else the examiner's own (<see cref="ExaminerOption.VoiceIdFor"/>), else null (the platform's).</summary>
    public string? VoiceIdFor(string? language) => Customization.VoiceFor(Option.Id, language) ?? Option.VoiceIdFor(language);
}
