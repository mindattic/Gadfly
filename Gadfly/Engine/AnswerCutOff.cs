namespace Gadfly.Engine;

/// <summary>
/// Answer cut-offs (GAD-T462): the end-of-answer silence closed a spoken answer and the student went on speaking within a few
/// seconds, so the answer may have been cut short. The exam room's browser notices it and sends a count and the delay, never
/// audio; the server range-checks it and keeps it with the answer's speech metrics under these keys.
/// <para>
/// It is a fairness measurement and a neutral note for reviewers ("the student may have been cut off"), nothing more. It never
/// steers probing (owner decision GAD-T258: AnswerDelivery reads no <c>cutoff.*</c> key) and never enters a grade. It is recorded
/// only where voice measurements run (GAD-T259, GAD-T283): an answer whose voice was not measured carries no <c>cutoff.*</c> key.
/// </para>
/// </summary>
public static class AnswerCutOff
{
    /// <summary>
    /// 0 on a spoken answer that was watched for a cut-off and not cut off, 1 when it was. Absent: not measured (an older answer,
    /// a typed one, or voice measurements off).
    /// </summary>
    public const string CountKey = "cutoff.count";

    /// <summary>Seconds from the silence closing the answer to the student speaking again (only when <see cref="CountKey"/> is 1).</summary>
    public const string ResumeSecondsKey = "cutoff.resumeSeconds";

    /// <summary>Every cut-off key starts with this.</summary>
    public const string Prefix = "cutoff.";

    /// <summary>Speaking again within this many seconds of the silence closing the answer counts as a cut-off.</summary>
    public const double WindowSeconds = 3;

    /// <summary>The note a reviewer reads. Neutral: the student may simply have added an afterthought.</summary>
    public const string ReviewerNote = "The student may have been cut off";

    /// <summary>True when <paramref name="seconds"/> is a delay the exam room could have reported (finite, 0 to the window).</summary>
    public static bool InRange(double seconds) => double.IsFinite(seconds) && seconds >= 0 && seconds <= WindowSeconds;

    /// <summary>Whether a stored answer was cut off: true or false when measured, null when not.</summary>
    public static bool? Read(IReadOnlyDictionary<string, double>? metrics) =>
        metrics is not null && metrics.TryGetValue(CountKey, out var c) && double.IsFinite(c) ? c > 0 : null;

    /// <summary>The resume delay in seconds when the answer was cut off, else null.</summary>
    public static double? ResumeSeconds(IReadOnlyDictionary<string, double>? metrics) =>
        Read(metrics) == true && metrics!.TryGetValue(ResumeSecondsKey, out var s) && InRange(s) ? s : null;

    /// <summary>The metrics with the "watched, not cut off" marker added (a spoken answer as it is stored).</summary>
    public static IReadOnlyDictionary<string, double> Watched(IReadOnlyDictionary<string, double> metrics)
    {
        var d = metrics.ToDictionary(kv => kv.Key, kv => kv.Value);
        d[CountKey] = 0;
        return d;
    }

    /// <summary>The metrics with the cut-off recorded. Only a watched answer (one carrying <see cref="CountKey"/>) can be marked.</summary>
    public static IReadOnlyDictionary<string, double>? Mark(IReadOnlyDictionary<string, double> metrics, double resumeSeconds)
    {
        if (!InRange(resumeSeconds) || Read(metrics) != false) return null;
        var d = metrics.ToDictionary(kv => kv.Key, kv => kv.Value);
        d[CountKey] = 1;
        d[ResumeSecondsKey] = Math.Round(resumeSeconds, 2);
        return d;
    }
}
