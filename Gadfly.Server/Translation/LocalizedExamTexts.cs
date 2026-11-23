using Gadfly.Engine;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Translation;

/// <summary>
/// The texts a student reads for an exam in one language (GAD-T540): its title, instructions and objective titles. Each falls back
/// to the English text when the language has none. <see cref="MachineDraft"/> is true when any text shown in the language is an
/// unapproved machine draft: the student page must then say so (never present it silently as the instructor's own words).
/// </summary>
public sealed record LocalizedExamTexts(string Language, string Title, string? Instructions, IReadOnlyDictionary<string, string> ObjectiveTitles,
    bool Translated, bool MachineDraft)
{
    /// <summary>The objective's title in this language, else <paramref name="fallback"/>.</summary>
    public string ObjectiveTitle(string key, string fallback) => ObjectiveTitles.GetValueOrDefault(key) ?? fallback;

    /// <summary>
    /// What a student may read (GAD-T630): the approved texts in <paramref name="language"/>, and the English whenever the language's
    /// texts are only an unapproved machine draft, so a draft is never shown to a student. Staff pages use <see cref="ForAsync"/>.
    /// </summary>
    public static async Task<LocalizedExamTexts?> ForStudentAsync(GadflyDbContext db, Guid organizationId, Guid examId, string? language, CancellationToken ct = default)
    {
        var texts = await ForAsync(db, organizationId, examId, language, ct);
        return texts is { MachineDraft: true } ? await ForAsync(db, organizationId, examId, ExamLanguage.English, ct) : texts;
    }

    /// <summary>
    /// Reads an exam's texts in <paramref name="language"/> (English reads the exam row itself). Tenant-filtered; null when the exam
    /// is not visible.
    /// </summary>
    public static async Task<LocalizedExamTexts?> ForAsync(GadflyDbContext db, Guid organizationId, Guid examId, string? language, CancellationToken ct = default)
    {
        var lang = ExamLanguage.Normalize(language);
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == organizationId)
            .Select(e => new { e.Title, e.Instructions }).FirstOrDefaultAsync(ct);
        if (exam is null) return null;
        var objectives = await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId && o.OrganizationId == organizationId)
            .Select(o => new { o.Key, o.Title }).ToListAsync(ct);
        var english = objectives.ToDictionary(o => o.Key, o => o.Title, StringComparer.Ordinal);
        if (ExamLanguage.IsEnglish(lang)) return new(lang, exam.Title, exam.Instructions, english, false, false);

        var row = await db.ExamLocalizations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ExamId == examId && l.OrganizationId == organizationId && l.Language == lang, ct);
        if (row is null) return new(lang, exam.Title, exam.Instructions, english, false, false);
        var titles = ExamLanguageService.ReadTitles(row.ObjectiveTitlesJson);
        var merged = english.ToDictionary(kv => kv.Key, kv => titles.GetValueOrDefault(kv.Key) ?? kv.Value, StringComparer.Ordinal);
        return new(lang,
            string.IsNullOrWhiteSpace(row.Title) ? exam.Title : row.Title,
            string.IsNullOrWhiteSpace(row.Instructions) ? exam.Instructions : row.Instructions,
            merged, true, row.Status == ExamLocalizationStatus.MachineDraft);
    }
}
