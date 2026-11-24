using System.Globalization;
using System.Text;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources.Grading;

namespace Gadfly.Server.Grading;

/// <summary>One objective's line in the assessment report.</summary>
public sealed record ReportObjective(string Key, string Title, decimal MaxScore, decimal Weight, decimal? AiScore, string? AiLevel, string? AiConfidence,
    bool AiExcluded, bool AiIsMock, decimal? HumanScore, string? HumanLevel, string? HumanComment, decimal? Combined, string? Band, string? Reconciliation,
    IReadOnlyList<ReportEvidence> Evidence, string? AiExplanation, IReadOnlyList<string>? Missing = null, int? PreviousAttempt = null,
    decimal? PreviousCombined = null, bool? FeedbackBeforeAttempt = null)
{
    /// <summary>What this objective lacked, criterion by criterion, and what the next level asks for (GAD-T273).</summary>
    public IReadOnlyList<string> WhatWasMissing => Missing ?? [];

    /// <summary>The combined score's change since <see cref="PreviousAttempt"/>, when both are known.</summary>
    public decimal? Change => Combined is { } now && PreviousCombined is { } before ? now - before : null;
}

/// <summary>A verified response quoted as evidence.</summary>
public sealed record ReportEvidence(int Sequence, string Question, string VerifiedText, bool Corrected);

/// <summary>
/// The assessment report (Feature 10; Part 11 Stage 11): final grade, per-objective AI, human and combined
/// scores, evidence, knowledge gaps and approval status. Reviewers see everything; a student sees it only
/// after publication with an approval record, without reviewer-only detail (voters, audit, unresolved flags).
/// Evidence quotes are verified response text only.
/// </summary>
public sealed record AssessmentReport
{
    public required Guid SessionId { get; init; }
    public required string StudentName { get; init; }
    public required string ExamTitle { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset? EndedAt { get; init; }
    public required string Status { get; init; }
    public required decimal? FinalPercent { get; init; }
    public required string? Label { get; init; }
    public required string Policy { get; init; }
    public required IReadOnlyList<ReportObjective> Objectives { get; init; }
    public required IReadOnlyList<string> KnowledgeGaps { get; init; }
    public required IReadOnlyList<string> Approvals { get; init; }
    public required IReadOnlyList<string> Appeals { get; init; }
    public required bool IsMock { get; init; }
    public required bool ForStudent { get; init; }
    public required bool IsApproved { get; init; }
    /// <summary>
    /// "summative" or "formative" (GAD-T267): a Learning session, or an examination that examined fewer objectives than its
    /// policy's minimum, gives formative per-objective scores. One short oral samples too little content to grade reliably.
    /// </summary>
    public string ScoreUse { get; init; } = ExamObjectives.Summative;
    public string ScoreUseReason { get; init; } = "";
    public bool IsFormative => ScoreUse == ExamObjectives.Formative;
    /// <summary>
    /// Reviewers only: follow-up questions each objective received after its opening one, by objective key (GAD-T267), so a
    /// long thread is seen rather than silently shaping a score. Empty for students and when the transcript is gone.
    /// </summary>
    public IReadOnlyDictionary<string, int> FollowUps { get; init; } = new Dictionary<string, int>();
    /// <summary>
    /// Reviewers only: the path each objective took, by objective key (GAD-T435): its questions and their kinds, the criteria
    /// the score covers, and the answers excluded by reason (the last two only once the AI's view is visible). Empty for students.
    /// </summary>
    public IReadOnlyDictionary<string, ObjectivePath> Paths { get; init; } = new Dictionary<string, ObjectivePath>();

    /// <summary>An objective scoring below this share of its maximum is reported as a knowledge gap.</summary>
    public const decimal GapBelowPercent = 60m;

    public static AssessmentReport Build(GradingWorkspace w, bool forStudent)
    {
        var ci = CultureInfo.InvariantCulture;
        var byId = w.Responses.ToDictionary(r => r.Response.Id);
        var objectives = w.Objectives.Select(o =>
        {
            var showAi = w.AiVisible && o.Ai is not null;
            var evidenceIds = (showAi ? o.Ai!.EvidenceRefs : []).Concat((w.AiVisible ? o.Evidence : []).Select(e => e.ResponseId)).Distinct()
                .Where(byId.ContainsKey).Select(id => byId[id]).Where(r => !r.NeedsHuman)
                .OrderBy(r => r.Response.Sequence).Take(6)
                .Select(r => new ReportEvidence(r.Response.Sequence, r.Question, r.Response.VerifiedText, r.TextChanged)).ToList();
            var rec = o.LatestReconciliation;
            // The comparison attempt: the latest earlier graded one; a student only ever sees one published to them.
            var prior = w.AiVisible ? w.PriorAttempts.FirstOrDefault(p => (!forStudent || p.VisibleToStudent) && p.Combined.GetValueOrDefault(o.Objective.Key) is not null) : null;
            return new ReportObjective(o.Objective.Key, o.Objective.Title, o.Objective.MaxScore, o.Objective.Weight,
                showAi ? o.Combined?.AiScore is { } a && !o.AiExcluded ? a : o.Ai!.Row.Score : null,
                showAi ? MasteryLevels.Label(o.Ai!.Row.Level) : null, showAi ? o.Ai!.Row.Confidence : null, showAi && o.AiExcluded, showAi && o.Ai!.Row.IsMock,
                o.Combined?.HumanScore ?? o.Human?.Score, o.Human is { } h ? MasteryLevels.Label(h.Level) : null, o.Human?.Comment,
                w.AiVisible ? o.Combined?.Combined : null, w.AiVisible ? o.Combined?.Band?.ToString() : null,
                !w.AiVisible || rec is null ? null : $"{ReconciliationCauses.Label(rec.Cause)}: {rec.Resolution}",
                evidenceIds, showAi ? o.Ai!.Row.Explanation : null, WhatWasMissing(o, showAi),
                prior?.Attempt, prior?.Combined.GetValueOrDefault(o.Objective.Key), prior?.FeedbackBeforeThisAttempt);
        }).ToList();

        var gaps = new List<string>();
        foreach (var o in w.Objectives)
        {
            if (w.AiVisible && o.Combined?.Combined is { } c && c / o.Objective.MaxScore * 100m < GapBelowPercent)
                gaps.Add($"{o.Objective.Title}: {Pct(c / o.Objective.MaxScore * 100m)} of the objective demonstrated.");
            else if (o.Human is { } h && h.Level <= MasteryLevel.BasicExplanation && h.Level != MasteryLevel.InsufficientEvidence)
                gaps.Add($"{o.Objective.Title}: examiner judged {MasteryLevels.Label(h.Level)}.");
            if (w.AiVisible && o.Ai is { } ai)
                foreach (var m in ai.Misconceptions.Take(3)) gaps.Add($"{o.Objective.Title}: misconception noted: {m.Text}");
            if (w.AiVisible && o.Ai?.Insufficient == true && o.Human?.Level == MasteryLevel.InsufficientEvidence)
                gaps.Add($"{o.Objective.Title}: not enough evidence was gathered to judge this objective (not a judgment of zero understanding).");
        }

        var p = w.Policy.Rules;
        var (followUps, examined) = ThreadLengths(w.Session);
        var sessionPolicy = GradingJson.Read<SessionPolicy>(w.Session.PolicyJson) ?? new SessionPolicy();
        var (scoreUse, scoreUseReason) = ExamObjectives.ScoreUse(sessionPolicy, examined ?? w.Objectives.Count);
        return new AssessmentReport
        {
            SessionId = w.Session.Id, StudentName = w.StudentName, ExamTitle = w.ExamTitle, Attempt = w.Session.Attempt, EndedAt = w.Session.EndedAt,
            Status = w.Status is { } s ? GradeStatusMachine.Label(s) : "Not started",
            FinalPercent = w.AiVisible ? w.Grade?.FinalPercent : null, Label = w.AiVisible ? w.Grade?.LetterOrLabel : null,
            Policy = $"{Assessments.PolicySnapshot.ModeLabel(p.Mode, p.AiWeight, p.HumanWeight)}; additional review at {p.AdditionalReviewAt.ToString("0.##", ci)} points, " +
                     $"reconciliation at {p.MandatoryReconciliationAt.ToString("0.##", ci)} points; {(p.BlindHumanGrading ? "blind" : "open")} human grading ({w.Policy.Source}).",
            Objectives = objectives, KnowledgeGaps = gaps,
            Approvals = w.Approvals.Where(a => !forStudent || a.Record.Action is ApprovalAction.Finalize or ApprovalAction.Publish or ApprovalAction.Amend or ApprovalAction.AppealResolved)
                .Select(a => $"{When(a.Record.At)} · {ActionLabel(a.Record.Action)} by {(forStudent ? RoleLabel(a.Record.ActorRole) : a.Actor + " (" + RoleLabel(a.Record.ActorRole) + ")")}" +
                             (string.IsNullOrWhiteSpace(a.Record.Note) || forStudent && a.Record.Action == ApprovalAction.Finalize ? "" : $": {a.Record.Note}")).ToList(),
            Appeals = w.Appeals.Select(a => $"{When(a.Appeal.OpenedAt)} · appeal {a.Appeal.State.ToString().ToLowerInvariant()}" +
                                            (a.Appeal.Resolution is { } r ? $": {r}" : "")).ToList(),
            IsMock = w.AiVisible && w.AnyMock, ForStudent = forStudent, IsApproved = w.HasFinalizeApproval,
            ScoreUse = scoreUse, ScoreUseReason = scoreUseReason, FollowUps = forStudent ? new Dictionary<string, int>() : followUps,
            Paths = forStudent ? new Dictionary<string, ObjectivePath>() : ObjectivePath.ForSession(w.Session, w.Objectives, w.AiVisible, w.Policy.AllowAssistedEvidence),
        };
    }

    /// <summary>
    /// What one objective lacked (GAD-T273): each rubric criterion scored below its maximum, with the panel's reason
    /// and what the next level asks for, the misconceptions noted, or the examiner's level and the next one. Retakes
    /// help only with feedback that says what was missing (research note 03: Rowland2014, PanRickard2018). Anything
    /// from the AI appears only once the AI evaluation is visible (blind grading, Part 8.3).
    /// </summary>
    private static IReadOnlyList<string> WhatWasMissing(ObjectiveView o, bool showAi)
    {
        var lines = new List<string>();
        var aiCriteria = 0;
        if (showAi && o.Ai is { } ai)
        {
            if (ai.Insufficient)
                lines.Add("Not enough evidence was gathered to judge this objective: next time, answer its questions in full, in your own words, with an example.");
            foreach (var c in ai.Criteria.Where(c => c.Score < c.MaxScore))
            {
                aiCriteria++;
                var line = $"{c.Criterion}: shown at {MasteryLevels.Label(c.Level)} ({N(c.Score)} of {N(c.MaxScore)}).";
                if (!string.IsNullOrWhiteSpace(c.Rationale)) line += $" {c.Rationale.Trim()}";
                if (NextLevel(o.Objective, c.Criterion, c.Level) is { } next) line += $" To reach {next.Label}: {next.Descriptor}";
                lines.Add(line);
            }
            foreach (var m in ai.Misconceptions.Take(3)) lines.Add($"Misconception to correct: {m.Text}");
        }
        // Without per-criterion detail, the examiner's level says what the next step is.
        if (aiCriteria == 0 && o.Human is { } h && h.Level < MasteryLevel.TransferAndCriticalReasoning)
        {
            if (h.Level == MasteryLevel.InsufficientEvidence)
                lines.Add("The examiner could not judge this objective from your answers: next time, explain it fully in your own words.");
            else if (NextLevel(o.Objective, null, h.Level) is { } next)
                lines.Add($"Examiner judged {MasteryLevels.Label(h.Level)}. To reach {next.Label}: {next.Descriptor}");
        }
        return lines;
    }

    /// <summary>The level above <paramref name="level"/> and its descriptor: the rubric's own wording when the criterion defines it.</summary>
    private static (string Label, string Descriptor)? NextLevel(LearningObjective objective, string? criterion, MasteryLevel level)
    {
        if (level >= MasteryLevel.TransferAndCriticalReasoning) return null;
        var next = level + 1;
        var own = criterion is null ? null
            : objective.Criteria.FirstOrDefault(c => string.Equals(c.Name, criterion, StringComparison.OrdinalIgnoreCase))?
                .Levels.FirstOrDefault(l => l.Level == next)?.Descriptor;
        return (MasteryLevels.Label(next), string.IsNullOrWhiteSpace(own) ? MasteryLevels.DefaultDescriptor(next) : own.Trim());
    }

    /// <summary>One line on the objective's change since the earlier attempt (reviewers also see whether its feedback came first).</summary>
    public string? ChangeLine(ReportObjective o)
    {
        if (o.Change is not { } d || o.PreviousAttempt is not { } attempt) return null;
        var line = $"Since attempt {attempt}: {N(o.PreviousCombined)} → {N(o.Combined)} ({(d > 0 ? "+" : "")}{N(d)} of {N(o.MaxScore)})";
        if (!ForStudent && o.FeedbackBeforeAttempt is { } fb)
            line += fb ? "; attempt " + attempt + "'s feedback was published before this attempt" : "; attempt " + attempt + "'s feedback was not published before this attempt";
        return line + ".";
    }

    public string ToMarkdown()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# Assessment report: {ExamTitle}");
        sb.AppendLine();
        if (IsMock) sb.AppendLine("> **MOCK**: the AI scores in this report come from the mock panel, not a genuine AI evaluation.").AppendLine();
        if (!IsApproved) sb.AppendLine("> **Preliminary**: this grade has not been approved. It is not a final grade.").AppendLine();
        sb.AppendLine($"- Student: {StudentName}");
        sb.AppendLine($"- Attempt: {Attempt}{(EndedAt is { } ended ? $", ended {When(ended)}" : "")}");
        sb.AppendLine($"- Status: **{Status}**");
        sb.AppendLine($"- Final grade: **{(FinalPercent is { } f ? Pct(f) : "not available")}**{(Label is { } l ? $" ({l})" : "")}");
        sb.AppendLine($"- Grading policy: {Policy}");
        sb.AppendLine();
        sb.AppendLine("## Learning objectives");
        sb.AppendLine();
        if (IsFormative) sb.AppendLine($"> **Formative scores**: {ScoreUseReason}").AppendLine();
        var showFollowUps = !ForStudent && FollowUps.Count > 0;
        sb.AppendLine("| Objective | Max | Weight | AI | Human | Combined | Agreement |" + (showFollowUps ? " Follow-ups |" : ""));
        sb.AppendLine("|---|---|---|---|---|---|---|" + (showFollowUps ? "---|" : ""));
        foreach (var o in Objectives)
            sb.AppendLine($"| {Cell(o.Title)} | {N(o.MaxScore)} | {N(o.Weight)} | {(o.AiExcluded ? "insufficient evidence" : N(o.AiScore))}{(o.AiIsMock ? " (MOCK)" : "")} | {N(o.HumanScore)} | {N(o.Combined)} | {o.Band ?? ""} |" +
                          (showFollowUps ? $" {FollowUps.GetValueOrDefault(o.Key)} |" : ""));
        foreach (var o in Objectives)
        {
            sb.AppendLine().AppendLine($"### {o.Title}");
            if (o.HumanLevel is not null) sb.AppendLine($"- Examiner: {o.HumanLevel}{(o.HumanComment is { } c ? $": {c}" : "")}");
            if (o.AiLevel is not null) sb.AppendLine($"- AI (preliminary{(o.AiIsMock ? ", MOCK" : "")}): {o.AiLevel}, confidence {o.AiConfidence}{(o.AiExplanation is { Length: > 0 } x ? $". {x}" : "")}");
            if (o.Reconciliation is not null) sb.AppendLine($"- Reconciliation: {o.Reconciliation}");
            if (ChangeLine(o) is { } change) sb.AppendLine($"- {change}");
            if (!ForStudent && Paths.GetValueOrDefault(o.Key) is { } path)
            {
                sb.AppendLine($"- Path: {path.MovesLine()}");
                if (path.CriteriaLine() is { } coverage) sb.AppendLine($"- Coverage: {coverage}");
                if (path.ExclusionsLine() is { } excluded && path.Questions > 0) sb.AppendLine($"- Exclusions: {excluded}");
            }
            if (o.WhatWasMissing.Count > 0)
            {
                sb.AppendLine("- What was missing:");
                foreach (var m in o.WhatWasMissing) sb.AppendLine($"  - {Quote(m)}");
            }
            foreach (var e in o.Evidence)
                sb.AppendLine($"- Evidence (response {e.Sequence}{(e.Corrected ? ", verified correction" : "")}): \"{Quote(e.VerifiedText)}\"");
        }
        sb.AppendLine().AppendLine("## Knowledge gaps").AppendLine();
        if (KnowledgeGaps.Count == 0) sb.AppendLine("None identified.");
        foreach (var g in KnowledgeGaps) sb.AppendLine($"- {g}");
        sb.AppendLine().AppendLine("## Approval").AppendLine();
        if (Approvals.Count == 0) sb.AppendLine("Not approved yet.");
        foreach (var a in Approvals) sb.AppendLine($"- {a}");
        if (Appeals.Count > 0)
        {
            sb.AppendLine().AppendLine("## Appeals").AppendLine();
            foreach (var a in Appeals) sb.AppendLine($"- {a}");
        }
        return sb.ToString();
    }

    public string ToCsv()
    {
        var sb = new StringBuilder();
        void Row(params string?[] cells) => sb.AppendJoin(',', cells.Select(Csv)).Append("\r\n");
        Row(["session_id", "student", "exam", "attempt", "status", "final_percent", "label", "objective_key", "objective", "max_score", "weight",
            "ai_score", "ai_level", "ai_confidence", "ai_excluded", "ai_mock", "human_score", "human_level", "combined", "band", "reconciliation", "evidence_responses",
            "what_was_missing", "previous_attempt", "previous_combined", "change", "feedback_before_attempt", .. ObjectivePathCsv.ObjectiveHeaders]);
        foreach (var o in Objectives)
            Row([SessionId.ToString(), StudentName, ExamTitle, Attempt.ToString(CultureInfo.InvariantCulture), Status, N(FinalPercent), Label, o.Key, o.Title, N(o.MaxScore), N(o.Weight),
                N(o.AiScore), o.AiLevel, o.AiConfidence, o.AiExcluded ? "yes" : "no", o.AiIsMock ? "yes" : "no", N(o.HumanScore), o.HumanLevel, N(o.Combined), o.Band,
                o.Reconciliation, string.Join(" ", o.Evidence.Select(e => e.Sequence)),
                string.Join(" | ", o.WhatWasMissing), o.PreviousAttempt?.ToString(CultureInfo.InvariantCulture), N(o.PreviousCombined), N(o.Change),
                o.FeedbackBeforeAttempt is { } fb ? fb ? "yes" : "no" : null,
                // GAD-T461: the objective's path, as on the grading page; criteria and exclusions empty while blind, none for students.
                .. ObjectivePathCsv.ObjectiveCells(ForStudent ? null : Paths.GetValueOrDefault(o.Key))]);
        return sb.ToString();
    }

    public string FileStem()
    {
        var raw = $"{StudentName}-{ExamTitle}-attempt{Attempt}-assessment";
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Length == 0 ? SessionId.ToString("N") : clean[..Math.Min(clean.Length, 90)];
    }

    /// <summary>
    /// Follow-up questions per objective key and the number of objectives examined, from the session's transcript
    /// (GAD-T267); null examined when there is no readable transcript.
    /// </summary>
    private static (IReadOnlyDictionary<string, int> FollowUps, int? Examined) ThreadLengths(ExamSession session)
    {
        if (string.IsNullOrWhiteSpace(session.TranscriptJson)) return (new Dictionary<string, int>(), null);
        try
        {
            var turns = SessionTranscriptSerializer.Deserialize(session.TranscriptJson).Turns.GroupBy(ExamObjectives.KeyOf, StringComparer.Ordinal).ToList();
            return (turns.ToDictionary(g => g.Key, g => g.Count(t => MoveProfiles.IsFollowUp(t.Move)), StringComparer.Ordinal), turns.Count);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return (new Dictionary<string, int>(), null);
        }
    }

    public static string Pct(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string N(decimal? v) => v is { } x ? x.ToString("0.##", CultureInfo.InvariantCulture) : "";
    private static string When(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ");
    private static string Quote(string s) { var t = s.Replace("\r", " ").Replace("\n", " "); return t.Length <= 400 ? t : t[..400] + "…"; }

    public static string ActionLabel(ApprovalAction a) => a switch
    {
        ApprovalAction.Finalize => "Final grade approved",
        ApprovalAction.Publish => "Published",
        ApprovalAction.Amend => "Amended",
        ApprovalAction.AppealOpened => "Appeal opened",
        ApprovalAction.AppealResolved => "Appeal resolved",
        _ => a.ToString(),
    };

    public static string RoleLabel(string? role) => role switch
    {
        "Instructor" => "Teacher",
        "SecondReviewer" => "Second reviewer",
        null or "" => "Gadfly",
        _ => role,
    };

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
