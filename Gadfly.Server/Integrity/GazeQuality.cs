using System.Globalization;
using System.Text.Json;

namespace Gadfly.Server.Integrity;

/// <summary>
/// How far a session's eye-contact notes can be trusted (GAD-T305). The browser works it out on the device from what it measured
/// (the calibration's accuracy in degrees, how steadily the eyes were followed, how much of the answering time had a usable gaze
/// sample, lighting and framing, the frame rate) and sends it with every eye-contact episode as codes in the summary field:
/// <c>q:NN</c> (0..100), <c>m:cal</c> or <c>m:head</c> (set up to the student's screen, or head and eye direction only) and, when
/// calibrated, <c>a:N.N</c> (accuracy in degrees). The server never takes text from the browser: it parses the codes, bounds them,
/// writes the reviewer's sentence itself, and keeps the numbers in the event's DataJson. Webcam gaze is coarse (Papoutsaki et al.
/// 2016: about 4 degrees), so even "good" means good for a webcam; every note stays low-confidence context, never a verdict.
/// </summary>
public sealed record GazeQuality(int Score, bool Calibrated, double? AccuracyDeg)
{
    /// <summary>Scores from here up read "good", from <see cref="FairFrom"/> "fair", below it "poor".</summary>
    public const int GoodFrom = 70;
    public const int FairFrom = 40;

    /// <summary>The largest accuracy figure kept: anything worse is not a calibration anyone used.</summary>
    public const double MaxAccuracyDeg = 45;

    public string Band => Score >= GoodFrom ? "good" : Score >= FairFrom ? "fair" : "poor";

    public bool Poor => Score < FairFrom;

    /// <summary>The codes in a browser summary (separated by ';'), trimmed and lower-cased.</summary>
    public static IReadOnlyList<string> Codes(string? summary) =>
        (summary ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.ToLowerInvariant()).ToList();

    /// <summary>Whether a browser summary carries <paramref name="code"/> (for example "remarked").</summary>
    public static bool Has(string? summary, string code) => Codes(summary).Contains(code, StringComparer.Ordinal);

    /// <summary>The value of a <c>name:value</c> code, or null.</summary>
    public static string? Value(string? summary, string name) =>
        Codes(summary).FirstOrDefault(c => c.StartsWith(name + ":", StringComparison.Ordinal)) is { } c ? c[(name.Length + 1)..] : null;

    /// <summary>The quality an episode was sent with, or null when it carries none (an older page) or the codes don't parse.</summary>
    public static GazeQuality? Parse(string? summary)
    {
        if (!int.TryParse(Value(summary, "q"), NumberStyles.None, CultureInfo.InvariantCulture, out var score)) return null;
        var calibrated = Value(summary, "m") == "cal";
        double? accuracy = calibrated && double.TryParse(Value(summary, "a"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var a)
            && double.IsFinite(a) ? Math.Round(Math.Clamp(a, 0, MaxAccuracyDeg), 1) : null;
        return new GazeQuality(Math.Clamp(score, 0, 100), calibrated, accuracy);
    }

    /// <summary>The sentence a reviewer reads beside the episode.</summary>
    public string Sentence()
    {
        var how = Calibrated
            ? AccuracyDeg is { } a ? $"set up to the student's screen, about {a.ToString("0.#", CultureInfo.InvariantCulture)}° accuracy" : "set up to the student's screen"
            : "head and eye direction only, not set up to the screen";
        var weigh = Poor ? " Treat this note as very weak." : "";
        return $" Eye-tracking quality this session: {Band} ({Score}/100; {how}).{weigh}";
    }

    /// <summary>What is kept in the event's DataJson.</summary>
    public string Json() => JsonSerializer.Serialize(new { GazeQuality = new { Score, Band, Mode = Calibrated ? "calibrated" : "head", AccuracyDeg } });

    /// <summary>The quality kept in an event's DataJson, or null.</summary>
    public static GazeQuality? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("GazeQuality", out var q) || q.ValueKind != JsonValueKind.Object) return null;
            if (!q.TryGetProperty("Score", out var s) || !s.TryGetInt32(out var score)) return null;
            var calibrated = q.TryGetProperty("Mode", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() == "calibrated";
            double? accuracy = q.TryGetProperty("AccuracyDeg", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : null;
            return new GazeQuality(Math.Clamp(score, 0, 100), calibrated, accuracy);
        }
        catch (JsonException) { return null; }
    }
}
