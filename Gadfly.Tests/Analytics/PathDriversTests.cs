using Gadfly.Engine;
using Gadfly.Server.Analytics;
using Gadfly.Tests.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// Path-driver rates by group (GAD-T434): each probing cue is read back from a stored transcript exactly as the examiner computed
/// it, rates are per answer (the no-reason cue per answer whose question asked for one), small groups are withheld, and a cue
/// firing more for one group is warned with the English word-list caveat. Pure.
/// </summary>
[TestFixture]
public class PathDriversTests
{
    private static PathDriverAnswer Answer(string group, Guid student, bool hedging = false, bool vague = false, bool raised = false) =>
        new(group, student, false, 60, 1, hedging, vague, true, false, false, false, false, false,
            raised || hedging || vague ? ProbePressure.Some : ProbePressure.Steady);

    private static IEnumerable<PathDriverAnswer> Group(string group, int students, int answersEach, Func<int, bool> hedging)
    {
        var n = 0;
        for (var s = 0; s < students; s++)
        {
            var id = Guid.NewGuid();
            for (var a = 0; a < answersEach; a++) yield return Answer(group, id, hedging: hedging(n++));
        }
    }

    [Test]
    public void Read_recomputes_the_examiners_cues_from_the_transcript()
    {
        var s = Session();
        s.Judged("c", MoveKind.Probe, 0.8, 0.7, minute: 0, answer: "Light drives it because chlorophyll absorbs it.");
        s.Judged("c", MoveKind.Probe, 0.8, 0.7, minute: 2, answer: "I think maybe it's sort of the water and stuff, basically.");
        var (answers, session) = PathDrivers.Read("A", Guid.NewGuid(), s);
        Assert.That(answers, Has.Count.EqualTo(2));
        Assert.That(answers[0].Pressure, Is.EqualTo(ProbePressure.Steady));
        Assert.That(answers[1].Hedging, Is.True);
        Assert.That(answers[1].Vague, Is.True);
        Assert.That(answers[1].NoReason, Is.True);
        Assert.That(answers[1].Pressure, Is.EqualTo(ProbePressure.Shaky));
        Assert.That(answers[0].AnswerSeconds, Is.EqualTo(30));
        Assert.That(session, Is.Not.Null);
    }

    [Test]
    public void An_unanswered_transcript_has_no_session_row()
    {
        var s = Session();
        s.Ask("c", MoveKind.Elicit, "What is it?", T0);
        var (answers, session) = PathDrivers.Read("A", Guid.NewGuid(), s);
        Assert.That(answers, Is.Empty);
        Assert.That(session, Is.Null);
    }

    [Test]
    public void Rates_are_per_answer_and_small_groups_are_withheld()
    {
        var groups = PathDrivers.Summarize(Group("A", 5, 4, i => i % 4 == 0).Concat(Group("B", 3, 4, _ => true)), []);
        var a = groups.Single(g => g.Group == "A");
        Assert.That(a.Answers, Is.EqualTo(20));
        Assert.That(a.Hedging, Is.EqualTo(0.25));
        Assert.That(a.PressureRaised, Is.EqualTo(0.25));
        Assert.That(a.LongPausesPerMinute, Is.EqualTo(1));
        var b = groups.Single(g => g.Group == "B");
        Assert.That(b.TooFew, Is.True);
        Assert.That(b.Hedging, Is.Null);
    }

    /// <summary>GAD-T480: answers whose pauses were not measured (voice measurements off, older answers) don't dilute the rate.</summary>
    [Test]
    public void Long_pauses_are_a_rate_over_measured_answers_only()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var answers = ids.SelectMany(s => new[]
        {
            new PathDriverAnswer("A", s, false, 60, 2, false, false, false, false, false, false, false, false, ProbePressure.Steady),
            new PathDriverAnswer("A", s, false, 60, 0, false, false, false, false, false, false, false, false, ProbePressure.Steady, PausesMeasured: false),
        });
        Assert.That(PathDrivers.Summarize(answers, []).Single().LongPausesPerMinute, Is.EqualTo(2));
    }

    [Test]
    public void No_reason_counts_only_answers_whose_question_asked_for_one()
    {
        var id = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var answers = id.SelectMany(s => new[]
        {
            new PathDriverAnswer("A", s, false, 30, 0, false, false, true, true, false, false, false, false, ProbePressure.Some),
            new PathDriverAnswer("A", s, false, 30, 0, false, false, false, false, false, false, false, false, ProbePressure.Steady),
        });
        Assert.That(PathDrivers.Summarize(answers, []).Single().NoReason, Is.EqualTo(1));
    }

    [Test]
    public void A_cue_firing_more_for_one_group_is_warned_with_the_english_caveat()
    {
        var groups = PathDrivers.Summarize(Group("A", 5, 4, i => i % 4 == 0).Concat(Group("B", 5, 4, i => i % 2 == 0)), []);
        var w = PathDrivers.Warnings(groups);
        Assert.That(w, Has.Some.Contains("The hedging cue differs between groups").And.Some.Contains("English"));
        Assert.That(w, Has.Some.Contains("Raised probing pressure"));
    }

    [Test]
    public void Equal_rates_raise_no_warning()
    {
        var groups = PathDrivers.Summarize(Group("A", 5, 4, i => i % 4 == 0).Concat(Group("B", 5, 4, i => i % 4 == 0)), []);
        Assert.That(PathDrivers.Warnings(groups), Is.Empty);
    }

    [Test]
    public void Device_pauses_are_a_share_of_exam_time()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var answers = ids.Select(s => Answer("A", s));
        var sessions = ids.Select((s, i) => new PathDriverSession("A", s, 30, i == 0 ? 15 : 0, 0));
        var a = PathDrivers.Summarize(answers, sessions).Single();
        Assert.That(a.SessionsWithDevicePause, Is.EqualTo(0.2));
        Assert.That(a.DevicePausedShare, Is.EqualTo(0.1));
    }
}
