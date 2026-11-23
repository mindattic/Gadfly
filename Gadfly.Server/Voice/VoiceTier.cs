namespace Gadfly.Server.Voice;

/// <summary>
/// The examiner's voice quality/speed tier (GAD-T415, GAD-T616): which ElevenLabs text-to-speech model she speaks with, picked by an
/// organization (<c>OrganizationSettings.VoiceTier</c>) or one exam (<c>Exam.VoiceTier</c>). Stored by <see cref="Id"/>; null on both
/// means the deployment's own choice (<c>Gadfly:Voice:TtsModels:&lt;language&gt;</c>, else <c>Gadfly:Voice:TtsModel</c>), so an English
/// exam by an English speaker with nothing set sounds exactly as before.
/// <para>
/// Only models the examiner's path can use are offered: it streams over HTTP <c>/v1/text-to-speech/{voice}/stream/with-timestamps</c>
/// (word timings drive the lip-sync) and sets <c>voice_settings.speed</c> for second-language speakers (GAD-T555). Checked against
/// ElevenLabs' model docs on 2026-11-22: <c>eleven_turbo_v2_5</c> is deprecated as "functionally equivalent" to
/// <c>eleven_flash_v2_5</c>, so a "balanced" tier on it would be the fast tier under another name; <c>eleven_v4</c>/<c>eleven_v4_turbo</c>
/// are served only by the Text to Dialogue API and WebSocket and have no speed setting; <c>eleven_v3</c> is not on the stream-input
/// WebSocket and the docs do not confirm it on the with-timestamps stream. Those wait for the avatar library to speak through the
/// dialogue endpoint (an "expressive" tier then).
/// </para>
/// </summary>
/// <param name="Id">Stable id, what the settings store (max 40 characters).</param>
/// <param name="ModelId">The ElevenLabs model id.</param>
/// <param name="Label">The name an admin picks.</param>
/// <param name="Hint">The latency/quality tradeoff in one sentence (docs/VOICE.md, "Voice quality vs. latency").</param>
public sealed record VoiceTier(string Id, string ModelId, string Label, string Hint)
{
    public const int MaxIdLength = 40;

    public const string FastId = "fast";
    public const string QualityId = "quality";

    /// <summary>The low-latency model, and the deployment default (<see cref="VoiceSettings.TtsModel"/>).</summary>
    public static readonly VoiceTier Fast = new(FastId, "eleven_flash_v2_5", "Fast (default)",
        "First audio in about a quarter of a second, so the exam keeps a live conversational rhythm; a little flatter on long or expressive sentences.");

    /// <summary>ElevenLabs' most natural multilingual v2 model, slower to start.</summary>
    public static readonly VoiceTier Quality = new(QualityId, "eleven_multilingual_v2", "Quality",
        "The most natural-sounding voice, but slower to start: often a second or more of extra pause before each question.");

    /// <summary>Every tier, in the order an admin sees them.</summary>
    public static readonly IReadOnlyList<VoiceTier> All = [Fast, Quality];

    /// <summary>The tier stored as <paramref name="id"/> (any case, trimmed), or null for none or an unknown one.</summary>
    public static VoiceTier? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(t => string.Equals(t.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The id to store for <paramref name="id"/>: a known tier's own id, else null (the deployment's choice).</summary>
    public static string? Normalize(string? id) => Find(id)?.Id;

    /// <summary>The tier that applies to an attempt: the exam's, else the organization's (unknown ids are skipped), else null.</summary>
    public static VoiceTier? Resolve(string? examTier, string? organizationTier) => Find(examTier) ?? Find(organizationTier);

    /// <summary>The tier whose model is <paramref name="modelId"/>, or null (a model set only in configuration).</summary>
    public static VoiceTier? ForModel(string? modelId) => All.FirstOrDefault(t => t.ModelId == modelId);

    // ElevenLabs' language lists (docs "Models", 2026-11-22), as ExamLanguage tags. eleven_multilingual_v2 has 29; the v2.5 models add
    // Hungarian, Norwegian and Vietnamese; the v2 English models speak only English.
    private static readonly string[] Multilingual29 =
        ["en", "ja", "zh", "de", "hi", "fr", "ko", "pt", "it", "es", "id", "nl", "tr", "fil", "pl", "sv", "bg", "ro", "ar", "cs", "el", "fi",
         "hr", "ms", "sk", "da", "ta", "uk", "ru"];

    private static readonly Dictionary<string, HashSet<string>> Languages = new(StringComparer.Ordinal)
    {
        ["eleven_multilingual_v2"] = [.. Multilingual29],
        ["eleven_flash_v2_5"] = [.. Multilingual29, "hu", "no", "vi"],
        ["eleven_turbo_v2_5"] = [.. Multilingual29, "hu", "no", "vi"],
        ["eleven_flash_v2"] = ["en"],
        ["eleven_turbo_v2"] = ["en"],
    };

    /// <summary>
    /// Whether <paramref name="modelId"/> speaks <paramref name="language"/> (an exam language tag, any form ExamLanguage normalizes).
    /// A model not in the table (one a deployment configured) is trusted: only the catalog's known models are second-guessed.
    /// </summary>
    public static bool Speaks(string modelId, string? language)
    {
        if (!Languages.TryGetValue(modelId, out var spoken)) return true;
        var tag = Gadfly.Engine.ExamLanguage.Normalize(language);
        var primary = tag.Split('-')[0];
        return spoken.Contains(tag) || spoken.Contains(primary);
    }
}
