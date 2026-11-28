using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>
/// One exam on the student's list (GAD-T845): its title in the student's language, its category's name in that language
/// (empty when uncategorized) and its offered languages.
/// </summary>
public sealed record CatalogExam(Guid ExamId, string Title, Guid? CategoryId, string CategoryName, IReadOnlyList<string> Languages);

/// <summary>
/// Every published exam in the organization (GAD-T845): none is assigned to anyone, any student can take any of them, and the
/// attempt record is made when the student starts one (<see cref="StudentExams.EnsureOpenAssignmentAsync"/>). Titles are the
/// approved texts in the student's language, the English otherwise (never an unapproved machine draft, GAD-T630); category names
/// are the category's display name in that language, else its name. Ordered by the organization's categories (GAD-T656), then
/// title; uncategorized exams come last.
/// </summary>
public static class StudentCatalog
{
    public static async Task<IReadOnlyList<CatalogExam>> ListAsync(IServiceScopeFactory scopes, Guid organizationId, string? language,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();

        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.Status == ExamStatus.Published)
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
        var order = categories.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var culture = Localization.UiCatalog.CultureFor(lang);
        var comparer = StringComparer.Create(culture, ignoreCase: true);
        return exams
            .Select(e =>
            {
                var category = e.CategoryId is { } c ? categories.FirstOrDefault(x => x.Id == c) : null;
                return new CatalogExam(e.Id, titles.GetValueOrDefault(e.Id) ?? e.Title, category?.Id,
                    category is null ? "" : DisplayName(category, lang), ExamLanguage.ParseList(e.Languages));
            })
            .OrderBy(r => r.CategoryId is { } c ? order[c] : int.MaxValue)
            .ThenBy(r => r.Title, comparer)
            .ToList();
    }

    /// <summary>The category's name in <paramref name="language"/>: its display name there when set, else its (English) name.</summary>
    public static string DisplayName(ExamCategory category, string? language)
    {
        var names = ExamAdminService.ReadDisplayNames(category.DisplayNamesJson);
        return names.TryGetValue(ExamLanguage.Normalize(language), out var n) && !string.IsNullOrWhiteSpace(n) ? n : category.Name;
    }
}
