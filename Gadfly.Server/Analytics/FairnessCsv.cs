using System.Globalization;
using System.Text;
using Gadfly.Server.Data;

namespace Gadfly.Server.Analytics;

// The fairness views in the analytics CSV export (GAD-T463): fairness by group (score gaps GAD-T433, path drivers GAD-T434 with
// the cut-off rate GAD-T462), typed against spoken answers (GAD-T438), camera episodes and tracking quality (GAD-T261, GAD-T447)
// and what the screen fade costs (GAD-T448). The same tables the analytics page shows, grouped by the accommodations each
// assignment declared (a pasted grouping is used in memory on the page and never stored, so the export cannot repeat it). The
// same rules hold: groups under GroupFairness.MinStudents students keep only their name and size, and only Owners and Admins get
// these sections at all (GroupFairness.ViewerAllowed), since the figures carry panel levels and integrity notes.

public static class FairnessCsv
{
    /// <summary>What a withheld group's cells say in the export.</summary>
    public const string Withheld = "withheld";

    /// <summary>Every fairness table for one scope, by accommodation. Read-only, tenant-filtered.</summary>
    public static async Task<FairnessTables> BuildAsync(GadflyDbContext db, Guid organizationId, AnalyticsScope scope, CancellationToken ct = default)
    {
        var exams = await GroupFairness.ExamIdsAsync(db, organizationId, scope, ct);
        var sessions = await GroupFairness.SessionsAsync(db, organizationId, exams, ct);
        var groupOf = GroupFairness.Grouper(null);
        var gaps = await new ScoreGaps(db, organizationId).BuildAsync(sessions, groupOf, ct);
        var paths = await new PathDrivers(db, organizationId).BuildAsync(sessions, groupOf, ct);
        var modes = await new TypedSpoken(db, organizationId).BuildAsync(sessions, ct);
        var gaze = await new GazeFlagRates(db, organizationId).BuildAsync(exams, null, ct);
        var fade = await new FadeCost(db, organizationId).BuildAsync(sessions,
            s => s.GazeAccommodation ? GazeFlagRates.Accommodated : GazeFlagRates.NotAccommodated, ct);
        // GAD-T549 / GAD-T555: score gaps and path drivers again by exam language and by the student's stated primary language.
        var byExam = GroupFairness.Grouper(FairnessGrouping.ExamLanguage);
        var byPrimary = GroupFairness.Grouper(FairnessGrouping.PrimaryLanguage);
        return new FairnessTables(gaps, paths, modes, gaze, fade)
        {
            ByExamLanguage = new LanguageTables(await new ScoreGaps(db, organizationId).BuildAsync(sessions, byExam, ct),
                await new PathDrivers(db, organizationId).BuildAsync(sessions, byExam, ct)),
            ByPrimaryLanguage = new LanguageTables(await new ScoreGaps(db, organizationId).BuildAsync(sessions, byPrimary, ct),
                await new PathDrivers(db, organizationId).BuildAsync(sessions, byPrimary, ct)),
        };
    }

    /// <summary>The fairness sections, in the export's own section format (a blank line, then "section,&lt;name&gt;").</summary>
    public static string ToCsv(FairnessTables t)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string N(double? d, string f = "0.###") => d is { } x ? x.ToString(f, ci) : "";
        void Row(params object?[] cells) => sb.AppendJoin(',', cells.Select(c => AnalyticsEndpoints.Csv(c switch
        {
            null => "",
            double d => d.ToString("0.###", ci),
            IFormattable f => f.ToString(null, ci),
            _ => c.ToString(),
        }))).Append("\r\n");
        void Section(string title, string caveat, string grouping = "accommodations declared on each assignment")
        {
            sb.Append("\r\n");
            Row("section", title);
            Row("caveat", caveat);
            Row("grouping", grouping + "; groups under " + GroupFairness.MinStudents + " students are withheld");
        }
        void Warnings(IEnumerable<string> warnings) { foreach (var w in warnings) Row("warning", w); }
        object?[] Blank(int n, string first = Withheld) => [first, .. Enumerable.Repeat<object?>(Withheld, n - 1)];
        string I(MeanInterval? i) => i is null ? "" : N(i.Mean);
        string Lo(MeanInterval? i) => i is null ? "" : N(i.Low);
        string Hi(MeanInterval? i) => i is null ? "" : N(i.High);

        void Gaps(string name, ScoreGapsReport gaps, string grouping)
        {
            Section(name, ScoreGaps.Caveat, grouping);
            Row("group", "students", "objectives_graded_by_both", "conclusive_pairs", "mean_panel_level", "mean_human_level", "mean_panel_minus_human",
                "gap_in_standard_deviations", "same_level", "within_one_level", "panel_level_0_share", "human_level_0_share");
            foreach (var g in gaps.Groups)
                Row([g.Group, g.Students, .. g.TooFew ? Blank(10) : [g.Pairs, g.Conclusive, N(g.MeanPanel), N(g.MeanHuman), N(g.MeanGap), N(g.Smd),
                    N(g.SameLevel), N(g.WithinOne), N(g.PanelInconclusive), N(g.HumanInconclusive)]]);
            Warnings(gaps.Pairs == 0 ? [] : gaps.Warnings);
        }
        void Paths(string name, PathDriversReport paths, string grouping)
        {
            Section(name, PathDrivers.Caveat, grouping);
            Row("group", "students", "answers", "typed_share", "pressed_harder", "pressed_hard", "hedging", "vague_wording", "no_reason_or_example",
                "not_convincing", "evidence_against", "unclear_meaning", "recited", "cut_off_share_of_measured_spoken_answers", "measured_spoken_answers",
                "long_pauses_per_spoken_minute", "sessions_with_device_pause", "exam_time_paused_share");
            foreach (var g in paths.Groups)
                Row([g.Group, g.Students, .. g.TooFew ? Blank(16) : [g.Answers, N(g.TypedShare), N(g.PressureRaised), N(g.Shaky), N(g.Hedging), N(g.Vague),
                    N(g.NoReason), N(g.NotConvincing), N(g.EvidenceAgainst), N(g.Unclear), N(g.Recited), N(g.CutOff), g.CutOffMeasured,
                    N(g.LongPausesPerMinute), N(g.SessionsWithDevicePause), N(g.DevicePausedShare)]]);
            Warnings(paths.Answers == 0 ? [] : paths.Warnings);
        }

        // ---- score gaps (GAD-T433)
        Gaps("fairness_score_gaps", t.Gaps, "accommodations declared on each assignment");

        // ---- path drivers (GAD-T434) and cut-offs (GAD-T462)
        Paths("fairness_path_drivers", t.Paths, "accommodations declared on each assignment");

        // ---- the same by exam language (GAD-T549) and by stated primary language (GAD-T555)
        if (t.ByExamLanguage is { } byExam)
        {
            Gaps("fairness_score_gaps_by_exam_language", byExam.Gaps, "the language each attempt was taken in");
            Paths("fairness_path_drivers_by_exam_language", byExam.Paths, "the language each attempt was taken in");
        }
        if (t.ByPrimaryLanguage is { } byPrimary)
        {
            Gaps("fairness_score_gaps_by_primary_language", byPrimary.Gaps, "the primary language each student stated (asked, never inferred)");
            Paths("fairness_path_drivers_by_primary_language", byPrimary.Paths, "the primary language each student stated (asked, never inferred)");
        }

        // ---- typed against spoken (GAD-T438)
        var m = t.Modes;
        Section("fairness_typed_vs_spoken", TypedSpoken.Caveat);
        Row("answers", "students", "sessions", "objective_levels", "mean_human_level", "mean_panel_level", "panel_and_human_agree",
            "mean_absolute_difference_levels", "integrity_notes_per_10_min", "sessions_with_a_note");
        foreach (var (label, side) in new[] { ("typed", m.Typed), ("spoken", m.Spoken) })
            Row([label, side.Students, .. side.TooFew ? Blank(8) : [side.Sessions, side.Graded, N(side.MeanHuman), N(side.MeanPanel), N(side.SameLevel),
                N(side.MeanAbsGap), N(side.NotesPer10Min), N(side.SessionsWithNotes)]]);
        Row("typed_minus_spoken_human_level_same_objective", N(m.HumanGapSameObjective));
        Row("typed_minus_spoken_panel_level_same_objective", N(m.PanelGapSameObjective));
        if (m.Objectives.Count > 0)
        {
            Row("objective", "typed_students", "typed_mean_human_level", "typed_panel_and_human_agree", "spoken_students", "spoken_mean_human_level",
                "spoken_panel_and_human_agree");
            foreach (var o in m.Objectives)
                Row(o.Objective, o.Typed.Students, o.Typed.TooFew ? Withheld : N(o.Typed.MeanHuman), o.Typed.TooFew ? Withheld : N(o.Typed.SameLevel),
                    o.Spoken.Students, o.Spoken.TooFew ? Withheld : N(o.Spoken.MeanHuman), o.Spoken.TooFew ? Withheld : N(o.Spoken.SameLevel));
        }
        Warnings(m.Typed.Students == 0 ? [] : m.Warnings);

        // ---- eye-contact and camera episodes, tracking quality (GAD-T261, GAD-T447)
        Section("fairness_camera_episodes", GazeFlagRates.Caveat + " " + GazeFlagRates.FaceCaveat);
        Row("group", "students", "sessions", "answering_minutes", "look_aways_per_10_min", "reading_like_per_10_min", "fades_per_10_min",
            "time_faded_share", "sessions_with_a_fade", "spoken_requests_per_10_min", "no_face_per_10_min", "second_face_per_10_min",
            "changed_face_per_10_min", "sessions_with_a_quality_score", "median_tracking_quality", "poor_quality_share", "calibrated_share");
        foreach (var g in t.Gaze.Groups)
            Row([g.Group, g.Students, .. g.TooFew ? Blank(15) : [g.Sessions, N(g.AnswerMinutes, "0.0"), N(g.LookAwaysPer10Min),
                N(g.AnswerMinutes <= 0 ? null : g.Readings / g.AnswerMinutes * 10), N(g.FadesPer10Min), N(g.FadedShare), N(g.SessionsWithFadeShare),
                N(g.RemarksPer10Min), N(g.AbsentFacesPer10Min), N(g.SecondFacesPer10Min), N(g.ChangedFacesPer10Min), g.QualitySessions,
                N(g.MedianQuality, "0"), N(g.PoorQualityShare), N(g.CalibratedShare)]]);
        Warnings(t.Gaze.Sessions == 0 ? [] : t.Gaze.Warnings);

        // ---- what the fade costs (GAD-T448)
        Section("fairness_fade_cost", FadeCost.Caveat);
        Row("group", "students", "students_compared", "faded_answers", "judged_score_faded_minus_other", "score_low", "score_high",
            "answer_seconds_faded_minus_other", "seconds_low", "seconds_high", "students_compared_after", "next_answer_minus_clean", "next_low", "next_high");
        foreach (var g in t.Fade.Groups.Prepend(t.Fade.Overall))
            Row([g.Group, g.Students, g.Compared, .. g.TooFew ? Blank(11) : [
                // GAD-T486: the all-students count of faded answers would give a withheld group's away.
                ReferenceEquals(g, t.Fade.Overall) && t.Fade.OverallCountWithheld ? Withheld : (object)g.FadedAnswers, I(g.ScoreDiff), Lo(g.ScoreDiff), Hi(g.ScoreDiff),
                I(g.SecondsDiff), Lo(g.SecondsDiff), Hi(g.SecondsDiff), g.ComparedAfter, I(g.AfterScoreDiff), Lo(g.AfterScoreDiff), Hi(g.AfterScoreDiff)]]);
        Warnings(t.Fade.Overall.FadedAnswers == 0 ? [] : t.Fade.Warnings);
        return sb.ToString();
    }
}

/// <summary>The fairness views of one scope, by accommodation, as the export writes them.</summary>
public sealed record FairnessTables(ScoreGapsReport Gaps, PathDriversReport Paths, TypedSpokenReport Modes, GazeRatesReport Gaze, FadeCostReport Fade)
{
    /// <summary>Score gaps and path drivers by the language each attempt was taken in (GAD-T549).</summary>
    public LanguageTables? ByExamLanguage { get; init; }
    /// <summary>Score gaps and path drivers by the primary language each student stated (GAD-T555).</summary>
    public LanguageTables? ByPrimaryLanguage { get; init; }
}

/// <summary>The fairness tables that are repeated by language.</summary>
public sealed record LanguageTables(ScoreGapsReport Gaps, PathDriversReport Paths);
