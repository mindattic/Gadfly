using Gadfly.Assessment;
using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>
/// Adaptive Socratic probing (GAD-T233): what an answer said steers how hard the next question presses. Since GAD-T258 only
/// content cues and the judges' reading count, never the voice, the wait before answering or hesitations.
/// </summary>
[TestFixture]
public class AnswerDeliveryTests
{
    private static Turn Spoken(SocraticSession s, int minute, string text, double delay, double hesitationsPer100, int longPauses = 0,
        double? strain = null, double score = 0.6, MoveKind move = MoveKind.Probe, ResponseAnalysis? analysis = null)
    {
        s.Ask("c", move, "Why?", T0.AddMinutes(minute));
        var m = new Dictionary<string, double>
        {
            [AnswerDelivery.TimeToFirstWordKey] = delay,
            [AnswerDelivery.DisfluencyPer100Key] = hesitationsPer100,
            [AnswerDelivery.LongPauseCountKey] = longPauses,
        };
        if (strain is { } x) m[AnswerDelivery.StrainVsBaselineKey] = x;
        s.RecordAnswer(new Answer { Text = text, Mode = "voice", CompletedAt = T0.AddMinutes(minute).AddSeconds(40), SpeechMetrics = m });
        return s.RecordJudgment(Judgment(score), 0.5, analysis);
    }

    [Test]
    public void HedgesIn_FindsEachHedgeOnce()
    {
        var h = AnswerDelivery.HedgesIn("I think it's maybe the light, I think, sort of. I’m not sure.");
        Assert.That(h, Is.EqualTo(new[] { "i think", "maybe", "sort of", "i'm not sure" }));
        Assert.That(AnswerDelivery.HedgesIn("Light drives the reaction."), Is.Empty);
    }

    [Test]
    public void VagueIn_AndReasons_ReadTheWords()
    {
        Assert.That(AnswerDelivery.VagueIn("It does stuff with the light and so on, basically."), Is.EqualTo(new[] { "stuff", "and so on", "basically" }));
        Assert.That(AnswerDelivery.HasReasonOrExample("Water is split because light excites chlorophyll."), Is.True);
        Assert.That(AnswerDelivery.HasReasonOrExample("For example, a leaf in the dark stops."), Is.True);
        Assert.That(AnswerDelivery.HasReasonOrExample("Water is split in the chloroplast."), Is.False);
    }

    [Test]
    public void SteadyAnswer_AsksAsPlanned()
    {
        var s = Session();
        var t = Spoken(s, 0, "Light drives the reaction in the chloroplast because chlorophyll absorbs it.", 2, 1);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Steady));
        Assert.That(d.Cues, Is.Empty);
    }

    [Test]
    public void HedgedVagueUnreasonedAnswer_IsShaky()
    {
        var s = Session();
        var t = Spoken(s, 0, "I think maybe it's sort of the water and stuff, basically.", 2, 1);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Shaky));
        Assert.That(d.Cues, Has.Count.EqualTo(3), "hedging, vague wording, no reason");
        Assert.That(AnswerDelivery.Guidance(d), Does.Contain("concrete example").And.Contain("counterfactual"));
    }

    [Test]
    public void SlowHesitantStrainedVoice_WithAReasonedAnswer_IsSteady()
    {
        var s = Session();
        Spoken(s, 0, "Light drives the reaction because chlorophyll absorbs it.", 2, 1);
        Spoken(s, 2, "The chloroplast captures it, so energy is stored.", 3, 2);
        var t = Spoken(s, 4, "Water is split because the light energy is used to pull electrons from it.", 14, 9, longPauses: 3, strain: 0.9);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Steady), "timing and voice never steer probing (GAD-T258)");
        Assert.That(d.DelaySeconds, Is.EqualTo(14), "still recorded");
        Assert.That(d.BaselineDelaySeconds, Is.EqualTo(2.5));
        Assert.That(d.StrainVsBaseline, Is.EqualTo(0.9), "still recorded");
        Assert.That(d.Line(), Does.Not.Contain("14").And.Not.Contain("0.9").And.Not.Contain("hesitation"), "never in the prompt");
    }

    [Test]
    public void JudgedWeak_WithEvidenceAgainst_IsShaky()
    {
        var s = Session();
        var analysis = new ResponseAnalysis
        {
            Voters = 3,
            Misconceptions = [new AnalysisMisconception("o1", "thinks oxygen comes from carbon dioxide", "oxygen comes from the CO2")],
        };
        var t = Spoken(s, 0, "The oxygen comes from the CO2 because plants breathe it in.", 2, 1, score: 0.3, analysis: analysis);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Shaky));
        Assert.That(d.Cues, Has.Some.Contains("not find it convincing").And.Some.Contains("carbon dioxide"));
    }

    [Test]
    public void ElicitMove_DoesNotAskForAReason()
    {
        var s = Session();
        var t = Spoken(s, 0, "It is how plants make food from light.", 2, 1, move: MoveKind.Elicit);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.GaveReasonOrExample, Is.Null);
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Steady));
    }

    [Test]
    public void QuestionPrompt_CarriesDeliveryAndProbing()
    {
        var q = ExamPrompts.Set[ExamPrompts.QuestionKey];
        Assert.That(q, Does.Contain("{delivery}").And.Contain("{probing}"));
    }
}
