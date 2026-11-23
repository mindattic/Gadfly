using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// How fast each voice tier actually starts speaking (GAD-T617): the median and count of the live
/// <c>voice.question.firstAudio@&lt;model&gt;</c> rows (<see cref="QuestionVoiceStream.ModelTimingPrefix"/>, from the first part handed
/// to ElevenLabs to its first audio) over the last <see cref="Days"/> days, platform-wide (the rows hold names and durations only).
/// Shown next to the tier picker on the organization settings page, so whoever picks a tier sees measured numbers, not just the docs'.
/// </summary>
public static class VoiceTierLatency
{
    public const int Days = 30;

    /// <summary>The most recent measurements read per tier: enough for a stable median, bounded for a busy platform.</summary>
    public const int MaxRowsPerTier = 5000;

    /// <param name="Tier">The tier measured.</param>
    /// <param name="Count">Successful first-audio rows in the window (up to <see cref="MaxRowsPerTier"/>); 0 means no measurements yet.</param>
    /// <param name="MedianMs">Their median in milliseconds, or null with none.</param>
    public sealed record Measure(VoiceTier Tier, int Count, int? MedianMs);

    /// <summary>One <see cref="Measure"/> per tier in <see cref="VoiceTier.All"/>, in that order.</summary>
    public static async Task<IReadOnlyList<Measure>> ForTiersAsync(GadflyDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var since = now.AddDays(-Days);
        var result = new List<Measure>();
        foreach (var tier in VoiceTier.All)
        {
            var name = QuestionVoiceStream.ModelTimingPrefix + tier.ModelId;
            var durations = await db.ApiCallTimings.AsNoTracking()
                .Where(t => t.CallName == name && t.Succeeded && t.StartedAtUtc >= since)
                .OrderByDescending(t => t.StartedAtUtc).Take(MaxRowsPerTier)
                .Select(t => t.DurationMs).ToListAsync(ct);
            result.Add(new Measure(tier, durations.Count, Median(durations)));
        }
        return result;
    }

    /// <summary>The median of <paramref name="values"/> (the mean of the middle two for an even count, rounded), or null when empty.</summary>
    public static int? Median(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (int)Math.Round((sorted[mid - 1] + (double)sorted[mid]) / 2, MidpointRounding.AwayFromZero);
    }
}
