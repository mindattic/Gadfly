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
        return new FairnessTables(gaps, paths, modes, gaze, fade);
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
        void Section(string title, string caveat)
        {
            sb.Append("\r\n");
            Row("section", title);
            Row("caveat", caveat);
            Row("grouping", "accommodations declared on each assignment; groups under " + GroupFairness.MinStudents + " students are withheld");
        }
        void Warnings(IEnumerable<string> warnings) { foreach (var w in warnings) Row("warning", w); }
        object?[] Blank(int n, string first = Withheld) => [first, .. Enumerable.Repeat<object?>(Withheld, n - 1)];
        string I(MeanInterval? i) => i is null ? "" : N(i.Mean);
        string Lo(MeanInterval? i) => i is null ? "" : N(i.Low);
        string Hi(MeanInterval? i) => i is null ? "" : N(i.High);

        // ---- score gaps (GAD-T433)
        Section("fairness_score_gaps", ScoreGaps.Caveat);
        Row("group", "students", "objectives_graded_by_both", "conclusive_pairs", "mean_panel_level", "mean_human_level", "mean_panel_minus_human",
            "gap_in_standard_deviations", "same_level", "within_one_level", "panel_level_0_share", "human_level_0_share");
        foreach (var g in t.Gaps.Groups)
            Row([g.Group, g.Students, .. g.TooFew ? Blank(10) : [g.Pairs, g.Conclusive, N(g.MeanPanel), N(g.MeanHuman), N(g.MeanGap), N(g.Smd),
                N(g.SameLevel), N(g.WithinOne), N(g.PanelInconclusive), N(g.HumanInconclusive)]]);
        Warnings(t.Gaps.Pairs == 0 ? [] : t.Gaps.Warnings);

        // ---- path drivers (GAD-T434) and cut-offs (GAD-T462)
        Section("fairness_path_drivers", PathDrivers.Caveat);
        Row("group", "students", "answers", "typed_share", "pressed_harder", "pressed_hard", "hedging", "vague_wording", "no_reason_or_example",
            "not_convincing", "evidence_against", "unclear_meaning", "recited", "cut_off_share_of_measured_spoken_answers", "measured_spoken_answers",
            "long_pauses_per_spoken_minute", "sessions_with_device_pause", "exam_time_paused_share");
        foreach (var g in t.Paths.Groups)
            Row([g.Group, g.Students, .. g.TooFew ? Blank(16) : [g.Answers, N(g.TypedShare), N(g.PressureRaised), N(g.Shaky), N(g.Hedging), N(g.Vague),
                N(g.NoReason), N(g.NotConvincing), N(g.EvidenceAgainst), N(g.Unclear), N(g.Recited), N(g.CutOff), g.CutOffMeasured,
                N(g.LongPausesPerMinute), N(g.SessionsWithDevicePause), N(g.DevicePausedShare)]]);
        Warnings(t.Paths.Answers == 0 ? [] : t.Paths.Warnings);

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
            Row([g.Group, g.Students, g.Compared, .. g.TooFew ? Blank(11) : [g.FadedAnswers, I(g.ScoreDiff), Lo(g.ScoreDiff), Hi(g.ScoreDiff),
                I(g.SecondsDiff), Lo(g.SecondsDiff), Hi(g.SecondsDiff), g.ComparedAfter, I(g.AfterScoreDiff), Lo(g.AfterScoreDiff), Hi(g.AfterScoreDiff)]]);
        Warnings(t.Fade.Overall.FadedAnswers == 0 ? [] : t.Fade.Warnings);
        return sb.ToString();
    }
}

/// <summary>The fairness views of one scope, by accommodation, as the export writes them.</summary>
public sealed record FairnessTables(ScoreGapsReport Gaps, PathDriversReport Paths, TypedSpokenReport Modes, GazeRatesReport Gaze, FadeCostReport Fade);
