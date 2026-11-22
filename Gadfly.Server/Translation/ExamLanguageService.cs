using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Translation;

/// <summary>One language an exam can be offered in, as the languages panel shows it.</summary>
public sealed record ExamLanguageOption(string Tag, string EnglishName, string NativeName, bool Offered, bool IsSource);

/// <summary>An exam's languages as the instructor sees them (GAD-T539).</summary>
public sealed record ExamLanguagesView(Guid ExamId, string Title, ExamStatus Status, IReadOnlyList<ExamLanguageOption> Languages, bool CanEdit)
{
    public IReadOnlyList<string> Offered => Languages.Where(l => l.Offered).Select(l => l.Tag).ToList();
}

/// <summary>The outcome of a change on the languages panel.</summary>
public sealed record ExamLanguageResult(bool Ok, string Message);

/// <summary>
/// The languages an exam is offered in (GAD-T539, under GAD-T526). An exam lists the languages a student may take it in
/// (<see cref="Exam.Languages"/>, English and Korean by default); instructors and admins turn languages off or on in the
/// assessment editor and the admin desk, and students are offered only the languages listed. English is the language the exam is
/// written in (its source language); the other languages' texts live in <see cref="ExamLocalization"/>. Changing the list
/// never touches an attempt already started: an attempt's language is fixed when it begins (<see cref="ExamSession.Language"/>).
/// Every change checks <see cref="Permission.ManageAssessments"/> and is written to the audit log in the same save.
/// </summary>
public sealed class ExamLanguageService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const string LanguagesChangedAction = "exam.languages.changed";

    private readonly AssessmentGuard guard = new(db, tenant, clock);

    /// <summary>True when the caller may change an exam's languages and texts.</summary>
    public bool CanEdit => guard.Has(Permission.ManageAssessments);

    /// <summary>The exam's offered languages, or null when it is not visible to the caller.</summary>
    public async Task<ExamLanguagesView?> GetAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments) && !guard.Has(Permission.ApproveGradingPolicyChange)) return null;
        if (tenant.OrganizationId is not { } org) return null;
        var exam = await db.Exams.AsNoTracking().Where(e => e.Id == examId && e.OrganizationId == org)
            .Select(e => new { e.Id, e.Title, e.Status, e.Languages }).FirstOrDefaultAsync(ct);
        if (exam is null) return null;
        var offered = Offered(exam.Languages);
        var options = ExamLanguage.Supported.Select(t => new ExamLanguageOption(t, ExamLanguage.EnglishName(t), ExamLanguage.NativeName(t),
            offered.Contains(t), ExamLanguage.IsEnglish(t))).ToList();
        return new ExamLanguagesView(exam.Id, exam.Title, exam.Status, options, CanEdit);
    }

    /// <summary>
    /// Sets the languages the exam is offered in. At least one language stays on; only supported languages are kept. Attempts
    /// already started keep their language.
    /// </summary>
    public async Task<ExamLanguageResult> SetOfferedAsync(Guid examId, IEnumerable<string> languages, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Changing the languages an exam is offered in", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        if (tenant.OrganizationId is not { } org) return new(false, "No active organization.");
        var wanted = languages.Select(ExamLanguage.Normalize).Where(ExamLanguage.Supported.Contains).Distinct()
            .OrderBy(t => IndexOf(t)).ToList();
        if (wanted.Count == 0) return new(false, "Keep at least one language on: students need a language to take the exam in.");
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        var before = Offered(exam.Languages);
        if (before.SequenceEqual(wanted)) return new(true, "No change.");
        exam.Languages = ExamLanguage.FormatList(wanted);
        guard.Audit(LanguagesChangedAction, "Exam", exam.Id.ToString(), new { Languages = before }, new { Languages = wanted },
            reason: $"Offered in {Names(wanted)}.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, $"Students can now take this exam in {Names(wanted)}. Attempts already started keep their language.");
    }

    /// <summary>The offered languages of a stored list, in display order (English when the list is empty).</summary>
    public static IReadOnlyList<string> Offered(string? stored) => ExamLanguage.ParseList(stored).OrderBy(IndexOf).ToList();

    /// <summary>"English and Korean".</summary>
    public static string Names(IReadOnlyList<string> tags) => tags.Count switch
    {
        0 => "no language",
        1 => ExamLanguage.EnglishName(tags[0]),
        _ => string.Join(", ", tags.Take(tags.Count - 1).Select(ExamLanguage.EnglishName)) + " and " + ExamLanguage.EnglishName(tags[^1]),
    };

    private static int IndexOf(string tag)
    {
        var i = ExamLanguage.Supported.ToList().IndexOf(tag);
        return i < 0 ? int.MaxValue : i;
    }
}
