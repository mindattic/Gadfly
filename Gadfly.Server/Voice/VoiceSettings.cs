namespace Gadfly.Server.Voice;

/// <summary>How the voice exam room runs. Nothing but <see cref="Live"/> can reach a paid provider.</summary>
public enum VoiceMode
{
    /// <summary>Voice is not offered (the default outside Development): exams are answered by typing.</summary>
    Off,
    /// <summary>
    /// Simulated speech recognition (placeholder words timed to the microphone) and an in-process fake text-to-speech with
    /// real word timings. No key, no network, no cost; for development, demos and verification. Transcripts are placeholders.
    /// </summary>
    Mock,
    /// <summary>ElevenLabs Scribe / OpenAI for recognition and ElevenLabs for the examiner's voice. Billed. Keys stay on the server (Vault).</summary>
    Live,
}

/// <summary>
/// Voice exam room settings (GAD-T53), from <c>Gadfly:Voice</c>:
/// <c>Mode</c> (<c>off</c> | <c>mock</c> | <c>live</c>; default <c>mock</c> in Development, otherwise <c>off</c>),
/// <c>AvatarModelUrl</c> (the GLB; default <c>/assets/avatars/mpfb.glb</c> when that file is present, else the 2D portrait),
/// <c>PortraitUrl</c>, <c>EndOfAnswerSeconds</c> (silence that ends an answer, default 4).
/// Provider choice and keys are the libraries' own settings: <c>MindAttic:SpeechRecognition:Provider</c> and
/// <c>MindAttic:Avatar:ElevenLabs:VoiceId</c>; the ElevenLabs key is read from Vault on the server only.
/// </summary>
public sealed record VoiceSettings(VoiceMode Mode, string? AvatarModelUrl, string PortraitUrl, double EndOfAnswerSeconds)
{
    public bool Available => Mode != VoiceMode.Off;

    public static readonly VoiceSettings Disabled = new(VoiceMode.Off, null, "/assets/portrait.png", 4);

    /// <summary>Reads the settings. <paramref name="webRoot"/> is checked for the default avatar model.</summary>
    public static VoiceSettings From(IConfiguration config, bool development, string? webRoot)
    {
        var section = config.GetSection("Gadfly:Voice");
        var mode = (section["Mode"] ?? (development ? "mock" : "off")).Trim().ToLowerInvariant() switch
        {
            "mock" => VoiceMode.Mock,
            "live" => VoiceMode.Live,
            _ => VoiceMode.Off,
        };
        var model = section["AvatarModelUrl"];
        if (string.IsNullOrWhiteSpace(model) && webRoot is not null
            && File.Exists(Path.Combine(webRoot, "assets", "avatars", "mpfb.glb")))
            model = "/assets/avatars/mpfb.glb";
        var cue = double.TryParse(section["EndOfAnswerSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var c) ? Math.Clamp(c, 2, 15) : 4;
        return new VoiceSettings(mode, string.IsNullOrWhiteSpace(model) ? null : model, section["PortraitUrl"] ?? "/assets/portrait.png", cue);
    }
}
