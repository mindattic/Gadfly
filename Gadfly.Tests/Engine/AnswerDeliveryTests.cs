using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>Adaptive Socratic probing (GAD-T233): how an answer was delivered steers how hard the next question presses.</summary>
[TestFixture]
public class AnswerDeliveryTests
{
    private static Turn Spoken(SocraticSession s, int minute, string text, double delay, double hesitationsPer100, int longPauses = 0, double? strain = null)
    {
        s.Ask("c", MoveKind.Probe, "Why?", T0.AddMinutes(minute));
        var m = new Dictionary<string, double>
        {
            [AnswerDelivery.TimeToFirstWordKey] = delay,
            [AnswerDelivery.DisfluencyPer100Key] = hesitationsPer100,
            [AnswerDelivery.LongPauseCountKey] = longPauses,
        };
        if (strain is { } x) m[AnswerDelivery.StrainVsBaselineKey] = x;
        s.RecordAnswer(new Answer { Text = text, Mode = "voice", CompletedAt = T0.AddMinutes(minute).AddSeconds(40), SpeechMetrics = m });
        return s.RecordJudgment(Judgment(0.6), 0.5);
    }

    [Test]
    public void HedgesIn_FindsEachHedgeOnce()
    {
        var h = AnswerDelivery.HedgesIn("I think it's maybe the light, I think, sort of. I’m not sure.");
        Assert.That(h, Is.EqualTo(new[] { "i think", "maybe", "sort of", "i'm not sure" }));
        Assert.That(AnswerDelivery.HedgesIn("Light drives the reaction."), Is.Empty);
    }

    [Test]
    public void SteadyAnswer_AsksAsPlanned()
    {
        var s = Session();
        var t = Spoken(s, 0, "Light drives the reaction in the chloroplast.", 2, 1);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Steady));
        Assert.That(d.Cues, Is.Empty);
    }

    [Test]
    public void SlowHesitantHedgedAnswer_AgainstTheStudentsBaseline_IsShaky()
    {
        var s = Session();
        Spoken(s, 0, "Light drives the reaction.", 2, 1);
        Spoken(s, 2, "The chloroplast captures it.", 3, 2);
        var t = Spoken(s, 4, "I think maybe it's sort of the water?", 14, 9, strain: 0.8);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Shaky));
        Assert.That(d.BaselineDelaySeconds, Is.EqualTo(2.5));
        Assert.That(d.Cues, Has.Count.EqualTo(4));
        Assert.That(AnswerDelivery.Guidance(d), Does.Contain("concrete example").And.Contain("counterfactual"));
    }

    [Test]
    public void SlowStudent_IsNotShakyForBeingSlow()
    {
        var s = Session();
        Spoken(s, 0, "Light drives the reaction.", 12, 8);
        Spoken(s, 2, "The chloroplast captures it.", 13, 9);
        var t = Spoken(s, 4, "Water is split to release oxygen.", 14, 9);
        Assert.That(AnswerDelivery.Read(s, t)!.Pressure, Is.EqualTo(ProbePressure.Steady), "measured against their own baseline");
    }

    [Test]
    public void QuestionPrompt_CarriesDeliveryAndProbing()
    {
        var q = ExamPrompts.Set[ExamPrompts.QuestionKey];
        Assert.That(q, Does.Contain("{delivery}").And.Contain("{probing}"));
    }
}
