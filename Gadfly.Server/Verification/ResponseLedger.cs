using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Verification;

/// <summary>What the client needs to show a pending verification request (one at a time).</summary>
/// <param name="Kind">"transcript" (Part 4.3 Type A: is this what you said?) or "meaning" (Type B: what did you mean?).</param>
/// <param name="Prompt">The request as the student reads it.</param>
/// <param name="RecordedText">The answer as currently recorded (the latest transcript version).</param>
public sealed record PendingVerification(Guid ResponseId, string Kind, string Prompt, string RecordedText);

/// <summary>The student's last graded answer, for the typo-fix and change-my-answer controls.</summary>
public sealed record LastAnswer(Guid ResponseId, int TurnIndex, string Question, string Text, int Versions,
    bool CanCorrectTypo, bool CanRevise, int Revisions, string RevisionRule);

/// <summary>
/// Response versioning (GAD-T86, master plan Parts 4.2, 4.6, 4.7): every submitted answer is an
/// immutable <see cref="StudentResponse"/> with transcript version 1; transcription corrections add
/// versions and update <see cref="StudentResponse.VerifiedText"/>; meaning clarifications are stored
/// beside the original (a <see cref="ClarificationEvent"/> reply plus a MeaningClarification version),
/// never over it; substantive revisions are new response rows pointing at what they revise. Every
/// state change goes through <see cref="VerificationMachine"/> and is saved as a
/// <see cref="ResponseVerification"/> row plus an <see cref="AuditEvent"/>.
/// One instance per DbContext scope; callers save.
/// </summary>
public sealed class ResponseLedger(GadflyDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Revisions allowed per question, so "change my answer" can't become an endless retry loop.</summary>
    public const int MaxRevisionsPerTurn = 2;

    public const string TranscriptKind = "transcript", MeaningKind = "meaning";

    /// <summary>The answer's capture details, kept in the capture audit event so a pending answer survives a restart.</summary>
    public sealed record CapturedAnswer(DateTimeOffset? StartedAt, DateTimeOffset CompletedAt, IReadOnlyList<TimedWord> Words,
        IReadOnlyDictionary<string, double> SpeechMetrics);

    /// <summary>Records a submitted answer: the immutable original, transcript version 1, state Captured.</summary>
    public async Task<StudentResponse> CaptureAsync(Guid org, Guid sessionId, Guid turnId, Guid studentUserId, Answer answer,
        ResponseKind kind = ResponseKind.Original, Guid? revises = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var voice = answer.Mode == "voice";
        var sequence = (await db.StudentResponses.Where(r => r.SessionId == sessionId).MaxAsync(r => (int?)r.Sequence, ct) ?? 0) + 1;
        var response = new StudentResponse
        {
            OrganizationId = org,
            SessionId = sessionId,
            TurnId = turnId,
            Sequence = sequence,
            Kind = kind,
            RevisesResponseId = revises,
            Modality = voice ? "voice" : "text",
            OriginalText = answer.Text,
            VerifiedText = answer.Text,
            SpeechRecognitionText = voice ? answer.Text : null,
            SpeechRecognitionConfidence = voice && answer.Words.Count > 0 ? Math.Round(answer.Words.Average(w => w.Confidence), 4) : null,
            VerificationState = VerificationState.Captured,
            SubmittedAt = answer.CompletedAt,
        };
        response.Versions.Add(new TranscriptVersion
        {
            OrganizationId = org, ResponseId = response.Id, Version = 1, Text = answer.Text, CorrectionKind = null,
            Source = voice ? "speech-recognition" : "student", CreatedByUserId = voice ? null : studentUserId, CreatedAt = now,
        });
        db.StudentResponses.Add(response);
        Audit(org, studentUserId, kind == ResponseKind.Original ? "response.captured" : "response.revised", response, sessionId,
            before: null,
            after: JsonSerializer.Serialize(new
            {
                response.Sequence, response.Modality, kind = kind.ToString(), revises,
                capture = new CapturedAnswer(answer.StartedAt, answer.CompletedAt, answer.Words, answer.SpeechMetrics),
            }, Json),
            reason: kind == ResponseKind.Original ? null : "The student chose to change their answer.");
        return response;
    }

    /// <summary>Moves a response's verification state (refusing invalid transitions), with its history row and audit event.</summary>
    public void Move(StudentResponse response, VerificationState to, string reason, Guid? actorUserId, string? actorRole)
    {
        var from = response.VerificationState;
        response.VerificationState = VerificationMachine.Move(from, to);
        var now = clock.GetUtcNow();
        db.ResponseVerifications.Add(new ResponseVerification
        {
            OrganizationId = response.OrganizationId, ResponseId = response.Id, From = from, To = to,
            Reason = Clip(reason, 500), ActorUserId = actorUserId, At = now,
        });
        Audit(response.OrganizationId, actorUserId, "response.verification.changed", response, response.SessionId,
            JsonSerializer.Serialize(new { state = from.ToString() }), JsonSerializer.Serialize(new { state = to.ToString() }), reason, actorRole);
    }

    /// <summary>Moves through several states in order (e.g. Captured → VerificationRequired → AwaitingStudentConfirmation).</summary>
    public void MovePath(StudentResponse response, string reason, Guid? actor, string? role, params VerificationState[] path)
    {
        foreach (var to in path) Move(response, to, reason, actor, role);
    }

    /// <summary>
    /// Adds a transcript version. A Transcription correction also becomes the verified text (no score
    /// penalty; the original is untouched); a MeaningClarification version is stored beside it only.
    /// </summary>
    public TranscriptVersion AddVersion(StudentResponse response, string text, CorrectionKind kind, string source, Guid? actorUserId)
    {
        var version = new TranscriptVersion
        {
            OrganizationId = response.OrganizationId, ResponseId = response.Id,
            Version = (response.Versions.Count == 0 ? 1 : response.Versions.Max(v => v.Version)) + 1,
            Text = text, CorrectionKind = kind, Source = source, CreatedByUserId = actorUserId, CreatedAt = clock.GetUtcNow(),
        };
        response.Versions.Add(version);
        db.TranscriptVersions.Add(version);
        var before = response.VerifiedText;
        if (kind == CorrectionKind.Transcription) response.VerifiedText = text;
        Audit(response.OrganizationId, actorUserId, kind == CorrectionKind.Transcription ? "response.corrected" : "response.clarified",
            response, response.SessionId,
            JsonSerializer.Serialize(new { verifiedText = before }),
            JsonSerializer.Serialize(new { version = version.Version, kind = kind.ToString(), source, text }),
            kind == CorrectionKind.Transcription ? "Transcription correction: the verified text changes, the original is kept." : "Meaning clarification stored beside the answer.",
            actorUserId is null ? null : OrgRoles.Student);
        return version;
    }

    public ClarificationEvent Ask(StudentResponse response, ClarificationType type, string question, bool nonLeadingChecked, string reason)
    {
        var ev = new ClarificationEvent
        {
            OrganizationId = response.OrganizationId, SessionId = response.SessionId, ResponseId = response.Id,
            Type = type, Question = question, NonLeadingChecked = nonLeadingChecked, AskedAt = clock.GetUtcNow(),
        };
        db.ClarificationEvents.Add(ev);
        Audit(response.OrganizationId, null, "clarification.asked", response, response.SessionId, null,
            JsonSerializer.Serialize(new { clarificationId = ev.Id, type = type.ToString(), question, nonLeadingChecked }), reason, "System");
        return ev;
    }

    public void Answer(ClarificationEvent ev, StudentResponse response, string? reply, Guid studentUserId, string note)
    {
        ev.StudentReply = reply;
        ev.AnsweredAt = clock.GetUtcNow();
        Audit(response.OrganizationId, studentUserId, "clarification.answered", response, response.SessionId, null,
            JsonSerializer.Serialize(new { clarificationId = ev.Id, reply }), note, OrgRoles.Student);
    }

    /// <summary>The response on <paramref name="turnId"/> waiting for the student, with its open request.</summary>
    public async Task<(StudentResponse Response, ClarificationEvent? Request)?> PendingAsync(Guid sessionId, Guid turnId, CancellationToken ct)
    {
        var response = await db.StudentResponses.Include(r => r.Versions)
            .Where(r => r.SessionId == sessionId && r.TurnId == turnId && r.Kind == ResponseKind.Original
                && r.VerificationState == VerificationState.AwaitingStudentConfirmation)
            .OrderByDescending(r => r.Sequence).FirstOrDefaultAsync(ct);
        if (response is null) return null;
        var request = await db.ClarificationEvents.Where(c => c.ResponseId == response.Id && c.AnsweredAt == null)
            .OrderByDescending(c => c.AskedAt).FirstOrDefaultAsync(ct);
        return (response, request);
    }

    public Task<StudentResponse?> OriginalForTurnAsync(Guid sessionId, Guid turnId, CancellationToken ct) =>
        db.StudentResponses.Include(r => r.Versions)
            .Where(r => r.SessionId == sessionId && r.TurnId == turnId && r.Kind == ResponseKind.Original)
            .OrderBy(r => r.Sequence).FirstOrDefaultAsync(ct);

    /// <summary>The capture details saved with the response (start time, word timings, speech metrics).</summary>
    public async Task<CapturedAnswer?> CaptureOfAsync(Guid responseId, CancellationToken ct)
    {
        var id = responseId.ToString();
        var json = await db.AuditEvents.Where(a => a.EntityType == nameof(StudentResponse) && a.EntityId == id
                && (a.Action == "response.captured" || a.Action == "response.revised"))
            .OrderBy(a => a.Id).Select(a => a.AfterJson).FirstOrDefaultAsync(ct);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("capture", out var c) ? c.Deserialize<CapturedAnswer>(Json) : null;
        }
        catch (JsonException) { return null; }
    }

    public Task<int> MeaningClarificationsAsync(Guid sessionId, CancellationToken ct) =>
        db.ClarificationEvents.CountAsync(c => c.SessionId == sessionId && c.Type == ClarificationType.Meaning, ct);

    /// <summary>What the exam's rule says happens to a changed answer, in the student's words (Part 4.7, Scenario E).</summary>
    public static string RevisionRule(AssessmentMode mode) => mode == AssessmentMode.Learning
        ? "This is a practice session: you can change an answer. Your first answer and your change are both kept, and both help show what you've learned."
        : "This is an examination: if you change an answer, your original answer stays on record and your change is recorded as a new answer. "
          + "Your instructor sees both and decides how the change counts. The examiner's questions continue from your original answer.";

    public void Audit(Guid org, Guid? actor, string action, StudentResponse response, Guid sessionId, string? before, string? after,
        string? reason, string? role = null) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org, ActorUserId = actor, ActorRole = role ?? (actor is null ? "System" : OrgRoles.Student),
            Action = action, EntityType = nameof(StudentResponse), EntityId = response.Id.ToString(), SessionId = sessionId,
            BeforeJson = before, AfterJson = after, Reason = reason is null ? null : Clip(reason, 1000), At = clock.GetUtcNow(),
        });

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
