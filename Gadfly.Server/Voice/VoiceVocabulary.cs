using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// The exam's own vocabulary (GAD-T358): learning objective titles and the source material's key terms (via each
/// objective's linked concept), given to the speech recognizer as keyterm hints so it leans toward the words the exam
/// is actually about ("mitosis", "photosynthesis") instead of whatever it guesses from sound alone. Computed here, on the
/// server, from the exam the student is actually sitting: never trusted from the browser (<see cref="VoiceGrants"/> is the
/// only place a client-chosen list could sneak in, and it is validated there against exactly this list).
/// </summary>
public static class VoiceVocabulary
{
    /// <summary>
    /// The most keyterms handed to the recognizer (GAD-T597): ElevenLabs realtime Scribe (<c>scribe_v3_live</c>, <c>scribe_v2_realtime</c>)
    /// accepts at most 50 keyterms (keyterm prompting guide, read 2026-11-22), under the library's own cap of 100
    /// (<see cref="MindAttic.SpeechRecognition.AspNetCore.SpeechSessionManager"/>), so Gadfly's list is never the one cut by a provider.
    /// </summary>
    public const int MaxTerms = 50;

    /// <summary>The most characters all keyterms may have together (GAD-T597): Scribe realtime's limit is 1,000.</summary>
    public const int MaxTotalChars = 1000;

    /// <summary>Longest a single term may be; a longer one is dropped rather than sent as an unreasonable "word".</summary>
    public const int MaxTermLength = 60;

    /// <summary>
    /// The vocabulary for the exam <paramref name="sessionId"/> belongs to: every learning objective's title, plus the
    /// title of each objective's linked concept (the source material's own term for it). Empty for an exam without
    /// learning objectives (the older, concept-only exam builder) or one that belongs to another organization.
    /// </summary>
    /// <para>
    /// In an attempt in another language (GAD-T529, research note R6.1) the objective titles in that language (the exam's localization,
    /// <see cref="ExamLocalization"/>) come first, then the English ones: answers in Korean borrow English technical terms freely, so the
    /// recognizer is given both scripts. Most important first, so <see cref="Cap"/> drops the least important.
    /// </para>
    public static async Task<IReadOnlyList<string>> ForSessionAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, CancellationToken ct = default)
    {
        var exam = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId)
            .Select(s => new { s.ExamId, s.Language }).FirstOrDefaultAsync(ct);
        if (exam is null) return [];

        var objectives = await db.LearningObjectives.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && o.ExamId == exam.ExamId)
            .OrderBy(o => o.Ordinal)
            .Select(o => new { o.Key, o.Title, o.ConceptKey }).ToListAsync(ct);
        if (objectives.Count == 0) return [];

        var localized = new List<string>();
        var language = Gadfly.Engine.ExamLanguage.Normalize(exam.Language);
        if (!Gadfly.Engine.ExamLanguage.IsEnglish(language))
        {
            var json = await db.ExamLocalizations.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && l.ExamId == exam.ExamId && l.Language == language)
                .Select(l => l.ObjectiveTitlesJson).FirstOrDefaultAsync(ct);
            var titles = LocalizedTitles(json);
            foreach (var o in objectives)
                if (titles.TryGetValue(o.Key, out var t)) localized.Add(t);
        }

        var conceptKeys = objectives.Where(o => !string.IsNullOrWhiteSpace(o.ConceptKey)).Select(o => o.ConceptKey!).Distinct(StringComparer.Ordinal).ToList();
        var collectionId = await db.Exams.AsNoTracking().Where(e => e.Id == exam.ExamId && e.OrganizationId == organizationId)
            .Select(e => (Guid?)e.CollectionId).FirstOrDefaultAsync(ct);
        var conceptTitles = conceptKeys.Count == 0 || collectionId is null ? []
            : await db.Concepts.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && c.CollectionId == collectionId && conceptKeys.Contains(c.Key))
                .Select(c => c.Title).ToListAsync(ct);

        return Cap(localized.Concat(objectives.Select(o => o.Title)).Concat(conceptTitles));
    }

    /// <summary>
    /// The candidates in order, trimmed, deduplicated (ignoring case), without blank or over-long ones, stopping before
    /// <see cref="MaxTerms"/> terms or <see cref="MaxTotalChars"/> characters in all (GAD-T597): a term that would pass the character
    /// budget is skipped and a shorter one after it may still fit.
    /// </summary>
    public static IReadOnlyList<string> Cap(IEnumerable<string?> candidates)
    {
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chars = 0;
        foreach (var candidate in candidates)
        {
            if (terms.Count >= MaxTerms) break;
            var t = (candidate ?? "").Trim();
            if (t.Length == 0 || t.Length > MaxTermLength || chars + t.Length > MaxTotalChars || !seen.Add(t)) continue;
            terms.Add(t);
            chars += t.Length;
        }
        return terms;
    }

    /// <summary>An exam localization's objective titles (key to title); empty when absent or not a JSON object of strings.</summary>
    private static Dictionary<string, string> LocalizedTitles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)?
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
