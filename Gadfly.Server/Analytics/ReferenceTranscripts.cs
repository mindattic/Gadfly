using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Verification;
using Gadfly.Sources.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Analytics;

// Human-reference transcripts for word error rate (GAD-T426; ElenchusAtScale Section 5.3.1, Appendix A.9 open item, M14,
// correction 43; docs/EVIDENCE_MAP.md mechanic 7). Since GAD-T405 a spoken answer is taken as heard, with no read-back, so no
// spoken answer has a transcript the student checked and the speech-recognition accuracy audit (GAD-T265) lost its reference.
// This restores it without reinstating the read-back: an Owner or Admin draws a sample of spoken answers and a person who heard
// the answer (present while it was given, or from a recording the student agreed to and the institution keeps; Gadfly itself
// stores no audio, docs/PRIVACY.md) types what was actually said. The audit then measures word error rate against it.
//
// Each reference is an audit event ("response.reference-transcribed", tied to the session), so it is append-only, says who wrote
// it and when, is never read by grading, never changes the answer, and is cleared with the rest of the answer's words by
// retention and erasure (PrivacyService clears every "response." audit event's JSON). A later event replaces an earlier one; an
// empty one withdraws it.

/// <summary>One spoken answer offered for a reference transcript.</summary>
public sealed record ReferenceCandidate(Guid ResponseId, Guid Student, string Band);

/// <summary>One sampled answer as the sampler page shows it.</summary>
public sealed record ReferenceItem(Guid ResponseId, Guid SessionId, string Exam, string Student, int Attempt, int QuestionNumber, string Question,
    DateTimeOffset AnsweredAt, string Recognized, IReadOnlyList<string> Unsure, ReferenceRecord? Existing);

/// <summary>A stored reference: what was said, how the listener heard it, who wrote it and when.</summary>
public sealed record ReferenceRecord(string Text, string How, Guid? By, DateTimeOffset At);

/// <summary>The reference page's form state for one answer.</summary>
public sealed class ReferenceFormState
{
    public string? Text { get; set; }
    public string? How { get; set; }
    public bool Consented { get; set; }
    public bool Saved { get; set; }
    public string? Message { get; set; }
    public bool IsError { get; set; }
}

public sealed class ReferenceTranscripts(GadflyDbContext db, Guid organizationId)
{
    public const string Action = "response.reference-transcribed";

    /// <summary>How the listener heard the answer. Gadfly keeps no audio, so it is always one of these.</summary>
    public const string HowPresent = "present", HowRecording = "consented-recording";

    public static readonly IReadOnlyDictionary<string, string> HowLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [HowPresent] = "I heard the answer as it was given (in the room or on the call)",
        [HowRecording] = "From a recording the student agreed to, kept by the institution",
    };

    /// <summary>The longest reference kept.</summary>
    public const int MaxLength = 10000;

    /// <summary>The most answers one draw offers.</summary>
    public const int MaxSample = 50;

    /// <summary>
    /// Draws up to <paramref name="size"/> answers without a reference, spread across the disfluency bands (round robin, so the
    /// answers most at risk of mis-recognition are not crowded out) and across students (a student is picked again only once every
    /// other student in the band has been). The same <paramref name="seed"/> gives the same draw.
    /// </summary>
    public static IReadOnlyList<Guid> Sample(IEnumerable<ReferenceCandidate> candidates, IReadOnlySet<Guid> referenced, int size, int seed)
    {
        var rng = new Random(seed);
        var bands = candidates.Where(c => !referenced.Contains(c.ResponseId))
            .GroupBy(c => c.Band, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Queue<ReferenceCandidate>(SpreadByStudent(g.OrderBy(_ => rng.Next()).ToList())))
            .ToList();
        var picked = new List<Guid>();
        while (picked.Count < size && bands.Any(b => b.Count > 0))
            foreach (var band in bands.Where(b => b.Count > 0))
            {
                if (picked.Count >= size) break;
                picked.Add(band.Dequeue().ResponseId);
            }
        return picked;
    }

    /// <summary>Reorders a shuffled band so each student's first answer comes before anyone's second.</summary>
    private static IEnumerable<ReferenceCandidate> SpreadByStudent(IReadOnlyList<ReferenceCandidate> shuffled) =>
        shuffled.GroupBy(c => c.Student).SelectMany(g => g.Select((c, i) => (c, i))).OrderBy(x => x.i).Select(x => x.c);

    /// <summary>The latest reference per response, leaving out withdrawn and redacted ones.</summary>
    public static async Task<Dictionary<Guid, ReferenceRecord>> LoadAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<Guid> responseIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, ReferenceRecord>();
        foreach (var chunk in responseIds.Select(i => i.ToString()).Chunk(1000))
        {
            var rows = await db.AuditEvents.AsNoTracking()
                .Where(a => a.OrganizationId == organizationId && a.Action == Action && a.EntityType == nameof(StudentResponse)
                    && a.EntityId != null && chunk.Contains(a.EntityId))
                .OrderBy(a => a.Id).Select(a => new { a.EntityId, a.AfterJson, a.ActorUserId, a.At }).ToListAsync(ct);
            foreach (var row in rows)
            {
                if (!Guid.TryParse(row.EntityId, out var id)) continue;
                if (Parse(row.AfterJson) is { } r) result[id] = r with { By = row.ActorUserId, At = row.At };
                else result.Remove(id);   // withdrawn, or redacted by retention or erasure
            }
        }
        return result;
    }

    /// <summary>The reference in an audit event's JSON; null when withdrawn, redacted or unreadable.</summary>
    public static ReferenceRecord? Parse(string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(afterJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(afterJson);
            if (!doc.RootElement.TryGetProperty("reference", out var r) || r.ValueKind != JsonValueKind.String) return null;
            var text = r.GetString()?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            var how = doc.RootElement.TryGetProperty("how", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "";
            return new ReferenceRecord(text, how, null, default);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The spoken original answers of <paramref name="examIds"/>, as sampling candidates and by id.</summary>
    private async Task<List<StudentResponse>> SpokenAsync(IReadOnlyCollection<Guid> examIds, CancellationToken ct)
    {
        var sessionIds = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && examIds.Contains(s.ExamId)).Select(s => s.Id).ToListAsync(ct);
        var responses = new List<StudentResponse>();
        foreach (var chunk in sessionIds.Chunk(500))
            responses.AddRange(await db.StudentResponses.AsNoTracking()
                .Where(r => r.OrganizationId == organizationId && chunk.Contains(r.SessionId) && r.Modality == "voice" && r.Kind == ResponseKind.Original)
                .ToListAsync(ct));
        return responses;
    }

    /// <summary>How many spoken answers the scope has, and how many have a reference.</summary>
    public async Task<(int Spoken, int Referenced)> CountsAsync(IReadOnlyCollection<Guid> examIds, CancellationToken ct = default)
    {
        var spoken = await SpokenAsync(examIds, ct);
        var refs = await LoadAsync(db, organizationId, spoken.Select(r => r.Id).ToList(), ct);
        return (spoken.Count, refs.Count);
    }

    /// <summary>A fresh draw of answers without a reference, with what the sampler page shows for each.</summary>
    public async Task<IReadOnlyList<ReferenceItem>> DrawAsync(IReadOnlyCollection<Guid> examIds, int size, int seed,
        IReadOnlyDictionary<Guid, string> names, CancellationToken ct = default)
    {
        var spoken = (await SpokenAsync(examIds, ct)).Where(r => !string.IsNullOrWhiteSpace(r.SpeechRecognitionText ?? r.OriginalText)
            && !r.OriginalText.StartsWith("[redacted", StringComparison.Ordinal)).ToList();
        var referenced = (await LoadAsync(db, organizationId, spoken.Select(r => r.Id).ToList(), ct)).Keys.ToHashSet();
        var sessions = await db.ExamSessions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && examIds.Contains(s.ExamId))
            .Select(s => new { s.Id, s.StudentUserId, s.ExamId, s.Attempt }).ToDictionaryAsync(s => s.Id, ct);
        var candidates = spoken.Where(r => sessions.ContainsKey(r.SessionId)).Select(r => new ReferenceCandidate(r.Id, sessions[r.SessionId].StudentUserId,
            SpeechAccuracy.DisfluencyBand(SpeechAccuracy.DisfluencyRate(r.SpeechRecognitionText ?? r.OriginalText))));
        var ids = Sample(candidates, referenced, Math.Clamp(size, 1, MaxSample), seed).ToList();
        return await ItemsAsync(spoken.Where(r => ids.Contains(r.Id)).OrderBy(r => ids.IndexOf(r.Id)).ToList(), names, ct);
    }

    /// <summary>The answers that already have a reference in the scope, newest first, for checking or changing.</summary>
    public async Task<IReadOnlyList<ReferenceItem>> DoneAsync(IReadOnlyCollection<Guid> examIds, IReadOnlyDictionary<Guid, string> names, CancellationToken ct = default)
    {
        var spoken = await SpokenAsync(examIds, ct);
        var refs = await LoadAsync(db, organizationId, spoken.Select(r => r.Id).ToList(), ct);
        var items = await ItemsAsync(spoken.Where(r => refs.ContainsKey(r.Id)).ToList(), names, ct);
        return items.OrderByDescending(i => i.Existing!.At).ToList();
    }

    private async Task<IReadOnlyList<ReferenceItem>> ItemsAsync(IReadOnlyList<StudentResponse> responses, IReadOnlyDictionary<Guid, string> names,
        CancellationToken ct)
    {
        if (responses.Count == 0) return [];
        var sessionIds = responses.Select(r => r.SessionId).Distinct().ToList();
        var turnIds = responses.Select(r => r.TurnId).Distinct().ToList();
        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.OrganizationId == organizationId && sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.StudentUserId, s.ExamId, s.Attempt }).ToDictionaryAsync(s => s.Id, ct);
        var examIds = sessions.Values.Select(s => s.ExamId).Distinct().ToList();
        var exams = await db.Exams.AsNoTracking().Where(e => e.OrganizationId == organizationId && examIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Title }).ToDictionaryAsync(e => e.Id, e => e.Title, ct);
        var turns = await db.SessionTurns.AsNoTracking().Where(t => t.OrganizationId == organizationId && turnIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Index, t.Question }).ToDictionaryAsync(t => t.Id, ct);
        var marks = await SpeechMarks.LoadAsync(db, organizationId, responses, ct);
        var refs = await LoadAsync(db, organizationId, responses.Select(r => r.Id).ToList(), ct);
        return responses.Where(r => sessions.ContainsKey(r.SessionId)).Select(r =>
        {
            var s = sessions[r.SessionId];
            var t = turns.GetValueOrDefault(r.TurnId);
            return new ReferenceItem(r.Id, r.SessionId, exams.GetValueOrDefault(s.ExamId, "Assessment"), names.GetValueOrDefault(s.StudentUserId, "Student"),
                s.Attempt, (t?.Index ?? -1) + 1, t?.Question ?? "", r.SubmittedAt, r.SpeechRecognitionText ?? r.OriginalText,
                marks.GetValueOrDefault(r.Id)?.Segments.Select(x => x.Text).ToList() ?? [], refs.GetValueOrDefault(r.Id));
        }).ToList();
    }

    /// <summary>
    /// Records (or, with an empty <paramref name="text"/>, withdraws) the reference for one spoken answer of this organization.
    /// Returns an error to show, or null when saved.
    /// </summary>
    public async Task<string?> SaveAsync(Guid responseId, string? text, string? how, bool consented, Guid actorUserId, string? actorRole,
        DateTimeOffset now, CancellationToken ct = default)
    {
        var response = await db.StudentResponses.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.Id == responseId && r.Modality == "voice")
            .Select(r => new { r.Id, r.SessionId }).FirstOrDefaultAsync(ct);
        if (response is null) return "That answer is not a spoken answer in this organization.";
        var clean = Clean(text);
        string json;
        if (clean is null) json = JsonSerializer.Serialize(new { withdrawn = true });
        else
        {
            if (how is null || !HowLabels.ContainsKey(how)) return "Say how you heard the answer.";
            if (!consented) return "Confirm the student agreed to their answer being listened to for this check.";
            json = JsonSerializer.Serialize(new { reference = clean, how, consentConfirmed = true });
        }
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            ActorRole = actorRole is { Length: > 40 } r ? r[..40] : actorRole,
            Action = Action,
            EntityType = nameof(StudentResponse),
            EntityId = response.Id.ToString(),
            SessionId = response.SessionId,
            AfterJson = json,
            Reason = clean is null ? "Reference transcript withdrawn." : "Reference transcript for the speech-recognition accuracy audit.",
            At = now,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>A reference as kept: control characters removed, whitespace collapsed, at most <see cref="MaxLength"/>; null when empty.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = string.Join(' ', new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return t.Length == 0 ? null : t.Length > MaxLength ? t[..MaxLength] : t;
    }
}
