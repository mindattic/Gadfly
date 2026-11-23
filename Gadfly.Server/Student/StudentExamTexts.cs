using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Translation;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>
/// An exam's texts as a student reads them (GAD-T630): the approved localized title, instructions and objective titles, and the
/// English for any language whose texts are still an unapproved machine draft. Objectives are found by their English title,
/// which is what a stored report and result carry.
/// </summary>
public sealed class StudentExamTexts
{
    private readonly Dictionary<string, string> _byEnglish;

    private StudentExamTexts(string title, string? instructions, Dictionary<string, string> byEnglish)
    {
        Title = title;
        Instructions = instructions;
        _byEnglish = byEnglish;
    }

    public string Title { get; }
    public string? Instructions { get; }

    /// <summary>The objective's title in the student's language, else the English <paramref name="englishTitle"/>.</summary>
    public string ObjectiveTitle(string englishTitle) => _byEnglish.GetValueOrDefault(englishTitle) ?? englishTitle;

    /// <summary>The texts for <paramref name="language"/>; null when the exam isn't visible.</summary>
    public static async Task<StudentExamTexts?> ForAsync(GadflyDbContext db, Guid organizationId, Guid examId, string? language, CancellationToken ct = default)
    {
        var texts = await LocalizedExamTexts.ForStudentAsync(db, organizationId, examId, language, ct);
        if (texts is null) return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (texts.Translated)
        {
            var english = await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId && o.OrganizationId == organizationId)
                .Select(o => new { o.Key, o.Title }).ToListAsync(ct);
            foreach (var o in english) map[o.Title] = texts.ObjectiveTitle(o.Key, o.Title);
        }
        return new StudentExamTexts(texts.Title, texts.Instructions, map);
    }

    /// <summary>The report with the exam and objective titles in the attempt's language (GAD-T630); unchanged for English.</summary>
    public static async Task<AssessmentReport> LocalizeAsync(IServiceScopeFactory scopes, Guid organizationId, Guid examId, string? language, AssessmentReport report)
    {
        if (Gadfly.Engine.ExamLanguage.IsEnglish(language)) return report;
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var t = await ForAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), organizationId, examId, language);
        return t is null ? report : Localize(report, t);
    }

    /// <summary>Swaps the exam title and each English objective title (also inside knowledge-gap lines) for the student's.</summary>
    public static AssessmentReport Localize(AssessmentReport report, StudentExamTexts t)
    {
        var gaps = report.KnowledgeGaps.Select(g =>
        {
            foreach (var o in report.Objectives)
                if (g.StartsWith(o.Title + ":", StringComparison.Ordinal)) return t.ObjectiveTitle(o.Title) + g[o.Title.Length..];
            return g;
        }).ToList();
        return report with
        {
            ExamTitle = t.Title,
            Objectives = report.Objectives.Select(o => o with { Title = t.ObjectiveTitle(o.Title) }).ToList(),
            KnowledgeGaps = gaps,
        };
    }
}
