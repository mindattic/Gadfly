using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>One exam in the student's catalog (GAD-T658): its title in the student's language, its category and offered languages.</summary>
public sealed record CatalogExam(Guid ExamId, string Title, Guid? CategoryId, IReadOnlyList<string> Languages, bool Assigned);

/// <summary>A category in the student's catalog, named in the student's language, with its exams by title.</summary>
public sealed record CatalogCategory(Guid? Id, string Name, IReadOnlyList<CatalogExam> Exams);

/// <summary>
/// Browse by category (GAD-T658): every published exam the student can take, grouped by the organization's categories (GAD-T656).
/// That is each published exam open to all students (<see cref="Exam.OpenToAllStudents"/>) when open exams are on, plus every
/// published exam the student is assigned. Titles are the approved texts in the student's language, the English otherwise (never
/// an unapproved machine draft, GAD-T630); category names are the category's display name in that language, else its name.
/// Uncategorized exams come last, in a group with a null id.
/// </summary>
public static class StudentCatalog
{
    public static async Task<IReadOnlyList<CatalogCategory>> ListAsync(IServiceScopeFactory scopes, Guid organizationId, Guid studentUserId,
        bool openExams, string? language, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();

        var assigned = await db.ExamAssignments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.StudentUserId == studentUserId)
            .Select(a => a.ExamId).Distinct().ToListAsync(ct);
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.Status == ExamStatus.Published
                        && ((openExams && e.OpenToAllStudents) || assigned.Contains(e.Id)))
            .Select(e => new { e.Id, e.Title, e.CategoryId, e.Languages })
            .ToListAsync(ct);
        if (exams.Count == 0) return [];

        var lang = ExamLanguage.Normalize(language);
        var titles = new Dictionary<Guid, string>();
        if (!ExamLanguage.IsEnglish(lang))
        {
            var ids = exams.Select(e => e.Id).ToList();
            titles = await db.ExamLocalizations.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && l.Language == lang && l.Status == ExamLocalizationStatus.Approved
                            && ids.Contains(l.ExamId) && l.Title != "")
                .ToDictionaryAsync(l => l.ExamId, l => l.Title, ct);
        }

        var categories = await db.ExamCategories.AsNoTracking().Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var mine = assigned.ToHashSet();
        var culture = Localization.UiCatalog.CultureFor(lang);
        var comparer = StringComparer.Create(culture, ignoreCase: true);
        var rows = exams.Select(e => new CatalogExam(e.Id, titles.GetValueOrDefault(e.Id) ?? e.Title,
                e.CategoryId is { } c && categories.Any(x => x.Id == c) ? c : null,
                ExamLanguage.ParseList(e.Languages), mine.Contains(e.Id)))
            .ToList();

        var groups = new List<CatalogCategory>();
        foreach (var c in categories)
        {
            var inIt = rows.Where(r => r.CategoryId == c.Id).OrderBy(r => r.Title, comparer).ToList();
            if (inIt.Count == 0) continue;
            groups.Add(new CatalogCategory(c.Id, DisplayName(c, lang), inIt));
        }
        var loose = rows.Where(r => r.CategoryId is null).OrderBy(r => r.Title, comparer).ToList();
        if (loose.Count > 0) groups.Add(new CatalogCategory(null, "", loose));
        return groups;
    }

    /// <summary>The category's name in <paramref name="language"/>: its display name there when set, else its (English) name.</summary>
    public static string DisplayName(ExamCategory category, string? language)
    {
        var names = ExamAdminService.ReadDisplayNames(category.DisplayNamesJson);
        return names.TryGetValue(ExamLanguage.Normalize(language), out var n) && !string.IsNullOrWhiteSpace(n) ? n : category.Name;
    }
}
