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
/// <c>PortraitUrl</c>, <c>EndOfAnswerSeconds</c> (the shortest silence that ends an answer, default 4),
/// <c>EndOfAnswerMaxSeconds</c> (the longest it may grow to as it adapts to the student's own pauses, default 12; GAD-T264).
/// Provider choice and keys are the libraries' own settings: <c>MindAttic:SpeechRecognition:Provider</c> and
/// <c>MindAttic:Avatar:ElevenLabs:VoiceId</c>; the ElevenLabs key is read from Vault on the server only.
/// </summary>
public sealed record VoiceSettings(VoiceMode Mode, string? AvatarModelUrl, string PortraitUrl, double EndOfAnswerSeconds)
{
    public bool Available => Mode != VoiceMode.Off;

    /// <summary>
    /// The ceiling the end-of-answer silence may adapt up to (GAD-T264). The browser lengthens the cue toward this when the
    /// student's own mid-answer pauses are long, so a slow or disfluent speaker is not cut off; it never goes below
    /// <see cref="EndOfAnswerSeconds"/>.
    /// </summary>
    public double EndOfAnswerMaxSeconds { get; init; } = 12;

    /// <summary>The hard upper bound on the cue after an accommodation is applied: past this the student should press Done answering.</summary>
    public const double EndOfAnswerHardCapSeconds = 45;

    /// <summary>
    /// The end-of-answer cue's floor and ceiling for one student (GAD-T264), in milliseconds: both are lengthened by the
    /// extra-time accommodation (<paramref name="timeMultiplier"/>, 1 to 4), so a student with extra time also gets more
    /// silence before an answer is taken as finished.
    /// </summary>
    public (int FloorMs, int CeilingMs) EndCue(double timeMultiplier)
    {
        var m = double.IsFinite(timeMultiplier) ? Math.Clamp(timeMultiplier, 1, 4) : 1;
        var floor = Math.Min(EndOfAnswerSeconds * m, EndOfAnswerHardCapSeconds);
        var ceiling = Math.Clamp(EndOfAnswerMaxSeconds * m, floor, EndOfAnswerHardCapSeconds);
        return ((int)Math.Round(floor * 1000), (int)Math.Round(ceiling * 1000));
    }

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
        var cue = double.TryParse(section["EndOfAnswerSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var c) && double.IsFinite(c) ? Math.Clamp(c, 2, 15) : 4;
        var max = double.TryParse(section["EndOfAnswerMaxSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? Math.Clamp(x, cue, 30) : Math.Max(12, cue);
        return new VoiceSettings(mode, string.IsNullOrWhiteSpace(model) ? null : model, section["PortraitUrl"] ?? "/assets/portrait.png", cue)
        {
            EndOfAnswerMaxSeconds = max,
        };
    }
}
