using System.Text;

namespace Gadfly.Server.Diagnostics;

/// <summary>
/// Recognizer latency and unsure-word rate (GAD-T361), in memory only, the same spirit as <see cref="ApiCallTimings"/>
/// (GAD-T413) but without a durable table: these are diagnostics for the status page, not an audit trail, so they are
/// never written to the database and never survive a restart. "Latency" here is the time from the student's microphone
/// going quiet (a <c>Commit</c> call) to the recognizer's final transcript for that utterance coming back — the thing a
/// student actually waits on — timed in the browser (<c>voice-room.js</c>) since the hub's own push of a final segment is
/// not a request the server can time itself. "Unsure-word rate" is the share of an answer's words below the library's
/// low-confidence threshold, computed from the same <see cref="Voice.VoiceAnswerBuilder"/> every voice answer already goes
/// through, so it costs nothing extra to compute.
/// </summary>
public sealed class VoiceRecognitionTimingRecorder
{
    /// <summary>Samples kept per kind; old ones fall off the front. In memory, bounded, nothing durable.</summary>
    public const int MaxSamples = 2000;

    private readonly object _gate = new();
    private readonly Queue<(DateTimeOffset At, double Ms)> _latencies = new();
    private readonly Queue<(DateTimeOffset At, double Share, int Words)> _unsureRates = new();

    /// <summary>One answer's commit-to-final latencies (one per utterance segment the student spoke) and its unsure-word share.</summary>
    public void Record(IReadOnlyList<double>? recognizerLatenciesMs, double? unsureWordShare, int wordCount)
    {
        var at = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (recognizerLatenciesMs is { Count: > 0 })
                foreach (var ms in recognizerLatenciesMs)
                    if (double.IsFinite(ms) && ms is >= 0 and < 10 * 60_000)
                    {
                        _latencies.Enqueue((at, ms));
                        while (_latencies.Count > MaxSamples) _latencies.Dequeue();
                    }
            if (unsureWordShare is { } share && double.IsFinite(share) && wordCount > 0)
            {
                _unsureRates.Enqueue((at, Math.Clamp(share, 0, 1), wordCount));
                while (_unsureRates.Count > MaxSamples) _unsureRates.Dequeue();
            }
        }
    }

    /// <summary>A plain-text summary, the same shape as <c>/healthz/call-timings</c>: counts, average, percentiles.</summary>
    public string Report()
    {
        List<double> latencies;
        List<double> shares;
        lock (_gate)
        {
            latencies = _latencies.Select(x => x.Ms).ToList();
            shares = _unsureRates.Select(x => x.Share).ToList();
        }
        latencies.Sort();
        var text = new StringBuilder();
        text.AppendLine($"Recognizer latency, commit to final transcript (in memory, last {MaxSamples} samples):");
        if (latencies.Count == 0) text.AppendLine("  (no samples yet)");
        else
            text.AppendLine($"  count {latencies.Count}, avg {latencies.Average():0} ms, p50 {Percentile(latencies, 0.5):0} ms, " +
                             $"p95 {Percentile(latencies, 0.95):0} ms, max {latencies[^1]:0} ms");
        text.AppendLine();
        text.AppendLine($"Unsure-word rate per answer, share of words below the library's low-confidence threshold (last {MaxSamples} answers):");
        if (shares.Count == 0) text.AppendLine("  (no samples yet)");
        else text.AppendLine($"  count {shares.Count}, avg {shares.Average():0.0%}, max {shares.Max():0.0%}");
        return text.ToString();
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }
}

public static class VoiceRecognitionTimingsServices
{
    public static IServiceCollection AddVoiceRecognitionTimings(this IServiceCollection services) =>
        services.AddSingleton<VoiceRecognitionTimingRecorder>();

    /// <summary>GET /healthz/voice-recognition (GAD-T361): a plain-text summary, staff-only like /healthz/call-timings.</summary>
    public static RouteHandlerBuilder MapVoiceRecognitionTimings(IEndpointRouteBuilder app) =>
        app.MapGet("/healthz/voice-recognition", (VoiceRecognitionTimingRecorder recorder) =>
            Results.Text(recorder.Report(), "text/plain; charset=utf-8"));
}
