using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Assessments;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Translation;

/// <summary>One language an exam can be offered in, as the languages panel shows it.</summary>
public sealed record ExamLanguageOption(string Tag, string EnglishName, string NativeName, bool Offered, bool IsSource);

/// <summary>An exam's languages as the instructor sees them (GAD-T539).</summary>
public sealed record ExamLanguagesView(Guid ExamId, string Title, ExamStatus Status, IReadOnlyList<ExamLanguageOption> Languages, bool CanEdit)
{
    public IReadOnlyList<string> Offered => Languages.Where(l => l.Offered).Select(l => l.Tag).ToList();
}

/// <summary>One learning objective's title in the exam's source language.</summary>
public sealed record SourceObjective(string Key, string Title);

/// <summary>One language's texts (GAD-T540): what a student taking the exam in that language reads.</summary>
public sealed record ExamTextsView(string Language, bool Offered, string Title, string? Instructions, IReadOnlyDictionary<string, string> ObjectiveTitles,
    ExamLocalizationStatus? Status, DateTimeOffset? UpdatedAt, DateTimeOffset? ApprovedAt, IReadOnlyList<string> Missing)
{
    /// <summary>No texts exist yet for this language: students choosing it read the English texts.</summary>
    public bool Empty => Status is null;
}

/// <summary>The exam's source texts and every other supported language's texts.</summary>
public sealed record ExamLocalizationsView(Guid ExamId, string Title, string? Instructions, IReadOnlyList<SourceObjective> Objectives,
    IReadOnlyList<ExamTextsView> Languages, bool CanEdit, bool MockTranslation);

/// <summary>The texts an instructor saves for one language.</summary>
public sealed class ExamTextsForm
{
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public Dictionary<string, string> ObjectiveTitles { get; set; } = new(StringComparer.Ordinal);
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
public sealed class ExamLanguageService(GadflyDbContext db, ITenantContext tenant, TimeProvider clock, IPanelInfo panel, ILiveTurnModel model, UsageMeter meter)
{
    public const string LanguagesChangedAction = "exam.languages.changed";
    public const string DraftedAction = "exam.localization.drafted";
    public const string SavedAction = "exam.localization.saved";
    public const string ApprovedAction = "exam.localization.approved";
    public const string Entity = "ExamLocalization";
    public const int MaxTitle = 200, MaxObjectiveTitle = 300, MaxInstructions = 4000;
    private static readonly JsonSerializerOptions Json = new();

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

    // ================================================================== localized texts (GAD-T540)

    /// <summary>The exam's English texts and each other supported language's texts, or null when the exam is not visible.</summary>
    public async Task<ExamLocalizationsView?> TextsAsync(Guid examId, CancellationToken ct = default)
    {
        if (!guard.Has(Permission.ManageAssessments) && !guard.Has(Permission.ApproveGradingPolicyChange)) return null;
        if (tenant.OrganizationId is not { } org) return null;
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return null;
        var objectives = await SourceObjectivesAsync(org, examId, ct);
        var rows = await db.ExamLocalizations.AsNoTracking().Where(l => l.ExamId == examId && l.OrganizationId == org).ToListAsync(ct);
        var offered = Offered(exam.Languages);
        var views = ExamLanguage.Supported.Where(t => !ExamLanguage.IsEnglish(t)).Select(t =>
        {
            var row = rows.FirstOrDefault(r => ExamLanguage.Normalize(r.Language) == t);
            var titles = row is null ? new Dictionary<string, string>(StringComparer.Ordinal) : ReadTitles(row.ObjectiveTitlesJson);
            return new ExamTextsView(t, offered.Contains(t), row?.Title ?? "", row?.Instructions, titles, row?.Status, row?.UpdatedAt, row?.ApprovedAt,
                Missing(exam.Instructions, objectives, row, titles));
        }).ToList();
        return new ExamLocalizationsView(exam.Id, exam.Title, exam.Instructions, objectives, views, CanEdit, panel.IsMock);
    }

    /// <summary>
    /// Writes a machine-translated draft of the exam's title, instructions and objective titles in <paramref name="language"/>,
    /// from the English texts, as status <see cref="ExamLocalizationStatus.MachineDraft"/>. Replacing approved texts needs
    /// <paramref name="replaceApproved"/>. Live, the fast model translates (metered); in mock mode the draft is a labelled mock.
    /// </summary>
    public async Task<ExamLanguageResult> DraftAsync(Guid examId, string language, bool replaceApproved = false, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, "Drafting an exam's texts in another language", "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        if (tenant.OrganizationId is not { } org) return new(false, "No active organization.");
        var lang = ExamLanguage.Normalize(language);
        if (!ExamLanguage.IsSupported(lang) || ExamLanguage.IsEnglish(lang)) return new(false, "Choose a language other than English that Gadfly supports.");
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        var row = await db.ExamLocalizations.FirstOrDefaultAsync(l => l.ExamId == examId && l.OrganizationId == org && l.Language == lang, ct);
        if (row is { Status: ExamLocalizationStatus.Approved } && !replaceApproved)
            return new(false, $"The {ExamLanguage.EnglishName(lang)} texts are approved. Confirm to replace them with a new machine draft.");
        var objectives = await SourceObjectivesAsync(org, examId, ct);

        var translations = new TranslationService(db, tenant, panel, model, meter);
        var batch = await translations.TranslateAsync([exam.Title, exam.Instructions, .. objectives.Select(o => o.Title)], ExamLanguage.English, lang,
            "exam-texts", ct: ct);
        if (batch.Error is not null) return new(false, batch.Error);
        string T(string? s) => batch.Of(s)?.Text ?? s ?? "";

        var now = clock.GetUtcNow();
        var before = row is null ? null : Snapshot(row);
        if (row is null)
        {
            row = new ExamLocalization { OrganizationId = org, ExamId = examId, Language = lang };
            db.ExamLocalizations.Add(row);
        }
        row.Title = Cap(T(exam.Title), MaxTitle);
        row.Instructions = string.IsNullOrWhiteSpace(exam.Instructions) ? null : Cap(T(exam.Instructions), MaxInstructions);
        row.ObjectiveTitlesJson = JsonSerializer.Serialize(objectives.ToDictionary(o => o.Key, o => Cap(T(o.Title), MaxObjectiveTitle)), Json);
        row.Status = ExamLocalizationStatus.MachineDraft;
        row.UpdatedByUserId = tenant.UserId;
        row.ApprovedByUserId = null;
        row.ApprovedAt = null;
        row.UpdatedAt = now;
        var by = batch.ByOriginal.Values.FirstOrDefault(v => v.Model.Length > 0)?.Model ?? "the fast model";
        guard.Audit(DraftedAction, Entity, examId.ToString(), before, Snapshot(row),
            reason: batch.Mock ? $"Mock machine draft in {ExamLanguage.EnglishName(lang)} (mock mode: no model was called)."
                : $"Machine draft in {ExamLanguage.EnglishName(lang)} by {by}; awaiting review.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new(true, batch.Mock
            ? $"Mock {ExamLanguage.EnglishName(lang)} draft written (mock mode calls no model: each text is the English behind a marker). Edit it, then approve."
            : $"Machine-translated {ExamLanguage.EnglishName(lang)} draft written. Check every text, edit what reads wrongly, then approve.");
    }

    /// <summary>
    /// Saves an instructor's texts for <paramref name="language"/>; with <paramref name="approve"/> they become the approved texts
    /// students read. A save without approval keeps the current status.
    /// </summary>
    public async Task<ExamLanguageResult> SaveTextsAsync(Guid examId, string language, ExamTextsForm form, bool approve, CancellationToken ct = default)
    {
        if (!await guard.RequireAsync(Permission.ManageAssessments, approve ? "Approving an exam's texts in another language" : "Editing an exam's texts in another language",
                "Exam", examId.ToString(), ct))
            return new(false, AssessmentGuard.Denied(Permission.ManageAssessments));
        if (tenant.OrganizationId is not { } org) return new(false, "No active organization.");
        var lang = ExamLanguage.Normalize(language);
        if (!ExamLanguage.IsSupported(lang) || ExamLanguage.IsEnglish(lang)) return new(false, "Choose a language other than English that Gadfly supports.");
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && e.OrganizationId == org, ct);
        if (exam is null) return new(false, "That exam no longer exists.");
        var objectives = await SourceObjectivesAsync(org, examId, ct);

        var title = form.Title?.Trim() ?? "";
        var instructions = string.IsNullOrWhiteSpace(form.Instructions) ? null : form.Instructions.Trim();
        if (title.Length == 0) return new(false, "Give the exam a title in this language.");
        if (title.Length > MaxTitle) return new(false, $"Keep the title under {MaxTitle} characters.");
        if (instructions?.Length > MaxInstructions) return new(false, $"Keep the instructions under {MaxInstructions} characters.");
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in objectives)
        {
            var t = form.ObjectiveTitles.GetValueOrDefault(o.Key)?.Trim() ?? "";
            if (t.Length > MaxObjectiveTitle) return new(false, $"Keep the title of \"{o.Title}\" under {MaxObjectiveTitle} characters.");
            if (t.Length > 0) titles[o.Key] = t;
        }
        if (approve)
        {
            var gaps = new List<string>();
            if (!string.IsNullOrWhiteSpace(exam.Instructions) && instructions is null) gaps.Add("the instructions");
            gaps.AddRange(objectives.Where(o => !titles.ContainsKey(o.Key)).Select(o => $"\"{o.Title}\""));
            if (gaps.Count > 0) return new(false, $"Translate {string.Join(", ", gaps)} before approving.");
        }

        var row = await db.ExamLocalizations.FirstOrDefaultAsync(l => l.ExamId == examId && l.OrganizationId == org && l.Language == lang, ct);
        var before = row is null ? null : Snapshot(row);
        if (row is null)
        {
            row = new ExamLocalization { OrganizationId = org, ExamId = examId, Language = lang, Status = ExamLocalizationStatus.MachineDraft };
            db.ExamLocalizations.Add(row);
        }
        var now = clock.GetUtcNow();
        row.Title = title;
        row.Instructions = instructions;
        row.ObjectiveTitlesJson = JsonSerializer.Serialize(titles, Json);
        row.UpdatedByUserId = tenant.UserId;
        row.UpdatedAt = now;
        if (approve)
        {
            row.Status = ExamLocalizationStatus.Approved;
            row.ApprovedByUserId = tenant.UserId;
            row.ApprovedAt = now;
        }
        guard.Audit(approve ? ApprovedAction : SavedAction, Entity, examId.ToString(), before, Snapshot(row),
            reason: approve ? $"{ExamLanguage.EnglishName(lang)} texts approved for students." : $"{ExamLanguage.EnglishName(lang)} texts edited.");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var name = ExamLanguage.EnglishName(lang);
        return new(true, approve ? $"{name} texts approved: students taking the exam in {name} read them."
            : row.Status == ExamLocalizationStatus.Approved ? $"{name} texts saved; they stay approved."
            : $"{name} draft saved. Approve it once it reads right.");
    }

    /// <summary>The objective titles stored for one language, by objective key (an unreadable value reads as none).</summary>
    public static Dictionary<string, string> ReadTitles(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrWhiteSpace(json) ? "{}" : json) is { } d
                ? new Dictionary<string, string>(d.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException) { return new Dictionary<string, string>(StringComparer.Ordinal); }
    }

    private async Task<IReadOnlyList<SourceObjective>> SourceObjectivesAsync(Guid org, Guid examId, CancellationToken ct) =>
        (await db.LearningObjectives.AsNoTracking().Where(o => o.ExamId == examId && o.OrganizationId == org).OrderBy(o => o.Ordinal)
            .Select(o => new { o.Key, o.Title }).ToListAsync(ct)).Select(o => new SourceObjective(o.Key, o.Title)).ToList();

    private static IReadOnlyList<string> Missing(string? instructions, IReadOnlyList<SourceObjective> objectives, ExamLocalization? row,
        IReadOnlyDictionary<string, string> titles)
    {
        if (row is null) return [];
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(row.Title)) missing.Add("the title");
        if (!string.IsNullOrWhiteSpace(instructions) && string.IsNullOrWhiteSpace(row.Instructions)) missing.Add("the instructions");
        missing.AddRange(objectives.Where(o => !titles.ContainsKey(o.Key)).Select(o => $"objective \"{o.Title}\""));
        return missing;
    }

    private static object Snapshot(ExamLocalization l) => new
    {
        l.Language, l.Title, l.Instructions, ObjectiveTitles = ReadTitles(l.ObjectiveTitlesJson), Status = l.Status.ToString(),
    };

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

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
