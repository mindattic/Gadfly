using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Voice;
using Gadfly.Sources.Verification;

namespace Gadfly.Server.Research;

// The pilot research export (GAD-T451; ElenchusAtScale 7.1-7.3 measures, 7.5 data minimization). The loader
// (ResearchExportService) reads raw rows; everything here is pure, so the de-identification can be tested without a database:
// only students whose current research choice is "agreed" are included, every id becomes a keyed pseudonym, no name, email,
// answer text, audio or video is ever read into it, voice measurements are dropped wherever they are off or the student or
// organization is in the EU/EEA, and small groups are held back.

public sealed record ExportSession(
    Guid SessionId, Guid AssignmentId, Guid ExamId, Guid StudentUserId, int Attempt, string State, string? EndReason,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, bool Redacted,
    decimal TimeMultiplier, bool GazeAccommodation, bool TypedAnswers, bool StudentInEu,
    Guid? ArmId, DateTimeOffset? PublishedAt, string? FinalStatus, decimal? FinalPercent,
    /// <summary>The engine's saved session; read only to count the probing cues, never exported.</summary>
    string? TranscriptJson);

public sealed record ExportTurn(
    Guid SessionId, Guid TurnId, int Index, string ConceptKey, string Move, DateTimeOffset AskedAt,
    DateTimeOffset? AnswerStartedAt, DateTimeOffset? AnswerCompletedAt, string? AnswerMode, string? SpeechMetricsJson, double? MasteryAfter,
    double? PanelScore, double? PanelAgreement, bool? NeedsHumanReview, int PanelVotes, double? TextStyleScore,
    /// <summary>
    /// The spoken answer's word errors against a person's reference transcript (GAD-T426/T467): counts only, worked out by the
    /// loader, so neither the recognized words nor the reference ever reach the export. Null when the answer has no reference.
    /// </summary>
    WordErrorCount? WordErrors = null);

public sealed record ExportObjective(Guid ObjectiveId, Guid ExamId, string Key, int Ordinal, decimal Weight, decimal MaxScore);

public sealed record ExportPanelGrade(Guid SessionId, Guid ObjectiveId, string Level, decimal Score, decimal MaxScore, string Confidence);

public sealed record ExportHumanGrade(
    Guid SessionId, Guid ObjectiveId, Guid ReviewerUserId, string ReviewerRole, string Level, decimal Score, decimal MaxScore,
    bool WasBlind, bool Superseded, DateTimeOffset SubmittedAt);

public sealed record ExportDisagreement(
    Guid SessionId, Guid ObjectiveId, decimal AiScore, decimal HumanScore, decimal DifferencePoints, string Band,
    DateTimeOffset DetectedAt, DateTimeOffset? ResolvedAt, string? Cause, bool HadSecondReviewer, decimal? ResolvedAiScore, decimal? ResolvedHumanScore);

public sealed record ExportIntegrityEvent(
    Guid SessionId, string Source, string Type, string Severity, double Confidence, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, Guid? TurnId);

public sealed record ExportArm(Guid ArmId, Guid ExamId, string Name);

public sealed record ResearchExportInput
{
    public required string PseudonymKey { get; init; }
    public int MinimumGroupSize { get; init; } = 5;
    public bool OrganizationInEu { get; init; }
    public VoiceMeasuresSetting VoiceMeasures { get; init; } = VoiceMeasuresSetting.RegionDefault;
    public DateTimeOffset ExportedAt { get; init; }
    public IReadOnlyList<ResearchConsent> Consents { get; init; } = [];
    public IReadOnlyList<ExportSession> Sessions { get; init; } = [];
    public IReadOnlyList<ExportTurn> Turns { get; init; } = [];
    public IReadOnlyList<ExportObjective> Objectives { get; init; } = [];
    public IReadOnlyList<ExportPanelGrade> PanelGrades { get; init; } = [];
    public IReadOnlyList<ExportHumanGrade> HumanGrades { get; init; } = [];
    public IReadOnlyList<ExportDisagreement> Disagreements { get; init; } = [];
    public IReadOnlyList<ExportIntegrityEvent> IntegrityEvents { get; init; } = [];
    public IReadOnlyList<ExportArm> Arms { get; init; } = [];
}

/// <summary>One CSV table of the dataset.</summary>
public sealed record ResearchTable(string FileName, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>The de-identified dataset: its tables and what was held back.</summary>
public sealed record ResearchDataset(
    IReadOnlyList<ResearchTable> Tables, int Students, int Sessions, int ExamsIncluded, int ExamsSuppressed,
    int AccommodationColumnsSuppressed, int StudentsNotConsenting, IReadOnlyList<string> Notes)
{
    public ResearchTable Table(string fileName) => Tables.First(t => t.FileName == fileName);
}

public static class ResearchConsents
{
    /// <summary>
    /// The students whose current research choice (their latest decision) is "agreed". A later "declined" or "withdrawn" removes
    /// them; at the same instant the more restrictive decision wins.
    /// </summary>
    public static HashSet<Guid> CurrentlyAgreed(IEnumerable<ResearchConsent> consents) =>
        Current(consents).Where(kv => kv.Value == ResearchConsentDecision.Agreed).Select(kv => kv.Key).ToHashSet();

    /// <summary>Each student's latest decision.</summary>
    public static Dictionary<Guid, ResearchConsentDecision> Current(IEnumerable<ResearchConsent> consents) =>
        consents.GroupBy(c => c.StudentUserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.RecordedAt).ThenByDescending(c => (int)c.Decision).First().Decision);
}

public static class ResearchExport
{
    public const string SessionsFile = "sessions.csv", TurnsFile = "turns.csv", PanelFile = "panel_scores.csv", HumanFile = "human_scores.csv",
        ReconciliationsFile = "reconciliations.csv", IntegrityFile = "integrity_events.csv", ObjectivesFile = "objectives.csv", ArmsFile = "arms.csv";

    public const string Suppressed = "suppressed";

    /// <summary>Speech metrics every spoken answer may carry: how sure the recognizer was, and how long the answer was.</summary>
    public static readonly IReadOnlyList<string> RecognitionMetrics =
        [SpeechMetricKeys.WordCount, SpeechMetricKeys.SpokenSeconds, SpeechMetricKeys.VerificationConfidence, SpeechMetricKeys.LowConfidenceWords];

    /// <summary>
    /// Voice-derived metrics: exported only where voice measurements were on for the session and neither the organization nor
    /// the student is in the EU/EEA (EU AI Act Art. 5(1)(f), GAD-T259/T283).
    /// </summary>
    public static readonly IReadOnlyList<string> VoiceMetrics =
    [
        SpeechMetricKeys.TimeToFirstWordSeconds, SpeechMetricKeys.SpeechRateWpm, SpeechMetricKeys.PauseRatio, SpeechMetricKeys.LongPauseCount,
        SpeechMetricKeys.DisfluencyPer100, SpeechMetricKeys.FilledPausesPer100, VoiceStrain.VsBaselineKey,
    ];

    /// <summary>A keyed, stable pseudonym: the same key and id always give the same code; without the key it can't be reversed.</summary>
    public static string Pseudonym(string key, string prefix, Guid id)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var mac = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes($"{prefix}:{id:N}"));
        return prefix + "-" + Convert.ToHexString(mac, 0, 8).ToLowerInvariant();
    }

    /// <summary>A new random pseudonym key (32 bytes, base64).</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Whether voice measurements may be exported for a session.</summary>
    public static bool VoiceExportable(VoiceMeasuresSetting setting, bool organizationInEu, bool studentInEu) =>
        !organizationInEu && !studentInEu && VoiceStrain.IsEnabled(setting, organizationInEu, studentInEu);

    /// <summary>
    /// The speech metrics that may leave Gadfly for this answer: the recognition ones always, the voice-derived ones only when
    /// <paramref name="voiceAllowed"/> and the answer was not marked as unmeasured. Everything else (raw pitch, loudness, jitter,
    /// speaker counts, read-out pattern) is never exported.
    /// </summary>
    public static IReadOnlyDictionary<string, double> FilterMetrics(IReadOnlyDictionary<string, double>? metrics, bool voiceAllowed)
    {
        var d = new Dictionary<string, double>();
        if (metrics is null) return d;
        var voiceOff = !voiceAllowed || metrics.ContainsKey(VoiceStrain.OffKey);
        foreach (var k in RecognitionMetrics)
            if (metrics.TryGetValue(k, out var v)) d[k] = v;
        if (!voiceOff)
            foreach (var k in VoiceMetrics)
                if (metrics.TryGetValue(k, out var v)) d[k] = v;
        return d;
    }

    public static ResearchDataset Build(ResearchExportInput input)
    {
        var k = Math.Max(2, input.MinimumGroupSize);
        var key = input.PseudonymKey;
        string P(string prefix, Guid id) => Pseudonym(key, prefix, id);
        var notes = new List<string>();

        // 1. Consent: only students whose current choice is "agreed".
        var agreed = ResearchConsents.CurrentlyAgreed(input.Consents);
        var allStudents = input.Sessions.Select(s => s.StudentUserId).ToHashSet();
        var notConsenting = allStudents.Count(s => !agreed.Contains(s));
        var sessions = input.Sessions.Where(s => agreed.Contains(s.StudentUserId)).ToList();

        // 2. Small exams: an exam with fewer than k consenting students is held back entirely.
        var byExam = sessions.GroupBy(s => s.ExamId).ToDictionary(g => g.Key, g => g.ToList());
        var smallExams = byExam.Where(kv => kv.Value.Select(s => s.StudentUserId).Distinct().Count() < k).Select(kv => kv.Key).ToHashSet();
        if (smallExams.Count > 0)
            notes.Add($"{smallExams.Count} exam(s) held back: fewer than {k} consenting students.");
        sessions = sessions.Where(s => !smallExams.Contains(s.ExamId)).OrderBy(s => s.ExamId).ThenBy(s => s.StudentUserId).ThenBy(s => s.Attempt).ToList();

        // 3. Accommodations: within an exam, a value shared by fewer than k students (or held by all but fewer than k) is blanked.
        var suppressedColumns = 0;
        var suppress = new Dictionary<(Guid Exam, string Column), bool>();
        foreach (var g in sessions.GroupBy(s => s.ExamId))
        {
            var students = g.GroupBy(s => s.StudentUserId).Select(x => x.First()).ToList();
            void Check(string column, Func<ExportSession, bool> has)
            {
                var n = students.Count(has);
                var small = Math.Min(n, students.Count - n);
                if (small is > 0 && small < k) { suppress[(g.Key, column)] = true; suppressedColumns++; }
            }
            Check("extra_time", s => s.TimeMultiplier != 1m);
            Check("eye_contact_accommodation", s => s.GazeAccommodation);
            Check("typed_answer_accommodation", s => s.TypedAnswers);
        }
        if (suppressedColumns > 0)
            notes.Add($"{suppressedColumns} accommodation column(s) blanked as \"{Suppressed}\" within an exam: fewer than {k} students would be told apart.");

        var included = sessions.Select(s => s.SessionId).ToHashSet();
        var sessionById = sessions.ToDictionary(s => s.SessionId);
        var turnsBySession = input.Turns.Where(t => included.Contains(t.SessionId)).GroupBy(t => t.SessionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Index).ToList());
        var turnIndex = input.Turns.Where(t => included.Contains(t.SessionId)).ToDictionary(t => t.TurnId, t => t.Index);
        var eventsBySession = input.IntegrityEvents.Where(e => included.Contains(e.SessionId)).GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var objectives = input.Objectives.ToDictionary(o => o.ObjectiveId);
        var firstStart = sessions.GroupBy(s => s.StudentUserId)
            .ToDictionary(g => g.Key, g => g.Min(s => s.StartedAt ?? s.CreatedAt));

        // Probing cues, read from the saved engine session (content only, never voice), per session and turn index.
        var cues = sessions.ToDictionary(s => s.SessionId, s => CuesOf(s.TranscriptJson));

        // ---- sessions.csv
        var sessionRows = new List<IReadOnlyList<string>>();
        foreach (var s in sessions)
        {
            var start = s.StartedAt ?? s.CreatedAt;
            var voiceAllowed = VoiceExportable(input.VoiceMeasures, input.OrganizationInEu, s.StudentInEu);
            var earlierReleased = sessions.Any(o => o.AssignmentId == s.AssignmentId && o.Attempt < s.Attempt
                                                   && o.PublishedAt is { } p && p <= start);
            var turns = turnsBySession.GetValueOrDefault(s.SessionId) ?? [];
            var events = eventsBySession.GetValueOrDefault(s.SessionId) ?? [];
            string Acc(string column, string value) => suppress.ContainsKey((s.ExamId, column)) ? Suppressed : value;
            sessionRows.Add(
            [
                P("X", s.SessionId), P("S", s.StudentUserId), P("E", s.ExamId), s.ArmId is { } arm ? P("A", arm) : "",
                Num(s.Attempt), s.State, s.EndReason ?? "", IsoWeek(start),
                Num(Math.Round((start - firstStart[s.StudentUserId]).TotalDays, 2)),
                s.EndedAt is { } end && s.StartedAt is { } st ? Num(Math.Round((end - st).TotalSeconds)) : "",
                s.TypedAnswers ? "typed" : "spoken",
                Acc("extra_time", Num(s.TimeMultiplier)),
                Acc("eye_contact_accommodation", Bool(s.GazeAccommodation)),
                Acc("typed_answer_accommodation", Bool(s.TypedAnswers)),
                voiceAllowed ? "on" : "off",
                Bool(s.PublishedAt is not null), Bool(earlierReleased),
                s.FinalStatus ?? "", s.FinalPercent is { } fp ? Num(fp) : "",
                Num(turns.Count), Num(cues[s.SessionId].Values.Sum(c => c.Count)),
                Num(events.Count), Num(events.Count(e => e.Severity is "Info" or "Low")),
                Bool(s.Redacted),
            ]);
        }

        // ---- turns.csv
        var turnRows = new List<IReadOnlyList<string>>();
        foreach (var s in sessions)
        {
            var start = s.StartedAt ?? s.CreatedAt;
            var voiceAllowed = VoiceExportable(input.VoiceMeasures, input.OrganizationInEu, s.StudentInEu);
            foreach (var t in turnsBySession.GetValueOrDefault(s.SessionId) ?? [])
            {
                var raw = ReadMetrics(t.SpeechMetricsJson);
                var m = FilterMetrics(raw, voiceAllowed);
                string M(string metricKey) => m.TryGetValue(metricKey, out var v) ? Num(v) : "";
                var c = cues[s.SessionId].GetValueOrDefault(t.Index);
                turnRows.Add(
                [
                    P("X", s.SessionId), Num(t.Index), t.ConceptKey, t.Move,
                    Num(Math.Round((t.AskedAt - start).TotalSeconds, 1)),
                    t.AnswerStartedAt is { } a0 ? Num(Math.Round((a0 - t.AskedAt).TotalSeconds, 1)) : "",
                    t.AnswerStartedAt is { } a1 && t.AnswerCompletedAt is { } a2 ? Num(Math.Round((a2 - a1).TotalSeconds, 1)) : "",
                    Channel(t.AnswerMode),
                    M(SpeechMetricKeys.WordCount), M(SpeechMetricKeys.VerificationConfidence), M(SpeechMetricKeys.LowConfidenceWords),
                    .. WerCells(t),
                    M(SpeechMetricKeys.TimeToFirstWordSeconds), M(SpeechMetricKeys.SpeechRateWpm), M(SpeechMetricKeys.PauseRatio),
                    M(SpeechMetricKeys.LongPauseCount), M(SpeechMetricKeys.DisfluencyPer100), M(VoiceStrain.VsBaselineKey),
                    .. CutOffCells(t.AnswerMode, raw, voiceAllowed),
                    t.PanelScore is { } ps ? Num(ps) : "", t.PanelAgreement is { } pa ? Num(pa) : "",
                    t.NeedsHumanReview is { } nh ? Bool(nh) : "", Num(t.PanelVotes), t.TextStyleScore is { } ts ? Num(ts) : "",
                    t.MasteryAfter is { } ma ? Num(ma) : "",
                    c is null ? "" : Num(c.Count), c is null ? "" : string.Join(";", c.Kinds), c?.Pressure ?? "",
                ]);
            }
        }

        // ---- objectives.csv, panel_scores.csv, human_scores.csv, reconciliations.csv
        var includedExams = sessions.Select(s => s.ExamId).ToHashSet();
        var objectiveRows = input.Objectives.Where(o => includedExams.Contains(o.ExamId)).OrderBy(o => o.ExamId).ThenBy(o => o.Ordinal)
            .Select(o => (IReadOnlyList<string>)[P("O", o.ObjectiveId), P("E", o.ExamId), o.Key, Num(o.Ordinal), Num(o.Weight), Num(o.MaxScore)])
            .ToList();
        string Obj(Guid id) => P("O", id);
        var panelRows = input.PanelGrades.Where(g => included.Contains(g.SessionId))
            .Select(g => (IReadOnlyList<string>)[P("X", g.SessionId), Obj(g.ObjectiveId), g.Level, Num(g.Score), Num(g.MaxScore), g.Confidence])
            .ToList();
        var humanRows = input.HumanGrades.Where(g => included.Contains(g.SessionId)).OrderBy(g => g.SubmittedAt)
            .Select(g => (IReadOnlyList<string>)
            [
                P("X", g.SessionId), Obj(g.ObjectiveId), P("R", g.ReviewerUserId), g.ReviewerRole, g.Level, Num(g.Score), Num(g.MaxScore),
                Bool(g.WasBlind), Bool(g.Superseded),
                Num(Math.Round((g.SubmittedAt - (sessionById[g.SessionId].EndedAt ?? sessionById[g.SessionId].StartedAt ?? sessionById[g.SessionId].CreatedAt)).TotalHours, 2)),
            ]).ToList();
        var reconciliationRows = input.Disagreements.Where(d => included.Contains(d.SessionId))
            .Select(d => (IReadOnlyList<string>)
            [
                P("X", d.SessionId), Obj(d.ObjectiveId), Num(d.AiScore), Num(d.HumanScore), Num(d.DifferencePoints), d.Band,
                d.ResolvedAt is { } r ? Num(Math.Round((r - d.DetectedAt).TotalHours, 2)) : "", d.Cause ?? "", Bool(d.HadSecondReviewer),
                d.ResolvedAiScore is { } ra ? Num(ra) : "", d.ResolvedHumanScore is { } rh ? Num(rh) : "",
            ]).ToList();

        // ---- integrity_events.csv (low-confidence reviewer notes; never a verdict, never part of a grade)
        var integrityRows = new List<IReadOnlyList<string>>();
        foreach (var s in sessions)
        {
            var start = s.StartedAt ?? s.CreatedAt;
            foreach (var e in (eventsBySession.GetValueOrDefault(s.SessionId) ?? []).OrderBy(e => e.StartedAt))
                integrityRows.Add(
                [
                    P("X", s.SessionId), e.Source, e.Type, e.Severity, Num(Math.Round(e.Confidence, 3)),
                    Num(Math.Round((e.StartedAt - start).TotalSeconds, 1)),
                    e.EndedAt is { } en ? Num(Math.Round((en - e.StartedAt).TotalSeconds, 1)) : "",
                    e.TurnId is { } tid && turnIndex.TryGetValue(tid, out var ti) ? Num(ti) : "",
                ]);
        }

        // ---- arms.csv
        var armRows = input.Arms.Where(a => includedExams.Contains(a.ExamId))
            .Select(a => (IReadOnlyList<string>)[P("A", a.ArmId), P("E", a.ExamId), a.Name,
                Num(sessions.Where(s => s.ArmId == a.ArmId).Select(s => s.StudentUserId).Distinct().Count())])
            .ToList();

        if (notConsenting > 0)
            notes.Add($"{notConsenting} student(s) left out: they have not agreed to research use, declined, or withdrew.");

        var tables = new List<ResearchTable>
        {
            new(SessionsFile,
            [
                "session", "student", "exam", "arm", "attempt", "state", "end_reason", "started_week", "days_since_first_attempt", "duration_s",
                "answer_channel", "extra_time", "eye_contact_accommodation", "typed_answer_accommodation", "voice_measures",
                "result_published", "feedback_released_before_attempt", "final_status", "final_percent",
                "turns", "cues_total", "integrity_notes", "integrity_notes_info_or_low", "words_redacted_by_retention",
            ], sessionRows),
            new(TurnsFile,
            [
                "session", "turn", "concept", "move", "asked_at_s", "answer_latency_s", "answer_duration_s", "answer_channel",
                "word_count", "recognizer_confidence", "low_confidence_words",
                "reference_words", "wer_substitutions", "wer_deletions", "wer_insertions", "word_error_rate",
                "time_to_first_word_s", "speech_rate_wpm", "pause_ratio", "long_pauses", "disfluency_per_100_words", "voice_vs_baseline",
                "cut_off_watched", "cut_off", "cut_off_resume_s",
                "panel_score", "panel_agreement", "panel_needs_human_review", "panel_votes", "text_style_score", "mastery_after",
                "cue_count", "cue_kinds", "probe_pressure",
            ], turnRows),
            new(ObjectivesFile, ["objective", "exam", "key", "ordinal", "weight", "max_score"], objectiveRows),
            new(PanelFile, ["session", "objective", "level", "score", "max_score", "confidence"], panelRows),
            new(HumanFile, ["session", "objective", "rater", "rater_role", "level", "score", "max_score", "blind", "superseded", "hours_after_session"], humanRows),
            new(ReconciliationsFile,
            [
                "session", "objective", "panel_score", "human_score", "difference_points", "band", "hours_to_resolve", "cause", "second_reviewer",
                "resolved_panel_score", "resolved_human_score",
            ], reconciliationRows),
            new(IntegrityFile, ["session", "source", "type", "severity", "confidence", "started_at_s", "duration_s", "turn"], integrityRows),
            new(ArmsFile, ["arm", "exam", "name", "students"], armRows),
        };
        return new ResearchDataset(tables, sessions.Select(s => s.StudentUserId).Distinct().Count(), sessions.Count,
            includedExams.Count, smallExams.Count, suppressedColumns, notConsenting, notes);
    }

    /// <summary>The dataset as a ZIP: one CSV per table, README.txt and manifest.json.</summary>
    public static byte[] ToZip(ResearchDataset data, DateTimeOffset exportedAt, int minimumGroupSize)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var t in data.Tables)
                Write(zip, t.FileName, ToCsv(t));
            Write(zip, "README.txt", Readme(minimumGroupSize));
            Write(zip, "manifest.json", JsonSerializer.Serialize(new
            {
                exportedAt,
                format = "gadfly-research-export/1",
                researchNoticeVersion = ResearchConsentNotice.Version,
                minimumGroupSize,
                students = data.Students,
                sessions = data.Sessions,
                examsIncluded = data.ExamsIncluded,
                examsHeldBack = data.ExamsSuppressed,
                accommodationColumnsBlanked = data.AccommodationColumnsSuppressed,
                studentsLeftOut = data.StudentsNotConsenting,
                notes = data.Notes,
                files = data.Tables.Select(t => new { t.FileName, rows = t.Rows.Count }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return ms.ToArray();
    }

    public static string ToCsv(ResearchTable t)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", t.Columns.Select(Csv))).Append("\r\n");
        foreach (var r in t.Rows) sb.Append(string.Join(",", r.Select(Csv))).Append("\r\n");
        return sb.ToString();
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    private static string Readme(int k) => $"""
        Gadfly pilot research export (de-identified)

        Who is in it: only students whose current research choice is "agreed" (the optional research question, separate from
        the exam notice). Students who declined, never answered or withdrew are left out, and so is anyone whose data was erased.

        What is never in it: names, emails, user names, the words a student said or typed, the questions' text, audio, video,
        camera frames, free-text comments, appeal text, reviewer notes, or any database id.

        Codes: S- student, X- session, E- exam, O- objective, R- human rater, A- study arm. Codes are keyed pseudonyms: the same
        organization gives the same codes in every export until its research key is replaced. They cannot be turned back into ids
        without the key, which never leaves Gadfly.

        Times are relative: seconds from the session's start, the ISO week the session started, days since the student's first
        included attempt, hours from the session's end to a human score.

        Voice measurements (time to first word, speech rate, pauses, disfluency, the comparison with the student's first answers)
        are blank wherever voice measurements were off, and always for an organization or a student in the EU/EEA.

        Answer cut-offs (turns.csv: cut_off_watched, cut_off, cut_off_resume_s) are voice measurements too, blank under the same
        rule. cut_off_watched is 1 when the spoken answer was watched for one; cut_off is 1 when the end-of-answer silence closed
        the answer and the student went on speaking within 3 seconds (the delay is cut_off_resume_s). It may be an afterthought
        rather than a real cut-off; it never steered the questions and never entered a grade.

        Word error rate (turns.csv: reference_words, wer_substitutions, wer_deletions, wer_insertions, word_error_rate) is filled
        only for a spoken answer that a person transcribed by ear for the speech-recognition accuracy check, measured against
        that reference after lowercasing and removing punctuation. Only the counts are exported, never either transcript. The
        sample was drawn to cover answers with many fillers and repetitions, so compare groups with each other, not with
        published benchmarks; blank means no reference (most answers), or the words were removed by retention.

        Small groups (k = {k}): an exam with fewer than {k} included students is held back entirely; within an exam, an
        accommodation shared by fewer than {k} students (or missing for fewer than {k}) is shown as "{Suppressed}".

        Integrity notes are low-confidence reviewer notes. They were never a verdict and never part of any grade; analyse them
        as such.

        Files: sessions.csv, turns.csv, objectives.csv, panel_scores.csv (the AI panel's per-objective levels), human_scores.csv
        (every human mark, double marks included; "superseded" marks were later replaced), reconciliations.csv (each
        panel-human disagreement, its band, its cause code and resolution scores), integrity_events.csv, arms.csv, manifest.json.
        Columns are described in Gadfly's docs/PILOT.md ("Research export").
        """;

    // ---- probing cues (content only)

    public sealed record TurnCues(int Count, IReadOnlyList<string> Kinds, string Pressure);

    /// <summary>The cues <see cref="AnswerDelivery"/> read on each answered turn, as codes; empty when the transcript is gone or unreadable.</summary>
    public static Dictionary<int, TurnCues> CuesOf(string? transcriptJson)
    {
        var result = new Dictionary<int, TurnCues>();
        if (string.IsNullOrWhiteSpace(transcriptJson)) return result;
        try
        {
            var session = SessionTranscriptSerializer.Deserialize(transcriptJson);
            foreach (var turn in session.Turns)
            {
                if (AnswerDelivery.Read(session, turn) is not { } d) continue;
                result[turn.Index] = new TurnCues(d.Cues.Count, d.Cues.Select(CueKind).ToList(), d.Pressure.ToString());
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>A cue's code, without the student's words the cue quotes.</summary>
    public static string CueKind(string cue) => cue switch
    {
        _ when cue.StartsWith("hedging", StringComparison.Ordinal) => "hedging",
        _ when cue.StartsWith("vague wording", StringComparison.Ordinal) => "vague",
        _ when cue.StartsWith("no reason or example", StringComparison.Ordinal) => "no-reason",
        _ when cue.StartsWith("the judges did not find it convincing", StringComparison.Ordinal) => "unconvincing",
        _ when cue.StartsWith("evidence against understanding", StringComparison.Ordinal) => "evidence-against",
        _ when cue.StartsWith("unclear meaning", StringComparison.Ordinal) => "unclear",
        _ when cue.StartsWith("recited", StringComparison.Ordinal) => "recited",
        _ => "other",
    };

    // ---- word error rate (GAD-T467)

    /// <summary>
    /// One turn's word errors against the human reference transcripts of its spoken original answer(s): the counts summed over
    /// them (a turn normally has one). Null when none has a reference. The texts are used here and dropped; only counts leave.
    /// </summary>
    public static WordErrorCount? WordErrorsOf(IEnumerable<(string? Recognized, string? Reference)> answers)
    {
        WordErrorCount? total = null;
        foreach (var (recognized, reference) in answers)
        {
            if (string.IsNullOrWhiteSpace(reference)) continue;
            var e = SpeechAccuracy.WordErrors(reference, recognized);
            total = total is null ? e : new WordErrorCount(total.Substitutions + e.Substitutions, total.Deletions + e.Deletions,
                total.Insertions + e.Insertions, total.ReferenceWords + e.ReferenceWords);
        }
        return total;
    }

    /// <summary>reference_words, wer_substitutions, wer_deletions, wer_insertions, word_error_rate: blank without a reference.</summary>
    private static IReadOnlyList<string> WerCells(ExportTurn t) => t.WordErrors is not { } w || t.AnswerMode != "voice"
        ? ["", "", "", "", ""]
        : [Num(w.ReferenceWords), Num(w.Substitutions), Num(w.Deletions), Num(w.Insertions), Num(Math.Round(w.Rate, 4))];

    // ---- answer cut-offs (GAD-T470)

    /// <summary>
    /// cut_off_watched, cut_off, cut_off_resume_s for one answer (GAD-T462's measure): "1" in cut_off_watched when the spoken
    /// answer was watched for a cut-off, then cut_off 1 or 0, and the seconds to the student speaking again when it was cut off.
    /// It is a voice measurement, so all three are blank wherever voice measurements may not be exported (off for the
    /// organization, an EU/EEA organization or student, or this answer marked unmeasured), for a typed answer, and for an answer
    /// recorded before the measure existed.
    /// </summary>
    public static IReadOnlyList<string> CutOffCells(string? answerMode, IReadOnlyDictionary<string, double>? metrics, bool voiceAllowed)
    {
        if (!voiceAllowed || answerMode != "voice" || metrics is null || metrics.ContainsKey(VoiceStrain.OffKey)
            || AnswerCutOff.Read(metrics) is not { } cut)
            return ["", "", ""];
        return ["1", Bool(cut), AnswerCutOff.ResumeSeconds(metrics) is { } s ? Num(s) : ""];
    }

    // ---- formatting

    private static IReadOnlyDictionary<string, double>? ReadMetrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json); }
        catch (JsonException) { return null; }
    }

    private static string Channel(string? mode) => mode switch
    {
        null or "" => "",
        "voice" => "spoken",
        _ => "typed",
    };

    private static string IsoWeek(DateTimeOffset at)
    {
        var d = at.UtcDateTime;
        return $"{ISOWeek.GetYear(d)}-W{ISOWeek.GetWeekOfYear(d):00}";
    }

    private static string Bool(bool b) => b ? "1" : "0";
    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(string v)
    {
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            v = "'" + v;   // no spreadsheet formulas from a label an admin typed
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
