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
    /// The most keyterms handed to the recognizer. Well under the library's own cap (100, <see cref="MindAttic.SpeechRecognition.AspNetCore.SpeechSessionManager"/>)
    /// so Gadfly's list is never the one silently truncating.
    /// </summary>
    public const int MaxTerms = 60;

    /// <summary>Longest a single term may be; a longer one is dropped rather than sent as an unreasonable "word".</summary>
    public const int MaxTermLength = 60;

    /// <summary>
    /// The vocabulary for the exam <paramref name="sessionId"/> belongs to: every learning objective's title, plus the
    /// title of each objective's linked concept (the source material's own term for it). Empty for an exam without
    /// learning objectives (the older, concept-only exam builder) or one that belongs to another organization.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ForSessionAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, CancellationToken ct = default)
    {
        var exam = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId)
            .Select(s => new { s.ExamId }).FirstOrDefaultAsync(ct);
        if (exam is null) return [];

        var objectives = await db.LearningObjectives.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && o.ExamId == exam.ExamId)
            .OrderBy(o => o.Ordinal)
            .Select(o => new { o.Title, o.ConceptKey }).ToListAsync(ct);
        if (objectives.Count == 0) return [];

        var conceptKeys = objectives.Where(o => !string.IsNullOrWhiteSpace(o.ConceptKey)).Select(o => o.ConceptKey!).Distinct(StringComparer.Ordinal).ToList();
        var collectionId = await db.Exams.AsNoTracking().Where(e => e.Id == exam.ExamId && e.OrganizationId == organizationId)
            .Select(e => (Guid?)e.CollectionId).FirstOrDefaultAsync(ct);
        var conceptTitles = conceptKeys.Count == 0 || collectionId is null ? []
            : await db.Concepts.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && c.CollectionId == collectionId && conceptKeys.Contains(c.Key))
                .Select(c => c.Title).ToListAsync(ct);

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? candidate)
        {
            var t = (candidate ?? "").Trim();
            if (t.Length == 0 || t.Length > MaxTermLength || terms.Count >= MaxTerms || !seen.Add(t)) return;
            terms.Add(t);
        }
        foreach (var o in objectives) Add(o.Title);
        foreach (var t in conceptTitles) Add(t);
        return terms;
    }
}
