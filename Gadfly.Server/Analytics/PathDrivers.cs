using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Path-driver rates by group (GAD-T434; ElenchusAtScale M15, Sections 5.3.3, 5.3.4 and 6.4, Appendix A.10 "content cues by
// language"; docs/EVIDENCE_MAP.md mechanic 2). The examiner presses harder after an answer whose content shows a cue
// (AnswerDelivery.Read: hedging, vague wording, no reason or example, a judged score not yet convincing, evidence against,
// unclear meaning, recitation). None of it reaches the score, but it changes which questions a student gets, so its rates are
// compared by group: per answer, how often each cue fired and how often the pressure was raised, with long pauses per answering
// minute and exam time lost to device pauses. The hedging, vague-phrase and reason lists are English word lists, so they may
// fire more for second-language speakers; a pasted first-language grouping is the comparison that would show it.
// The cues are recomputed from each session's stored transcript exactly as the examiner computed them.

/// <summary>One answered question, as the path-driver rates see it.</summary>
/// <param name="ReasonAsked">The move asked for a reason or example, so <paramref name="NoReason"/> can fire.</param>
/// <param name="CutOff">
/// The end-of-answer silence closed it and the student went on speaking at once (GAD-T462); null when not measured (typed, older,
/// or voice measurements off).
/// </param>
public sealed record PathDriverAnswer(string Group, Guid Student, bool Typed, double AnswerSeconds, int LongPauses, bool Hedging, bool Vague,
    bool ReasonAsked, bool NoReason, bool NotConvincing, bool EvidenceAgainst, bool Unclear, bool Recited, ProbePressure Pressure, bool? CutOff = null,
    bool PausesMeasured = true);

/// <summary>One session's exam time and the time its clock stood still for a lost device.</summary>
public sealed record PathDriverSession(string Group, Guid Student, double ExamMinutes, double DevicePausedMinutes, double StaffHeldMinutes);

/// <summary>
/// One group's cue and pressure rates. Shares are of answers (the no-reason share of answers whose question asked for one; the
/// cut-off share, GAD-T462, of the <see cref="CutOffMeasured"/> spoken answers watched for one).
/// </summary>
public sealed record PathDriverGroup(string Group, int Students, int Answers, double? TypedShare, double? Hedging, double? Vague,
    double? NoReason, double? NotConvincing, double? EvidenceAgainst, double? Unclear, double? Recited, double? PressureRaised,
    double? Shaky, double? LongPausesPerMinute, double? SessionsWithDevicePause, double? DevicePausedShare, double? CutOff = null, int CutOffMeasured = 0)
{
    public bool TooFew => Students < GroupFairness.MinStudents;
}

public sealed record PathDriversReport(IReadOnlyList<PathDriverGroup> Groups, int Answers, IReadOnlyList<string> Warnings)
{
    /// <summary>The total of <see cref="Answers"/> is not shown while any group is withheld (GAD-T486).</summary>
    public bool TotalWithheld => GroupFairness.WithholdsTotal(Groups, g => g.TooFew);
}

public sealed class PathDrivers(GadflyDbContext db, Guid organizationId)
{
    /// <summary>Groups whose rate of a cue differs by more than this ratio, highest over lowest, are warned (as the gaze rates).</summary>
    public const double DisparityRatio = 1.25;

    /// <summary>A cue's rate is compared only between groups with at least this many answers that could show it.</summary>
    public const int MinAnswers = 20;

    public const string Caveat =
        "How often each cue that makes the examiner press harder fired, per answer. The cues come only from what the answer said and " +
        "how the judges read it, never the voice, and never reach the grade, but they decide which questions a student gets. The " +
        "hedging, vague-wording and reason checks are English word lists, so they may fire more for second-language speakers; paste a " +
        "first-language grouping to compare. \"Cut off\" is how often the student went on speaking within a few seconds of the " +
        "end-of-answer silence closing a spoken answer, of the spoken answers watched for it (none where voice measurements are off, " +
        "none before it was recorded): a group cut off more often may need a longer silence. It never steers the questions or the grade.";

    /// <summary>The rates for the given sessions, from each session's stored transcript.</summary>
    public async Task<PathDriversReport> BuildAsync(IReadOnlyList<FairSession> sessions, Func<FairSession, string?> groupOf, CancellationToken ct = default)
    {
        var answers = new List<PathDriverAnswer>();
        var rows = new List<PathDriverSession>();
        var placed = sessions.Select(s => (Session: s, Group: groupOf(s))).Where(x => x.Group is not null).ToDictionary(x => x.Session.Id, x => x);
        foreach (var chunk in placed.Keys.Chunk(200))
        {
            var stored = await db.ExamSessions.AsNoTracking()
                .Where(s => s.OrganizationId == organizationId && chunk.Contains(s.Id) && s.TranscriptJson != null)
                .Select(s => new { s.Id, s.TranscriptJson }).ToListAsync(ct);
            foreach (var row in stored)
            {
                SocraticSession transcript;
                try { transcript = SessionTranscriptSerializer.Deserialize(row.TranscriptJson!); }
                catch (JsonException) { continue; }
                var (session, group) = placed[row.Id];
                var read = Read(group!, session.StudentUserId, transcript);
                answers.AddRange(read.Answers);
                if (read.Session is { } s) rows.Add(s);
            }
        }
        var groups = Summarize(answers, rows);
        return new PathDriversReport(groups, answers.Count, Warnings(groups));
    }

    /// <summary>The answers of one transcript and its session row (null when nothing was answered).</summary>
    public static (IReadOnlyList<PathDriverAnswer> Answers, PathDriverSession? Session) Read(string group, Guid student, SocraticSession transcript)
    {
        var list = new List<PathDriverAnswer>();
        foreach (var turn in transcript.Turns)
        {
            if (turn.Answer is not { } answer || string.IsNullOrWhiteSpace(answer.Text)) continue;
            if (AnswerDelivery.Read(transcript, turn) is not { } d) continue;
            var seconds = Math.Clamp((answer.CompletedAt - (answer.StartedAt ?? turn.AskedAt)).TotalSeconds, 0, 3600);
            list.Add(new PathDriverAnswer(group, student, answer.Mode != "voice", seconds, d.LongPauses,
                d.Hedges.Count >= AnswerDelivery.HedgesCue, d.Vague.Count >= AnswerDelivery.VagueCue,
                d.GaveReasonOrExample is not null, d.GaveReasonOrExample == false,
                d.Cues.Any(c => c.StartsWith("the judges did not find it convincing", StringComparison.Ordinal)),
                d.Cues.Any(c => c.StartsWith("evidence against understanding", StringComparison.Ordinal)),
                d.Cues.Any(c => c.StartsWith("unclear meaning", StringComparison.Ordinal)),
                d.Recitation is not null, d.Pressure, answer.Mode == "voice" ? AnswerCutOff.Read(answer.SpeechMetrics) : null,
                PausesMeasured(answer)));
        }
        if (list.Count == 0) return (list, null);
        var end = transcript.EndedAt ?? transcript.Turns.Select(t => t.Answer?.CompletedAt ?? t.AskedAt).Max();
        var minutes = Math.Max(0, (end - transcript.StartedAt).TotalMinutes);
        return (list, new PathDriverSession(group, student, minutes, transcript.PausedTotal.TotalMinutes, transcript.HeldTotal.TotalMinutes));
    }

    /// <summary>
    /// Whether the answer's long pauses were measured (GAD-T480): a spoken answer carrying the pause count, taken with voice
    /// measurements on. Where they are off (EU/EEA organizations and students, GAD-T283) or the answer predates the count,
    /// AnswerDelivery reads the count as 0, which is "not measured", not "no long pauses".
    /// </summary>
    public static bool PausesMeasured(Answer answer) =>
        answer.Mode == "voice" && answer.SpeechMetrics.ContainsKey(AnswerDelivery.LongPauseCountKey)
        && !(answer.SpeechMetrics.TryGetValue(AnswerDelivery.StrainOffKey, out var off) && off > 0);

    /// <summary>One row per group; groups under <see cref="GroupFairness.MinStudents"/> students keep only their name and size.</summary>
    public static IReadOnlyList<PathDriverGroup> Summarize(IEnumerable<PathDriverAnswer> answers, IEnumerable<PathDriverSession> sessions)
    {
        var bySession = sessions.GroupBy(s => s.Group, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        return GroupFairness.Ordered(answers.GroupBy(a => a.Group, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var students = g.Select(a => a.Student).Distinct().Count();
            if (students < GroupFairness.MinStudents)
                return new PathDriverGroup(g.First().Group, students, 0, null, null, null, null, null, null, null, null, null, null, null, null, null);
            var all = g.ToList();
            double? Share(Func<PathDriverAnswer, bool> f, Func<PathDriverAnswer, bool>? of = null)
            {
                var pool = of is null ? all : all.Where(of).ToList();
                return pool.Count == 0 ? null : pool.Count(f) / (double)pool.Count;
            }
            // Only answers whose pauses were measured: unmeasured ones (voice measurements off, older answers) count no pauses and would
            // otherwise dilute the rate, making a group with many EU students look calmer than it is (GAD-T480).
            var spoken = all.Where(a => !a.Typed && a.PausesMeasured).ToList();
            var spokenMinutes = spoken.Sum(a => a.AnswerSeconds) / 60;
            var rows = bySession.GetValueOrDefault(g.Key) ?? [];
            var examMinutes = rows.Sum(r => r.ExamMinutes);
            var watched = all.Count(a => a.CutOff is not null);
            return new PathDriverGroup(g.First().Group, students, all.Count, Share(a => a.Typed), Share(a => a.Hedging), Share(a => a.Vague),
                Share(a => a.NoReason, a => a.ReasonAsked), Share(a => a.NotConvincing), Share(a => a.EvidenceAgainst), Share(a => a.Unclear),
                Share(a => a.Recited), Share(a => a.Pressure != ProbePressure.Steady), Share(a => a.Pressure is ProbePressure.Shaky or ProbePressure.Recited),
                spokenMinutes <= 0 ? null : spoken.Sum(a => a.LongPauses) / spokenMinutes,
                rows.Count == 0 ? null : rows.Count(r => r.DevicePausedMinutes > 0) / (double)rows.Count,
                examMinutes <= 0 ? null : Math.Min(1, rows.Sum(r => r.DevicePausedMinutes) / examMinutes),
                Share(a => a.CutOff == true, a => a.CutOff is not null), watched);
        }), g => g.Group).ToList();
    }

    /// <summary>Too little to compare, or a cue (or the raised pressure) whose rate differs by more than <see cref="DisparityRatio"/>.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<PathDriverGroup> groups)
    {
        var usable = groups.Where(g => !g.TooFew && g.Answers >= MinAnswers).ToList();
        var w = new List<string>();
        if (usable.Count < 2)
        {
            w.Add($"Fewer than two groups have {GroupFairness.MinStudents} or more students and {MinAnswers} or more answers, so the rates can't be compared yet.");
            return w;
        }
        void Check(string what, Func<PathDriverGroup, double?> rate, string why)
        {
            var rates = usable.Where(g => rate(g) is not null).Select(g => (g.Group, Rate: rate(g)!.Value)).ToList();
            if (rates.Count < 2) return;
            var hi = rates.MaxBy(r => r.Rate);
            var lo = rates.MinBy(r => r.Rate);
            if (hi.Rate <= 0) return;
            if (lo.Rate <= 0 || hi.Rate / lo.Rate > DisparityRatio)
                w.Add($"{what} differs between groups: {hi.Group} {hi.Rate:0%} of answers against {lo.Group} {lo.Rate:0%}. {why}");
        }
        const string english = "The word lists are English; a second-language speaker may hedge or use plainer words without understanding less.";
        Check("Raised probing pressure", g => g.PressureRaised, "One group is pressed harder more often; look at which cue drives it below.");
        Check("The hedging cue", g => g.Hedging, english);
        Check("The vague-wording cue", g => g.Vague, english);
        Check("The no-reason-or-example cue", g => g.NoReason, english);
        Check("The not-convincing cue", g => g.NotConvincing, "It follows the judges' score; compare with the score gaps above.");
        // GAD-T462: only groups with enough watched spoken answers; a group cut off more often may need a longer end-of-answer silence.
        var cutUsable = usable.Where(g => g.CutOffMeasured >= MinAnswers).ToList();
        if (cutUsable.Count >= 2)
        {
            var hi = cutUsable.MaxBy(g => g.CutOff ?? 0)!;
            var lo = cutUsable.MinBy(g => g.CutOff ?? 0)!;
            if ((hi.CutOff ?? 0) > 0 && ((lo.CutOff ?? 0) <= 0 || hi.CutOff!.Value / lo.CutOff!.Value > DisparityRatio))
                w.Add($"Cut-offs differ between groups: {hi.Group} {hi.CutOff:0%} of watched spoken answers against {lo.Group} {lo.CutOff ?? 0:0%}. " +
                      "The end-of-answer silence may be too short for one group; consider a longer silence (Gadfly:Voice:EndOfAnswerSeconds) or extra time, which lengthens it.");
        }
        return w;
    }
}
