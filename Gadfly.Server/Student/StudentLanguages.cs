using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Student;

/// <summary>One language a student can say they speak (GAD-T552): its tag and its own name for itself.</summary>
public sealed record SpokenLanguage(string Tag, string NativeName);

/// <summary>A student's answers to the language questions (GAD-T552): every language they speak and the one they speak best.</summary>
public sealed record LanguageAnswers(IReadOnlyList<string> Spoken, string Primary);

/// <summary>
/// The language questions on the get-ready page (GAD-T552, GAD-T546): which languages the exam is offered in, and the student's own
/// language profile (asked, never inferred; editable; prefilled next time). The profile helps the examiner and judges treat a
/// non-native speaker fairly (GAD-T555); it is never used to grade, and nothing about nationality or origin is derived from it.
/// </summary>
public static class StudentLanguages
{
    /// <summary>
    /// The languages a student can tick, each in its own script, the exam languages first. A list, not a free-text field: a
    /// language tag is all that is stored.
    /// </summary>
    public static readonly IReadOnlyList<SpokenLanguage> Choices =
    [
        new("en", "English"), new("ko", "한국어"), new("zh", "中文"), new("ja", "日本語"), new("vi", "Tiếng Việt"),
        new("es", "Español"), new("fr", "Français"), new("de", "Deutsch"), new("pt", "Português"), new("ru", "Русский"),
        new("ar", "العربية"), new("hi", "हिन्दी"), new("id", "Bahasa Indonesia"), new("th", "ไทย"), new("tl", "Filipino"),
    ];

    /// <summary>The native name of a tag in <see cref="Choices"/>, else the tag itself.</summary>
    public static string NativeName(string tag) => Choices.FirstOrDefault(c => c.Tag == ExamLanguage.Normalize(tag))?.NativeName ?? tag;

    /// <summary>The languages the assignment's exam is offered in, in display order (English when none are listed).</summary>
    public static async Task<IReadOnlyList<string>> OfferedAsync(IServiceScopeFactory scopes, Guid organizationId, Guid assignmentId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var list = await db.ExamAssignments.AsNoTracking()
            .Where(a => a.Id == assignmentId && a.OrganizationId == organizationId)
            .Select(a => a.Exam!.Languages).FirstOrDefaultAsync(ct);
        var offered = ExamLanguage.ParseList(list);
        return ExamLanguage.Supported.Where(offered.Contains).ToList();
    }

    /// <summary>The student's saved answers in this organization, or null when they haven't answered yet.</summary>
    public static async Task<LanguageAnswers?> LoadAsync(IServiceScopeFactory scopes, Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var row = await db.StudentLanguageProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);
        return row is null ? null : new LanguageAnswers(Tags(row.LanguagesSpoken), ExamLanguage.Normalize(row.PrimaryLanguage));
    }

    /// <summary>
    /// Saves the answers (the primary language is always among the spoken ones). Unknown tags are dropped; with nothing valid left
    /// the profile is not written at all.
    /// </summary>
    public static async Task SaveAsync(IServiceScopeFactory scopes, Guid organizationId, Guid userId, LanguageAnswers answers, DateTimeOffset now, CancellationToken ct = default)
    {
        var (spoken, primary) = Clean(answers);
        if (primary is null) return;
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var row = await db.StudentLanguageProfiles.FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);
        if (row is null)
        {
            row = new StudentLanguageProfile { OrganizationId = organizationId, UserId = userId };
            db.StudentLanguageProfiles.Add(row);
        }
        row.PrimaryLanguage = primary;
        row.LanguagesSpoken = ExamLanguage.FormatList(spoken);
        row.UpdatedAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two tabs saving the first profile at once: the unique (organization, user) index keeps one; this answer is the newer, so
            // write it onto that one.
            db.ChangeTracker.Clear();
            var existing = await db.StudentLanguageProfiles.FirstAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);
            existing.PrimaryLanguage = primary;
            existing.LanguagesSpoken = ExamLanguage.FormatList(spoken);
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The answers with unknown tags removed and the primary language included in the spoken ones.</summary>
    public static (IReadOnlyList<string> Spoken, string? Primary) Clean(LanguageAnswers answers)
    {
        var known = Choices.Select(c => c.Tag).ToHashSet();
        var spoken = answers.Spoken.Select(ExamLanguage.Normalize).Where(known.Contains).Distinct().ToList();
        var primary = ExamLanguage.Normalize(answers.Primary);
        if (!known.Contains(primary)) primary = spoken.FirstOrDefault() ?? "";
        if (primary.Length == 0) return ([], null);
        if (!spoken.Contains(primary)) spoken.Insert(0, primary);
        return (Choices.Select(c => c.Tag).Where(spoken.Contains).ToList(), primary);
    }

    /// <summary>
    /// The exam language to show first: the language the student speaks best when the exam offers it, else the page's current
    /// language when offered, else English when offered, else the first offered. An English speaker on an English page gets English.
    /// </summary>
    public static string DefaultExamLanguage(IReadOnlyList<string> offered, string? primary, string pageLanguage)
    {
        if (primary is not null && offered.Contains(ExamLanguage.Normalize(primary))) return ExamLanguage.Normalize(primary);
        if (offered.Contains(ExamLanguage.Normalize(pageLanguage))) return ExamLanguage.Normalize(pageLanguage);
        return offered.Contains(ExamLanguage.English) ? ExamLanguage.English : offered.FirstOrDefault() ?? ExamLanguage.English;
    }

    private static List<string> Tags(string? list) =>
        (list ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(ExamLanguage.Normalize).Distinct().ToList();
}
