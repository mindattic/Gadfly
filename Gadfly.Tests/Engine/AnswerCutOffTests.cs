using Gadfly.Engine;
using Gadfly.Server.Analytics;
using Gadfly.Server.Voice;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Answer cut-offs (GAD-T462): a spoken answer is watched (cutoff.count 0) when it is built, the exam room's delay is range-checked,
/// only a watched spoken answer can be marked (once), turning voice measurements off removes the watch so an EU answer can never be
/// marked, the answer's text and judgment are untouched, the delivery cues ignore it, and the path-driver rates count it only over
/// watched spoken answers. Pure.
/// </summary>
[TestFixture]
public class AnswerCutOffTests
{
    private static readonly IReadOnlyDictionary<string, double> Watched = AnswerCutOff.Watched(new Dictionary<string, double> { ["wordCount"] = 12 });

    private static SocraticSession SpokenSession(IReadOnlyDictionary<string, double> metrics)
    {
        var s = Session();
        s.Ask("c", MoveKind.Probe, "Why does it happen?", T0);
        s.RecordAnswer(new Answer { Text = "Because the light is absorbed", Mode = "voice", CompletedAt = T0.AddSeconds(30), SpeechMetrics = metrics });
        s.RecordJudgment(Judgment(0.7), 0.6);
        return s;
    }

    [TestCase(0)]
    [TestCase(1.5)]
    [TestCase(AnswerCutOff.WindowSeconds)]
    public void Delays_inside_the_window_are_in_range(double seconds) => Assert.That(AnswerCutOff.InRange(seconds), Is.True);

    [TestCase(-0.1)]
    [TestCase(AnswerCutOff.WindowSeconds + 0.01)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void Delays_outside_the_window_are_refused(double seconds) => Assert.That(AnswerCutOff.InRange(seconds), Is.False);

    [Test]
    public void A_watched_answer_is_marked_once_with_its_delay()
    {
        var marked = AnswerCutOff.Mark(Watched, 1.234)!;
        Assert.That(AnswerCutOff.Read(marked), Is.True);
        Assert.That(AnswerCutOff.ResumeSeconds(marked), Is.EqualTo(1.23));
        Assert.That(AnswerCutOff.Mark(marked, 1), Is.Null, "already marked");
        Assert.That(AnswerCutOff.Read(Watched), Is.False);
        Assert.That(AnswerCutOff.ResumeSeconds(Watched), Is.Null);
    }

    [Test]
    public void An_unwatched_answer_is_not_measured_and_cannot_be_marked()
    {
        var older = new Dictionary<string, double> { ["wordCount"] = 12 };
        Assert.That(AnswerCutOff.Read(older), Is.Null);
        Assert.That(AnswerCutOff.Mark(older, 1), Is.Null);
        Assert.That(AnswerCutOff.Mark(Watched, 9), Is.Null, "out of range");
    }

    [Test]
    public void Voice_measurements_off_removes_the_watch()
    {
        var off = VoiceStrain.TurnOff(Watched);
        Assert.That(off.Keys.Any(k => k.StartsWith(AnswerCutOff.Prefix, StringComparison.Ordinal)), Is.False);
        Assert.That(AnswerCutOff.Mark(off, 1), Is.Null);
    }

    [Test]
    public void The_session_marks_only_the_metrics_of_a_spoken_answer()
    {
        var s = SpokenSession(Watched);
        var before = s.Turns[0];
        Assert.That(s.NoteAnswerCutOff(0, 2), Is.True);
        var after = s.Turns[0];
        Assert.That(AnswerCutOff.Read(after.Answer!.SpeechMetrics), Is.True);
        Assert.That(after.Answer.Text, Is.EqualTo(before.Answer!.Text));
        Assert.That(after.Judgment, Is.SameAs(before.Judgment));
        Assert.That(after.MasteryAfter, Is.EqualTo(before.MasteryAfter));
        Assert.That(s.NoteAnswerCutOff(0, 2), Is.False, "once per answer");
        Assert.That(s.NoteAnswerCutOff(5, 2), Is.False, "no such turn");
    }

    [Test]
    public void A_typed_answer_is_never_marked()
    {
        var s = Session();
        s.Ask("c", MoveKind.Probe, "Why?", T0);
        s.RecordAnswer(new Answer { Text = "typed", Mode = "text", CompletedAt = T0.AddSeconds(30), SpeechMetrics = Watched });
        Assert.That(s.NoteAnswerCutOff(0, 1), Is.False);
    }

    [Test]
    public void The_delivery_cues_ignore_a_cut_off()
    {
        var plain = SpokenSession(Watched);
        var cut = SpokenSession(Watched);
        cut.NoteAnswerCutOff(0, 1);
        var a = AnswerDelivery.Read(plain, plain.Turns[0]);
        var b = AnswerDelivery.Read(cut, cut.Turns[0]);
        Assert.That(b?.Pressure, Is.EqualTo(a?.Pressure));
        Assert.That(b?.Cues, Is.EqualTo(a?.Cues));
    }

    [Test]
    public void Path_driver_rates_count_cut_offs_over_watched_spoken_answers_only()
    {
        PathDriverAnswer A(Guid student, bool? cut) =>
            new("A", student, cut is null, 60, 0, false, false, false, false, false, false, false, false, ProbePressure.Steady, cut);
        var answers = Enumerable.Range(0, 5).SelectMany(_ =>
        {
            var id = Guid.NewGuid();
            return new[] { A(id, true), A(id, false), A(id, false), A(id, false), A(id, null) };
        }).ToList();
        var g = PathDrivers.Summarize(answers, []).Single();
        Assert.That(g.CutOffMeasured, Is.EqualTo(20));
        Assert.That(g.CutOff, Is.EqualTo(0.25));
    }
}
