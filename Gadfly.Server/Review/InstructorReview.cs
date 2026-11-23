using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Grading;
using Gadfly.Server.Integrity;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Review;

/// <summary>One absence from the exam window, as the reviewer reads it (GAD-T511 notes, GAD-T603).</summary>
public sealed record ReviewAbsence(DateTimeOffset At, TimeSpan Length, int? Question);

/// <summary>The student's explanation of leaving the window, in their own words (never graded, never sent to the AI).</summary>
public sealed record ReviewExplanation(DateTimeOffset At, string Text);

/// <summary>Who cleared a review mark, when and why (from the audit log).</summary>
public sealed record ReviewDecision(DateTimeOffset At, string By, string Note, string How);

/// <summary>
/// Everything a reviewer needs about an attempt held for an instructor's review (GAD-T603): the stored reason, the absences from
/// the exam window with time and length, the student's explanation, and the cross-lingual calibration reason when the attempt is
/// in another language than its rubric. <see cref="DetailsHidden"/>: blind grading hides the integrity details (the absences and
/// the explanation) until the viewer's blind score is in; the hold itself and the calibration reason always show.
/// </summary>
public sealed record InstructorReviewView(
    bool Held,
    string? Reason,
    DateTimeOffset? FlaggedAt,
    IReadOnlyList<ReviewAbsence> Absences,
    IReadOnlyList<ReviewExplanation> Explanations,
    string? CalibrationReason,
    int UncalibratedObjectives,
    string? WindowRule,
    bool DetailsHidden,
    ReviewDecision? LastDecision)
{
    /// <summary>Anything worth showing: held now, or held before and decided.</summary>
    public bool Any => Held || LastDecision is not null;
    public bool WindowAbsences => Absences.Count > 0;
}

/// <summary>
/// Attempts marked <see cref="ExamSession.NeedsInstructorReview"/> (GAD-T511 window absences, GAD-T536 cross-lingual scores; GAD-T603):
/// what a reviewer sees, clearing the mark with a required note (audited), and the hold on results. While an attempt is held its
/// result never reaches the student: Publish is refused, and the student's results, transcript copy, certificate and LMS grade
/// passback (all through <see cref="Transcripts.TranscriptAccess.PublishedToStudentAsync"/>) treat it as unpublished. A person
/// decides: by clearing the mark here, or by approving the final grade with a note (GradingService.ApproveAsync), which clears it too.
/// </summary>
public static class InstructorReview
{
    public const string ClearedAction = "review.flag.cleared";
    public const string DecidedByApprovalAction = "review.flag.decided-by-approval";
    public const int MaxNote = 2000;

    /// <summary>The badge text, everywhere.</summary>
    public const string Badge = "Needs instructor review";

    /// <summary>Who may clear the mark: the roles that approve final grades (Instructor, Admin, Owner).</summary>
    public static bool CanDecide(string? role) => PermissionMatrix.Has(role, Permission.ApproveFinalGrade);

    /// <summary>A short reason for a list: what kind of hold this is.</summary>
    public static string ShortReason(string? reason, string? language) =>
        reason == Sessions.SessionOrchestrator.CrossLingualReviewReason
            ? $"{Gadfly.Engine.ExamLanguage.EnglishName(language)} attempt: AI scores not yet calibrated"
            : reason is { } r && r.StartsWith("The exam window was left", StringComparison.Ordinal) ? "left the exam window"
            : "flagged for review";

    /// <summary>The held attempts among <paramref name="sessionIds"/>.</summary>
    public static async Task<HashSet<Guid>> HeldAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct) =>
        sessionIds.Count == 0 ? [] : (await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && sessionIds.Contains(s.Id) && s.NeedsInstructorReview)
            .Select(s => s.Id).ToListAsync(ct)).ToHashSet();

    public static Task<bool> IsHeldAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, CancellationToken ct) =>
        db.ExamSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.OrganizationId == organizationId && s.NeedsInstructorReview, ct);

    /// <summary>The review view of one attempt, or null when it isn't in this organization.</summary>
    public static async Task<InstructorReviewView?> LoadAsync(GadflyDbContext db, Guid organizationId, Guid sessionId, bool hideIntegrity, CancellationToken ct)
    {
        var s = await db.ExamSessions.AsNoTracking().Where(x => x.Id == sessionId && x.OrganizationId == organizationId)
            .Select(x => new { x.NeedsInstructorReview, x.ReviewReason, x.ReviewFlaggedAt, x.Language, x.AssignmentId }).FirstOrDefaultAsync(ct);
        if (s is null) return null;

        var notes = await db.IntegrityEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.OrganizationId == organizationId
                        && ((e.Source == WindowFocusRecorder.Source && e.Type == WindowFocusRecorder.AbsenceType)
                            || (e.Source == WindowFocusRecorder.NoteSource && e.Type == WindowFocusRecorder.NoteType)))
            .OrderBy(e => e.StartedAt).Select(e => new { e.Type, e.StartedAt, e.EndedAt, e.TurnId, e.Summary }).ToListAsync(ct);
        var turnIds = notes.Where(n => n.TurnId is not null).Select(n => n.TurnId!.Value).Distinct().ToList();
        var turnIndex = turnIds.Count == 0 ? [] : await db.SessionTurns.AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && turnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Index, ct);
        var absences = notes.Where(n => n.Type == WindowFocusRecorder.AbsenceType)
            .Select(n => new ReviewAbsence(n.StartedAt, (n.EndedAt ?? n.StartedAt) - n.StartedAt,
                n.TurnId is { } t && turnIndex.TryGetValue(t, out var i) ? i + 1 : null)).ToList();
        var explanations = notes.Where(n => n.Type == WindowFocusRecorder.NoteType)
            .Select(n => new ReviewExplanation(n.StartedAt, Explanation(n.Summary))).ToList();

        var crossLingual = !Gadfly.Engine.ExamLanguage.IsEnglish(s.Language);
        var uncalibrated = await db.AiObjectiveGrades.AsNoTracking()
            .CountAsync(g => g.SessionId == sessionId && g.OrganizationId == organizationId
                             && g.ReviewFlagsJson.Contains(Gadfly.Sources.Grading.ReviewFlags.CrossLingual), ct);
        var rule = absences.Count > 0 ? (await WindowFocusRecorder.RuleAsync(db, organizationId, s.AssignmentId, ct)).Describe() : null;

        var decided = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.SessionId == sessionId && (e.Action == ClearedAction || e.Action == DecidedByApprovalAction))
            .OrderByDescending(e => e.At).Select(e => new { e.At, e.ActorUserId, e.Reason, e.Action }).FirstOrDefaultAsync(ct);
        ReviewDecision? last = null;
        if (decided is not null)
        {
            var by = decided.ActorUserId is { } u
                ? await db.AuthUsers.AsNoTracking().Where(x => x.Id == u).Select(x => x.DisplayName ?? x.UserName).FirstOrDefaultAsync(ct)
                : null;
            last = new ReviewDecision(decided.At, by ?? "staff", decided.Reason ?? "",
                decided.Action == ClearedAction ? "cleared the review mark" : "approved the final grade");
        }

        return new InstructorReviewView(s.NeedsInstructorReview, s.ReviewReason, s.ReviewFlaggedAt,
            hideIntegrity ? [] : absences, hideIntegrity ? [] : explanations,
            crossLingual || uncalibrated > 0 ? Sessions.SessionOrchestrator.CrossLingualReviewReason : null, uncalibrated,
            hideIntegrity ? null : rule, hideIntegrity && (absences.Count > 0 || explanations.Count > 0), last);
    }

    /// <summary>
    /// Clears the review mark with a required note (GAD-T603), as staff who approve final grades. Audited with the reason that was
    /// set and the note. The result is then released through the normal approval and publication flow.
    /// </summary>
    public static async Task<(bool Ok, string Message)> ClearAsync(GadflyDbContext db, Guid organizationId, Guid? actorUserId, string? actorRole,
        Guid sessionId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var actor = new GradingActor(actorUserId, actorRole);
        if (!CanDecide(actorRole))
        {
            await GradingAudit.DeniedAsync(db, organizationId, actor, Permission.ApproveFinalGrade, "Clearing an instructor-review mark", sessionId, now, ct);
            return (false, "Only staff who approve final grades can clear a review mark.");
        }
        var said = (note ?? "").Trim();
        if (said.Length == 0) return (false, "Write a note: what you looked at and why the attempt stands.");
        if (said.Length > MaxNote) return (false, $"Keep the note under {MaxNote:N0} characters.");
        var session = await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.OrganizationId == organizationId, ct);
        if (session is null) return (false, "That attempt was not found in this organization.");
        if (!session.NeedsInstructorReview) return (false, "This attempt isn't marked for review.");
        Decide(db, organizationId, actor, session, ClearedAction, said, now);
        await db.SaveChangesAsync(ct);
        return (true, "Review mark cleared. The result can now be approved and published as usual.");
    }

    /// <summary>Clears the mark on a tracked session and audits who decided, how and why (saved by the caller).</summary>
    internal static void Decide(GadflyDbContext db, Guid organizationId, GradingActor actor, ExamSession session, string action, string note, DateTimeOffset now)
    {
        var before = new { session.NeedsInstructorReview, session.ReviewReason, session.ReviewFlaggedAt };
        session.NeedsInstructorReview = false;   // the reason stays on the attempt, for the record
        GradingAudit.Add(db, organizationId, actor, action, "ExamSession", session.Id.ToString(), session.Id, now,
            before: before, after: new { NeedsInstructorReview = false }, reason: note);
    }

    /// <summary>The student's words from a stored explanation note.</summary>
    private static string Explanation(string summary)
    {
        var start = summary.IndexOf('"');
        return start >= 0 && summary.EndsWith('"') && summary.Length > start + 1 ? summary[(start + 1)..^1] : summary;
    }

    public static string Length(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(1, (int)Math.Round(t.TotalSeconds))} s";
}
