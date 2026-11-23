using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Localization;
using Gadfly.Server.Review;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Integrity;

/// <summary>One integrity note as the student sees it: what was noted, when, during which question, and how to read it.</summary>
public sealed record StudentIntegrityNote(
    string Label,
    string Explanation,
    string Summary,
    string When,
    int? Question,
    bool Normal,
    IntegritySeverity Severity);

/// <summary>A brief, ordinary kind of note that was noticed and not counted, with how often.</summary>
public sealed record StudentIntegrityOrdinary(string Label, int Count);

/// <summary>Everything the student may see about the integrity notes recorded on one attempt.</summary>
public sealed record StudentIntegrityView(IReadOnlyList<StudentIntegrityNote> Notes, IReadOnlyList<StudentIntegrityOrdinary> Ordinary)
{
    public bool IsEmpty => Notes.Count == 0 && Ordinary.Count == 0;
}

/// <summary>
/// The student's own view of the integrity context recorded on their attempt (GAD-T450; ElenchusAtScale Section 5.9: the people a
/// grade affects can see how it was reached and contest it). Reviewers read these notes; until now the student could answer them
/// only by guessing, through an appeal. Here they see the same notes with the same plain explanations reviewers get
/// (<see cref="IntegrityExplanations"/>), and can respond in the appeal on the result page.
/// <para>
/// When: only once the result is published to them (the same rule that shows them the grade), so nothing appears while anyone is
/// still grading. Who: only the student whose attempt it is, inside their own organization. It reads integrity notes and writes
/// nothing; a blind grader is unaffected, because the grader's views read their own rule (<see cref="Grading.BlindGrading"/>), and
/// a student's response arrives only in an appeal, which exists only on a published grade, after every blind score is in.
/// </para>
/// The browser's raw codes (<see cref="IntegrityEvent.DataJson"/>) and confidence numbers are not shown; the note's own wording,
/// written by the server, is.
/// </summary>
public static class StudentIntegrityRecord
{
    /// <summary>
    /// For the signed-in student (the caller's own user and organization, never a parameter): null when there is no caller, the
    /// attempt isn't theirs, or its result isn't published to them yet.
    /// </summary>
    public static async Task<StudentIntegrityView?> LoadForCallerAsync(IServiceScopeFactory scopes, ITenantContext tenant, Guid sessionId,
        CancellationToken ct = default, UiText? text = null)
    {
        if (tenant.IsSystem || tenant.UserId is not { } userId || tenant.OrganizationId is not { } orgId) return null;
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await LoadAsync(scope.ServiceProvider.GetRequiredService<GadflyDbContext>(), orgId, userId, sessionId, ct, text);
    }

    /// <summary>Null when the attempt isn't the student's, or its result isn't published to them yet.</summary>
    /// <param name="text">The student's language (GAD-T606): labels, explanations and times worded in it; null or English as before.</param>
    public static async Task<StudentIntegrityView?> LoadAsync(GadflyDbContext db, Guid organizationId, Guid studentUserId, Guid sessionId,
        CancellationToken ct = default, UiText? text = null)
    {
        var session = await db.ExamSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.OrganizationId == organizationId && s.StudentUserId == studentUserId)
            .Select(s => new { s.StartedAt, s.CreatedAt })
            .FirstOrDefaultAsync(ct);
        if (session is null) return null;
        var status = await db.FinalGrades.AsNoTracking()
            .Where(g => g.SessionId == sessionId && g.OrganizationId == organizationId)
            .Select(g => (GradeStatus?)g.Status).FirstOrDefaultAsync(ct);
        if (status is not { } st || !GradeStatusMachine.IsVisibleToStudent(st)) return null;

        var events = await db.IntegrityEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.OrganizationId == organizationId)
            .OrderBy(e => e.StartedAt).ToListAsync(ct);
        var turnIds = events.Where(e => e.TurnId is not null).Select(e => e.TurnId!.Value).Distinct().ToList();
        var questions = turnIds.Count == 0 ? new Dictionary<Guid, int>()
            : await db.SessionTurns.AsNoTracking().Where(t => t.OrganizationId == organizationId && turnIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Index + 1, ct);
        return Build(events, questions, session.StartedAt ?? session.CreatedAt, text);
    }

    /// <summary>Shapes stored notes for the student: ordinary, uncounted ones are only counted; the rest listed in time order.</summary>
    public static StudentIntegrityView Build(IReadOnlyList<IntegrityEvent> events, IReadOnlyDictionary<Guid, int> questionOfTurn, DateTimeOffset start,
        UiText? text = null)
    {
        var notes = new List<StudentIntegrityNote>();
        var ordinary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in events.OrderBy(e => e.StartedAt))
        {
            var (label, explanation, normal) = IntegrityExplanations.For(e.Type, e.Source);
            if (text is { IsEnglish: false } lang)
            {
                label = Translated(lang, $"integrityNote.{e.Type}.label", label);
                explanation = Translated(lang, $"integrityNote.{e.Type}.explanation", explanation);
            }
            // A brief, ordinary note at the lowest level is noticed and never counted (the reviewer sees it the same way).
            if (normal && e.Severity == IntegritySeverity.Info)
            {
                ordinary[label] = ordinary.GetValueOrDefault(label) + 1;
                continue;
            }
            int? question = e.TurnId is { } t && questionOfTurn.TryGetValue(t, out var q) ? q : null;
            notes.Add(new StudentIntegrityNote(label, explanation, e.Summary, When(e, start, text), question, normal, e.Severity));
        }
        return new StudentIntegrityView(notes, ordinary.Select(kv => new StudentIntegrityOrdinary(kv.Key, kv.Value)).ToList());
    }

    /// <summary>The translated text, or <paramref name="english"/> when the language has none for this note type.</summary>
    private static string Translated(UiText t, string key, string english) => t[key] is { ResourceNotFound: false } s ? s.Value : english;

    /// <summary>"4 min 05 s into the exam, for 12 s", from the exam's start (never negative); in the student's language when given.</summary>
    public static string When(IntegrityEvent e, DateTimeOffset start, UiText? language = null)
    {
        if (language is { IsEnglish: false } t)
        {
            var from = e.StartedAt - start;
            if (from < TimeSpan.Zero) from = TimeSpan.Zero;
            var said = t["integrityNote.when.at", (int)from.TotalMinutes, from.Seconds.ToString("00")].Value;
            if (e.EndedAt is { } until && until > e.StartedAt)
            {
                var lasted = until - e.StartedAt;
                said += lasted.TotalMinutes >= 1
                    ? t["integrityNote.when.forMinutes", (int)lasted.TotalMinutes, lasted.Seconds.ToString("00")].Value
                    : t["integrityNote.when.forSeconds", Math.Max(1, (int)Math.Round(lasted.TotalSeconds))].Value;
            }
            return said;
        }
        var at = e.StartedAt - start;
        if (at < TimeSpan.Zero) at = TimeSpan.Zero;
        var text = $"{(int)at.TotalMinutes} min {at.Seconds:00} s into the exam";
        if (e.EndedAt is { } end && end > e.StartedAt)
        {
            var span = end - e.StartedAt;
            text += span.TotalMinutes >= 1 ? $", for {(int)span.TotalMinutes} min {span.Seconds:00} s" : $", for {Math.Max(1, (int)Math.Round(span.TotalSeconds))} s";
        }
        return text;
    }

    /// <summary>
    /// The appeal text when the student also responds to the integrity notes: their response follows their reason under a plain
    /// heading, so the teacher reviewing the appeal reads both. Either part may be empty, not both.
    /// </summary>
    public static string AppealText(string? reason, string? integrityResponse)
    {
        var r = (reason ?? "").Trim();
        var i = (integrityResponse ?? "").Trim();
        if (i.Length == 0) return r;
        var response = ResponseHeading + "\n" + i;
        return r.Length == 0 ? response : r + "\n\n" + response;
    }

    /// <summary>The heading a response to the integrity notes carries inside the appeal.</summary>
    public const string ResponseHeading = "About the integrity notes on this attempt:";
}
