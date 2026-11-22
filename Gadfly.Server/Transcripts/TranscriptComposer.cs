using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Review;
using Gadfly.Server.Voice;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// Turns one session's ledger rows into the transcript document for one audience (GAD-T295). Pure: no database, no clock,
/// so the same rows always give the same document and the same verification code. This is the one place that decides
/// what each copy carries:
/// <list type="bullet">
/// <item>every copy: the questions with their times and the examiner's move, every answer exactly as recorded, the
/// read-back, clarifications, every transcription correction (original and corrected), the response delay and pauses,
/// and the words the speech recognizer was unsure of;</item>
/// <item>the reviewer copy adds integrity notes, voice measurements, the examiner's reason for each question, the AI
/// panel's exam-time reading and the recognizer's confidence figures;</item>
/// <item>the student copy and the blind grader copy carry none of those (reviewer-only context; docs/PRIVACY.md).</item>
/// </list>
/// </summary>
public static class TranscriptComposer
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static TranscriptDocument Compose(TranscriptSource src, TranscriptAudience audience)
    {
        var full = audience == TranscriptAudience.Reviewer;
        var s = src.Session;
        var turns = src.Turns.OrderBy(t => t.Index).ToList();
        var origin = s.StartedAt ?? (turns.Count > 0 ? turns[0].AskedAt : s.CreatedAt);
        double Off(DateTimeOffset at) => Math.Round(Math.Max(0, (at - origin).TotalSeconds), 1);

        var responsesByTurn = src.Responses.ToLookup(r => r.TurnId);
        var clarificationsByResponse = src.Clarifications.ToLookup(c => c.ResponseId);
        var judgments = full
            ? src.Judgments.GroupBy(j => j.TurnId).ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.CreatedAt).First())
            : [];

        // Integrity notes (reviewer copy only): on their turn by id, else inside the turn's question-to-answer window, else between turns.
        var notesByTurn = turns.ToDictionary(t => t.Id, _ => new List<TranscriptNote>());
        var loose = new List<TranscriptNote>();
        if (full)
        {
            foreach (var e in src.IntegrityEvents.OrderBy(e => e.StartedAt))
            {
                var (label, explanation, normal) = IntegrityExplanations.For(e.Type, e.Source);
                var note = new TranscriptNote(e.StartedAt, Off(e.StartedAt), label, explanation, e.Severity.ToString(), Math.Round(e.Confidence, 2), normal,
                    string.IsNullOrWhiteSpace(e.Summary) ? null : e.Summary);
                var owner = e.TurnId is { } tid && notesByTurn.ContainsKey(tid) ? tid
                    : turns.FirstOrDefault(t => e.StartedAt >= t.AskedAt && e.StartedAt <= (t.AnswerCompletedAt ?? t.AskedAt))?.Id;
                if (owner is { } o) notesByTurn[o].Add(note);
                else loose.Add(note);
            }
        }

        var docTurns = new List<TranscriptTurn>();
        foreach (var t in turns)
        {
            var metrics = Metrics(t.SpeechMetricsJson);
            var responses = responsesByTurn[t.Id].OrderBy(r => r.Sequence).ToList();
            var items = responses.Count > 0
                ? responses.Select(r => Response(r, t, metrics, clarificationsByResponse[r.Id].ToList(), src.Marks.GetValueOrDefault(r.Id), full, Off)).ToList()
                : LegacyAnswer(t, metrics, Off);

            TranscriptAiReading? ai = null;
            if (full && judgments.TryGetValue(t.Id, out var j))
                ai = new TranscriptAiReading(Math.Round(j.OverallScore, 3), Math.Round(j.Agreement, 3), j.NeedsHumanReview, j.Summary,
                    j.TextStyleScore is { } ts ? Math.Round(ts, 3) : null, j.TextStyleScore is { } ts2 ? IntegrityExplanations.TextStyle(ts2) : null,
                    t.MasteryAfter is { } m ? Math.Round(m, 3) : null);

            docTurns.Add(new TranscriptTurn
            {
                Number = t.Index + 1,
                Concept = src.ConceptTitles.TryGetValue(t.ConceptKey, out var title) && !string.IsNullOrWhiteSpace(title) ? title : t.ConceptKey,
                Move = MoveLabel(t.Move),
                MoveKind = t.Move,
                AskedAt = t.AskedAt,
                OffsetSeconds = Off(t.AskedAt),
                Question = t.Question,
                Responses = items,
                ExaminerReason = full && !string.IsNullOrWhiteSpace(t.PlannerReason) ? t.PlannerReason : null,
                Ai = ai,
                VoiceMeasurements = full && t.AnswerMode == "voice" ? VoiceStrain.Describe(metrics)?.Summary : null,
                Notes = full ? notesByTurn[t.Id] : [],
            });
        }

        var exam = src.Exam;
        return new TranscriptDocument
        {
            Audience = TranscriptAudiences.Key(audience),
            SessionId = s.Id,
            Organization = src.OrganizationName,
            ExamTitle = exam?.Title ?? "(deleted exam)",
            Subject = string.IsNullOrWhiteSpace(exam?.Subject) ? null : exam.Subject,
            Topic = string.IsNullOrWhiteSpace(exam?.Topic) ? null : exam.Topic,
            StudentName = src.StudentName,
            Attempt = s.Attempt,
            Mode = exam?.Mode == ExamMode.Voice || src.Responses.Any(r => r.Modality == "voice") ? "Spoken" : "Typed",
            Purpose = exam?.AssessmentMode == AssessmentMode.Learning ? "Practice" : "Examination",
            Status = StatusLabel(s.State),
            EndReason = s.EndReason is { Length: > 0 } er ? EndReasonLabel(er) : null,
            StartedAt = s.StartedAt,
            EndedAt = s.EndedAt,
            WordsRemovedAt = s.RedactedAt,
            Turns = docTurns,
            SessionNotes = full ? loose : [],
            LeftOut = TranscriptAudiences.LeftOut(audience),
        };
    }

    private static TranscriptResponse Response(StudentResponse r, SessionTurn t, IReadOnlyDictionary<string, double> metrics,
        IReadOnlyList<ClarificationEvent> clarifications, Verification.SpeechMark? mark, bool full, Func<DateTimeOffset, double> off)
    {
        var original = r.Kind == ResponseKind.Original;
        var voice = r.Modality == "voice";
        var versions = r.Versions.OrderBy(v => v.Version).ToList();
        var corrections = new List<TranscriptCorrection>();
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            if (v.CorrectionKind != CorrectionKind.Transcription) continue;
            // The text it corrected: the latest earlier version that was the recorded text (not a meaning clarification).
            var before = versions.Take(i).LastOrDefault(p => p.CorrectionKind is null or CorrectionKind.Transcription)?.Text ?? r.OriginalText;
            corrections.Add(new TranscriptCorrection(v.Version, v.CreatedAt, before, v.Text, CorrectedBy(v.Source)));
        }

        var readBack = clarifications.Where(c => c.Type == ClarificationType.Transcription).OrderBy(c => c.AskedAt).FirstOrDefault();
        DateTimeOffset? startAt = original ? t.AnswerStartedAt : null;
        var words = mark?.Words is { Count: > 0 } mw ? mw : original ? Words(t.WordsJson) : [];
        if (startAt is null && words.Count > 0) startAt = r.SubmittedAt - words[^1].End;

        return new TranscriptResponse
        {
            Sequence = r.Sequence,
            Kind = original ? "Answer" : "Changed answer",
            Modality = voice ? "Spoken" : "Typed",
            StartedAt = startAt,
            CompletedAt = r.SubmittedAt,
            StartOffsetSeconds = startAt is { } sa ? off(sa) : null,
            EndOffsetSeconds = off(r.SubmittedAt),
            AsRecorded = r.OriginalText,
            Verified = string.Equals(r.VerifiedText, r.OriginalText, StringComparison.Ordinal) ? null : r.VerifiedText,
            RecordStatus = StateLabel(r.VerificationState, voice),
            DelaySeconds = original && t.AnswerStartedAt is { } st ? Math.Round(Math.Max(0, (st - t.AskedAt).TotalSeconds), 1) : null,
            SpokenSeconds = original ? Round1(Get(metrics, SpeechMetricKeys.SpokenSeconds)) : null,
            Pauses = original ? Int(Get(metrics, SpeechMetricKeys.PauseCount)) : null,
            LongPauses = original ? Int(Get(metrics, SpeechMetricKeys.LongPauseCount)) : null,
            LongestPauseSeconds = original ? Round1(Get(metrics, SpeechMetricKeys.PauseMaxSeconds)) : null,
            ReadBack = readBack is null ? null : new TranscriptReadBack(readBack.AskedAt, readBack.Question, ReadBackOutcome(readBack), readBack.AnsweredAt),
            Corrections = corrections,
            Clarifications = clarifications.Where(c => c.Type == ClarificationType.Meaning).OrderBy(c => c.AskedAt)
                .Select(c => new TranscriptClarification(c.AskedAt, c.Question, c.StudentReply, c.AnsweredAt)).ToList(),
            UnsureWords = (mark?.Segments ?? []).Select(u => new TranscriptUnsure(u.Text, Math.Round(u.Start.TotalSeconds, 2), Math.Round(u.End.TotalSeconds, 2),
                full ? Math.Round(u.MinConfidence, 2) : null)).ToList(),
            CheckedByStudent = voice ? mark?.TranscriptChecked ?? (r.VerificationState is VerificationState.Confirmed or VerificationState.Corrected) : null,
            Words = words.Select(w => new TranscriptWord(w.Text, Math.Round(w.Start.TotalSeconds, 2), Math.Round(w.End.TotalSeconds, 2))).ToList(),
        };
    }

    /// <summary>A turn answered before the response ledger existed (or not yet captured there): its answer from the turn itself.</summary>
    private static IReadOnlyList<TranscriptResponse> LegacyAnswer(SessionTurn t, IReadOnlyDictionary<string, double> metrics, Func<DateTimeOffset, double> off)
    {
        if (t.AnswerText is null || t.AnswerCompletedAt is not { } done) return [];
        var voice = t.AnswerMode == "voice";
        var words = Words(t.WordsJson);
        return
        [
            new TranscriptResponse
            {
                Sequence = 0, Kind = "Answer", Modality = voice ? "Spoken" : "Typed",
                StartedAt = t.AnswerStartedAt, CompletedAt = done,
                StartOffsetSeconds = t.AnswerStartedAt is { } sa ? off(sa) : null, EndOffsetSeconds = off(done),
                AsRecorded = t.AnswerText, RecordStatus = "Recorded",
                DelaySeconds = t.AnswerStartedAt is { } st ? Math.Round(Math.Max(0, (st - t.AskedAt).TotalSeconds), 1) : null,
                SpokenSeconds = Round1(Get(metrics, SpeechMetricKeys.SpokenSeconds)),
                Pauses = Int(Get(metrics, SpeechMetricKeys.PauseCount)),
                LongPauses = Int(Get(metrics, SpeechMetricKeys.LongPauseCount)),
                LongestPauseSeconds = Round1(Get(metrics, SpeechMetricKeys.PauseMaxSeconds)),
                Words = words.Select(w => new TranscriptWord(w.Text, Math.Round(w.Start.TotalSeconds, 2), Math.Round(w.End.TotalSeconds, 2))).ToList(),
            },
        ];
    }

    // ------------------------------------------------------------------ labels

    public static string MoveLabel(string move) => move switch
    {
        nameof(MoveKind.Elicit) => "Opening question",
        nameof(MoveKind.Clarify) => "Asks what a term means",
        nameof(MoveKind.Probe) => "Probes the reasoning",
        nameof(MoveKind.Counterexample) => "Offers a counterexample",
        nameof(MoveKind.Contradiction) => "Points out a contradiction",
        nameof(MoveKind.Apply) => "Asks to apply the idea",
        nameof(MoveKind.Synthesize) => "Connects two ideas",
        nameof(MoveKind.Close) => "Closes the topic",
        nameof(MoveKind.CausalReasoning) => "Asks about cause and effect",
        nameof(MoveKind.ScenarioVariation) => "Changes the scenario",
        nameof(MoveKind.AssumptionTesting) => "Tests an assumption",
        nameof(MoveKind.IndependentExplanation) => "Asks for an explanation in own words",
        nameof(MoveKind.Hint) => "Gives a hint (practice)",
        nameof(MoveKind.GuidedExplanation) => "Explains, then asks again (practice)",
        "" => "Question",
        _ => move,
    };

    public static string StateLabel(VerificationState state, bool voice) => state switch
    {
        VerificationState.Captured => voice ? "Recorded" : "Submitted",
        VerificationState.VerificationRequired => "Needs checking",
        VerificationState.AwaitingStudentConfirmation => "Waiting for the student to check it",
        VerificationState.Confirmed => "Confirmed by the student",
        VerificationState.Corrected => "Corrected by the student",
        VerificationState.Clarified => "Clarified by the student",
        VerificationState.Unresolved => "Unresolved: a person reviews it",
        VerificationState.FlaggedForHumanReview => "Sent to a person for review",
        _ => state.ToString(),
    };

    private static string StatusLabel(SessionState s) => s switch
    {
        SessionState.Created or SessionState.ConsentGiven => "Not started",
        SessionState.Running => "In progress",
        SessionState.Paused => "Paused",
        SessionState.Ended => "Ended",
        SessionState.Failed => "Stopped by a technical problem",
        _ => s.ToString(),
    };

    private static string EndReasonLabel(string reason) => reason switch
    {
        nameof(EndReason.Mastery) => "Every topic was shown to be understood",
        nameof(EndReason.TimeUp) => "Time ran out",
        nameof(EndReason.TurnLimit) => "The question limit was reached",
        nameof(EndReason.Abandoned) => "Ended early by the student or the host",
        nameof(EndReason.Aporia) => "The student said honestly that they did not know",
        nameof(EndReason.Failed) => "Stopped by a technical problem",
        nameof(EndReason.EvidenceComplete) => "Every learning objective had enough evidence",
        nameof(EndReason.PromptSetChanged) => "Could not resume after an update (not a used attempt)",
        _ => System.Text.RegularExpressions.Regex.Replace(reason, "(?<=[a-z])(?=[A-Z])", " "),
    };

    private static string CorrectedBy(string source) => source switch
    {
        "speech-recognition" => "the student, by saying it again",
        "reviewer" => "a reviewer",
        _ => "the student",
    };

    private static string ReadBackOutcome(ClarificationEvent c)
    {
        if (c.AnsweredAt is null) return "No reply recorded.";
        if (c.StudentReply is null) return "No reply was given; the answer stands as recorded and a person reviews it.";
        return string.Equals(c.StudentReply, "confirmed", StringComparison.OrdinalIgnoreCase)
            ? "The student confirmed this is what they said."
            : "The student corrected the recording.";
    }

    // ------------------------------------------------------------------ helpers

    private static IReadOnlyDictionary<string, double> Metrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json, Web) ?? new(); }
        catch (JsonException) { return new Dictionary<string, double>(); }
    }

    internal static IReadOnlyList<TimedWord> Words(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<TimedWord>>(json, Web)?.Where(w => w is not null).ToList() ?? []; }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return []; }
    }

    private static double? Get(IReadOnlyDictionary<string, double> m, string key) =>
        m.TryGetValue(key, out var v) && double.IsFinite(v) ? v : null;

    private static double? Round1(double? v) => v is { } x ? Math.Round(x, 1) : null;
    private static int? Int(double? v) => v is { } x ? (int)Math.Round(x) : null;
}
