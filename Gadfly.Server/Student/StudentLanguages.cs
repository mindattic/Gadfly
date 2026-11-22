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

    // ----------------------------------------------------------------------------------------------- voice calibration (GAD-T594)

    /// <summary>The sentence read for voice calibration in one language: a pangram, with its word and syllable counts.</summary>
    public sealed record CalibrationText(string Language, string Text, int Words, int Syllables);

    /// <summary>The calibration sentence per language: English "The quick brown fox…", Korean "다람쥐 헌 쳇바퀴에 타고파" (each Hangul block a syllable).</summary>
    public static CalibrationText CalibrationFor(string language) => ExamLanguage.Normalize(language) switch
    {
        ExamLanguage.Korean => new(ExamLanguage.Korean, "다람쥐 헌 쳇바퀴에 타고파.", 4, 11),
        _ => new(ExamLanguage.English, "The quick brown fox jumps over the lazy dog.", 9, 11),
    };

    /// <summary>The words of the sentence, allowed as recognizer hints for the calibration's grant.</summary>
    public static IReadOnlyList<string> CalibrationTerms(string language) =>
        CalibrationFor(language).Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',')).Where(w => w.Length > 0).ToList();

    /// <summary>How many times the sentence is read.</summary>
    public const int CalibrationReadings = 3;

    /// <summary>One reading's numbers, as js/voice-calibration.js measures them (no audio, no words).</summary>
    public sealed record CalibrationReading(bool Heard, double SpanMs, IReadOnlyList<double> PausesMs, double? Confidence, double Match);

    /// <summary>
    /// The readings summarized into the stored measures: speech rate from the known sentence's words and syllables over the mean
    /// reading time, mean and longest pause, and the recognizer's confidence (its own word confidences when it gives them, else how
    /// closely its words matched the sentence). Null when no reading was heard.
    /// </summary>
    public static VoiceCalibration? Summarize(string language, IReadOnlyList<CalibrationReading> readings, DateTimeOffset at)
    {
        var heard = readings.Where(r => r.Heard && r.SpanMs >= 300).ToList();
        if (heard.Count == 0) return null;
        var text = CalibrationFor(language);
        var span = heard.Average(r => r.SpanMs);
        var pauses = heard.SelectMany(r => r.PausesMs).Where(p => p > 0 && double.IsFinite(p)).ToList();
        return new VoiceCalibration
        {
            SpeechRateWordsPerMinute = Math.Round(text.Words / (span / 60000.0), 1),
            SpeechRateSyllablesPerSecond = Math.Round(text.Syllables / (span / 1000.0), 2),
            MeanPauseMs = pauses.Count == 0 ? 0 : Math.Round(pauses.Average()),
            LongestPauseMs = pauses.Count == 0 ? 0 : Math.Round(pauses.Max()),
            RecognitionConfidence = Math.Round(heard.Average(r => Math.Clamp(r.Confidence ?? r.Match, 0, 1)), 3),
            Readings = heard.Count,
            MeasuredAt = at,
        };
    }

    /// <summary>The languages this student has a calibration for (empty when none).</summary>
    public static async Task<IReadOnlyCollection<string>> CalibratedAsync(IServiceScopeFactory scopes, Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var row = await db.StudentLanguageProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);
        return row is null ? [] : row.ReadVoiceCalibration().Keys.ToList();
    }

    /// <summary>
    /// Stores a calibration for one language on the student's profile, creating the profile from <paramref name="answers"/> when
    /// there is none yet. Only the derived measures are stored.
    /// </summary>
    public static async Task SaveCalibrationAsync(IServiceScopeFactory scopes, Guid organizationId, Guid userId, LanguageAnswers answers,
        string language, VoiceCalibration calibration, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId));
        var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
        var row = await db.StudentLanguageProfiles.FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);
        if (row is null)
        {
            var (spoken, primary) = Clean(answers);
            row = new StudentLanguageProfile
            {
                OrganizationId = organizationId, UserId = userId,
                PrimaryLanguage = primary ?? ExamLanguage.Normalize(language),
                LanguagesSpoken = ExamLanguage.FormatList(spoken.Count > 0 ? spoken : [ExamLanguage.Normalize(language)]),
            };
            db.StudentLanguageProfiles.Add(row);
        }
        row.WriteVoiceCalibration(language, calibration, now);
        await db.SaveChangesAsync(ct);
    }

    private static List<string> Tags(string? list) =>
        (list ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(ExamLanguage.Normalize).Distinct().ToList();
}
