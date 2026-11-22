using Gadfly.Server.Data;
using Gadfly.Server.Verification;
using Gadfly.Sources.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Speech-recognition accuracy by group (GAD-T265, docs/EVIDENCE_MAP.md mechanic 7, research note 02: Koenecke2020,
// Mujtaba2024). Recognizers make more word errors for some accents and dialects and for people who stutter, and a
// transcription error can be graded as wrong content. This audit measures Gadfly's own provider on Gadfly's own answers:
// word error rate against the transcripts students checked, how often students had to correct a transcript, and how often
// the recognizer was unsure, compared across groups. Gadfly collects no accent, dialect or disability data: the default
// grouping is a proxy read from the transcripts (fillers and repetitions), and any other grouping is pasted in for the
// comparison, used in memory and never stored. No student is named; groups too small to report are withheld.

/// <summary>The audit: groups, answers looked at, students the pasted grouping could not place, and warnings.</summary>
public sealed record SpeechAuditReport(IReadOnlyList<SpeechGroupStats> Groups, int Responses, int Unplaced, IReadOnlyList<string> Warnings);

public sealed class SpeechRecognitionAudit(GadflyDbContext db, Guid organizationId)
{
    /// <summary>Groups whose word error rate (or correction share) differs by more than this ratio, highest over lowest, are flagged.</summary>
    public const double DisparityRatio = 1.25;

    public const string Caveat =
        "Word error rate here is measured against a reference: a human listener's transcript of a sampled answer (Owners and Admins " +
        "mark these from the link below), or, for answers before 22 November 2026, the transcript the student confirmed or corrected. " +
        "Spoken answers are no longer read back to the student, so newer answers add to the word error rate only once a listener has " +
        "transcribed them; the unsure-word share still covers every spoken answer. Student checks were asked mostly when the recognizer " +
        "was unsure, so those rates describe the answers most at risk: compare groups with each other, not with published benchmarks. " +
        "Recognizers make more errors for some accents and dialects and for people who stutter.";

    /// <summary>The analytics scope's assessments (published or archived, as the analytics page reports on).</summary>
    public async Task<IReadOnlyList<Guid>> ExamIdsAsync(AnalyticsScope scope, CancellationToken ct = default)
    {
        var q = db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.Status != ExamStatus.Draft);
        if (scope.ExamId is { } examId) q = q.Where(e => e.Id == examId);
        else if (scope.CourseId is { } courseId) q = q.Where(e => e.CourseId == courseId);
        return await q.Select(e => e.Id).ToListAsync(ct);
    }

    /// <summary>
    /// The audit over the voice answers (original responses, not revisions) of the given exams, grouped by
    /// <paramref name="groupOf"/> (student id to group; null leaves the student out as unplaced). Without a grouping, each
    /// student is placed by the fillers and repetitions in their recognized answers (<see cref="SpeechAccuracy.DisfluencyBand"/>).
    /// </summary>
    public async Task<SpeechAuditReport> BuildAsync(IReadOnlyCollection<Guid> examIds, Func<Guid, string?>? groupOf = null, CancellationToken ct = default)
    {
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && examIds.Contains(s.ExamId))
            .Select(s => new { s.Id, s.StudentUserId }).ToListAsync(ct);
        var studentOf = sessions.ToDictionary(s => s.Id, s => s.StudentUserId);
        var ids = sessions.Select(s => s.Id).ToList();
        var responses = new List<StudentResponse>();
        foreach (var chunk in ids.Chunk(500))
            responses.AddRange(await db.StudentResponses.AsNoTracking()
                .Where(r => r.OrganizationId == organizationId && chunk.Contains(r.SessionId) && r.Modality == "voice" && r.Kind == ResponseKind.Original)
                .ToListAsync(ct));

        var samples = new List<(Guid Student, SpeechSample Sample)>();
        foreach (var chunk in responses.Chunk(500))
        {
            var marks = await SpeechMarks.LoadAsync(db, organizationId, chunk, ct);
            // A human listener's transcript (GAD-T426) is the reference wherever one exists; otherwise the student's check, if any.
            var references = await ReferenceTranscripts.LoadAsync(db, organizationId, chunk.Select(r => r.Id).ToList(), ct);
            foreach (var r in chunk)
            {
                var m = marks.GetValueOrDefault(r.Id);
                var words = m?.Words ?? [];
                var student = studentOf.GetValueOrDefault(r.SessionId);
                var human = references.GetValueOrDefault(r.Id);
                samples.Add((student, new SpeechSample(student.ToString(), r.SpeechRecognitionText ?? r.OriginalText, human?.Text ?? r.VerifiedText,
                    human is not null || (m?.TranscriptChecked ?? false), m?.Corrected ?? false, words.Count,
                    words.Count(w => w.Confidence < Gadfly.Sources.Verification.ResponseVerifier.LowSpeechConfidenceThreshold),
                    words.Count == 0 ? r.SpeechRecognitionConfidence : words.Average(w => w.Confidence), human is not null)));
            }
        }

        Func<Guid, string?> group = groupOf ?? DisfluencyGroups(samples);
        var placed = samples.Select(s => (s.Student, s.Sample, Group: Clean(group(s.Student)))).ToList();
        var unplaced = placed.Where(p => p.Group is null).Select(p => p.Student).Distinct().Count();
        var groups = placed.Where(p => p.Group is not null).GroupBy(p => p.Group!, StringComparer.OrdinalIgnoreCase)
            .Select(g => SpeechAccuracy.Summarize(g.First().Group!, g.Select(p => p.Sample)))
            .OrderBy(g => groupOf is null ? BandOrder(g.Group) : 0).ThenBy(g => g.Group, StringComparer.OrdinalIgnoreCase).ToList();
        // Withheld groups keep only their name and size.
        groups = groups.Select(g => g.Reportable ? g : new SpeechGroupStats(g.Group, g.Students, 0, 0, 0, 0, 0, null, null, null, false)).ToList();
        return new SpeechAuditReport(groups, samples.Count, unplaced, Warnings(groups));
    }

    /// <summary>Each student's observed-disfluency band over all their recognized answers.</summary>
    private static Func<Guid, string?> DisfluencyGroups(IEnumerable<(Guid Student, SpeechSample Sample)> samples)
    {
        var bands = samples.GroupBy(s => s.Student).ToDictionary(g => g.Key, g =>
        {
            var text = string.Join(" ", g.Select(s => s.Sample.RecognizedText));
            return SpeechAccuracy.DisfluencyBand(SpeechAccuracy.DisfluencyRate(text));
        });
        return id => bands.GetValueOrDefault(id);
    }

    private static int BandOrder(string group) => group switch
    {
        SpeechAccuracy.FluentBand => 0,
        SpeechAccuracy.SomeBand => 1,
        SpeechAccuracy.FrequentBand => 2,
        _ => 3,
    };

    /// <summary>Too little to compare, or rates that differ by more than <see cref="DisparityRatio"/> between groups.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<SpeechGroupStats> groups)
    {
        var usable = groups.Where(g => g.Reportable && g.Responses > 0).ToList();
        var w = new List<string>();
        if (usable.Count < 2)
        {
            w.Add($"Fewer than two groups have {SpeechAccuracy.MinStudentsPerGroup} or more students with voice answers, so the groups can't be compared yet.");
            return w;
        }
        void Check(string what, Func<SpeechGroupStats, double?> rate, string unit)
        {
            var rates = usable.Where(g => rate(g) is not null).Select(g => (g.Group, Rate: rate(g)!.Value)).ToList();
            if (rates.Count < 2) return;
            var hi = rates.MaxBy(r => r.Rate);
            var lo = rates.MinBy(r => r.Rate);
            if (hi.Rate <= 0) return;
            if (lo.Rate <= 0 || hi.Rate / lo.Rate > DisparityRatio)
                w.Add($"{what} differs between groups: {hi.Group} {hi.Rate.ToString(unit)} against {lo.Group} {lo.Rate.ToString(unit)}. " +
                      "Ask students in the higher group to check their transcripts, and read the low-confidence marks in grading with care.");
        }
        Check("Word error rate", g => g.WordErrorRate, "0.0%");
        Check("The share of answers the student had to correct", g => g.CorrectedShare, "0.0%");
        Check("The share of words the recognizer was unsure of", g => g.LowConfidenceShare, "0.0%");
        return w;
    }

    /// <summary>
    /// Reads a grouping pasted in for the comparison, one "email or user name, group" per line (comma, tab or semicolon), against
    /// the organization's members. Returns student id to group; lines that match no member are counted, never echoed.
    /// </summary>
    public static (Dictionary<Guid, string> Map, int Unmatched) ParseGrouping(string? text, IEnumerable<(Guid UserId, string UserName, string? Email)> members)
    {
        var byKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in members)
        {
            if (!string.IsNullOrWhiteSpace(m.Email)) byKey.TryAdd(m.Email.Trim(), m.UserId);
            if (!string.IsNullOrWhiteSpace(m.UserName)) byKey.TryAdd(m.UserName.Trim(), m.UserId);
        }
        var map = new Dictionary<Guid, string>();
        var unmatched = 0;
        foreach (var raw in (text ?? "").Split('\n').Take(20000))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cut = line.IndexOfAny([',', '\t', ';']);
            if (cut <= 0) { unmatched++; continue; }
            var who = line[..cut].Trim().Trim('"');
            var group = Clean(line[(cut + 1)..].Trim().Trim('"'));
            if (group is null || !byKey.TryGetValue(who, out var id)) { unmatched++; continue; }
            map[id] = group;
        }
        return (map, unmatched);
    }

    private static string? Clean(string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return null;
        var g = new string(group.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return g.Length == 0 ? null : g.Length > 60 ? g[..60] : g;
    }
}
