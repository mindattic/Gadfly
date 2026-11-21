using Gadfly.Engine;
using NUnit.Framework;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Engine;

/// <summary>Recitation check (GAD-T242): a recited answer is not accepted, and the next question asks for it differently.</summary>
[TestFixture]
public class RecitationCheckTests
{
    private const string Source =
        "Photosynthesis is the process by which green plants use sunlight to synthesize foods from carbon dioxide and water. "
        + "In the light-dependent reactions, chlorophyll absorbs light energy, water molecules are split, and oxygen is released "
        + "as a by-product while the energy is stored in ATP and NADPH for the Calvin cycle.";

    private static Turn Answered(SocraticSession s, int minute, string text, double delay, double hesitationsPer100 = 0, int longPauses = 0)
    {
        s.Ask("c", MoveKind.Probe, "What happens?", T0.AddMinutes(minute));
        s.RecordAnswer(new Answer
        {
            Text = text, Mode = "voice", CompletedAt = T0.AddMinutes(minute).AddSeconds(50),
            SpeechMetrics = new Dictionary<string, double>
            {
                [AnswerDelivery.TimeToFirstWordKey] = delay,
                [AnswerDelivery.DisfluencyPer100Key] = hesitationsPer100,
                [AnswerDelivery.LongPauseCountKey] = longPauses,
            },
        });
        return s.Turns[^1];
    }

    [Test]
    public void AnswerInTheSourcesWords_IsFlagged()
    {
        var s = Session();
        var t = Answered(s, 0, "In the light-dependent reactions, chlorophyll absorbs light energy, water molecules are split, and oxygen is released as a by-product.", 3);
        var flag = RecitationCheck.Check(s, t, Source);
        Assert.That(flag, Is.Not.Null);
        Assert.That(flag!.MatchesSource, Is.True);
        Assert.That(flag.ReadOutPattern, Is.False);
        Assert.That(flag.LongestCopiedWords, Is.GreaterThanOrEqualTo(RecitationCheck.LongestCopiedCue));
        Assert.That(flag.CopiedText, Does.Contain("chlorophyll absorbs light energy"));
    }

    [Test]
    public void AnswerInTheStudentsOwnWords_IsNotFlagged()
    {
        var s = Session();
        var t = Answered(s, 0, "The plant catches sunlight with its green pigment, breaks water apart, and the oxygen we breathe comes out as leftovers, "
            + "while the captured energy gets packed away to build sugar later.", 3);
        Assert.That(RecitationCheck.Check(s, t, Source), Is.Null);
    }

    [Test]
    public void ShortQuotedTerm_IsNotFlagged()
    {
        var s = Session();
        var t = Answered(s, 0, "chlorophyll absorbs light energy, water molecules are split", 3);
        Assert.That(RecitationCheck.Check(s, t, Source), Is.Null);
    }

    [Test]
    public void LongFluentAnswerAfterALongSilentWait_IsAReadOutPattern()
    {
        var s = Session();
        Answered(s, 0, "Light drives it.", 3, 4);
        Answered(s, 2, "The water is split.", 4, 5);
        var words = string.Join(' ', Enumerable.Repeat("energy moves from one stage of the process to the next one", 7));
        var t = Answered(s, 4, words, 25, 0.5);
        var flag = RecitationCheck.Check(s, t, "");
        Assert.That(flag, Is.Not.Null);
        Assert.That(flag!.ReadOutPattern, Is.True);
        Assert.That(flag.Reason, Does.Contain("silent wait"));
    }

    [Test]
    public void LongHesitantAnswerAfterAWait_IsNotAReadOutPattern()
    {
        var s = Session();
        var words = string.Join(' ', Enumerable.Repeat("energy moves from one stage of the process to the next one", 7));
        var t = Answered(s, 0, words, 25, 8, longPauses: 3);
        Assert.That(RecitationCheck.Check(s, t, ""), Is.Null);
    }

    private static Turn ReadOutTurn(SocraticSession s)
    {
        Answered(s, 0, "Light drives it.", 3, 4);
        Answered(s, 2, "The water is split.", 4, 5);
        return Answered(s, 4, string.Join(' ', Enumerable.Repeat("energy moves from one stage of the process to the next one", 7)), 25, 0.5);
    }

    [Test]
    public void ReadOutPattern_IsSuppressedUnderAnAccommodation()
    {
        var s = Session(new SessionPolicy { TimeMultiplier = 1.5 });
        Assert.That(RecitationCheck.Check(s, ReadOutTurn(s), ""), Is.Null);
    }

    [Test]
    public void ReadOutPattern_IsSuppressedWhenTheExamSwitchesItOff_ButSourceOverlapStillFlags()
    {
        var s = Session(new SessionPolicy { ReadOutCheck = false });
        Assert.That(RecitationCheck.Check(s, ReadOutTurn(s), ""), Is.Null);
        var copied = Answered(s, 6, "In the light-dependent reactions, chlorophyll absorbs light energy, water molecules are split, and oxygen is released as a by-product.", 3);
        Assert.That(RecitationCheck.Check(s, copied, Source)?.MatchesSource, Is.True);
    }

    [Test]
    public void Measure_CountsFiresIncludingSuppressedOnes()
    {
        var plain = Session();
        ReadOutTurn(plain);
        var accommodated = Session(new SessionPolicy { TimeMultiplier = 1.5 });
        ReadOutTurn(accommodated);
        var quiet = Session();
        Answered(quiet, 0, "Light drives it.", 3, 4);

        var m = RecitationCheck.Measure([plain, accommodated, quiet]);
        Assert.That(m.Sessions, Is.EqualTo(3));
        Assert.That(m.EligibleAnswers, Is.EqualTo(2));
        Assert.That(m.Fires, Is.EqualTo(2));
        Assert.That(m.SuppressedFires, Is.EqualTo(1));
        Assert.That(m.AccommodatedFires, Is.EqualTo(1));
        Assert.That(m.SessionsFlagged, Is.EqualTo(2));
        Assert.That(m.FireRate, Is.EqualTo(1.0));
    }

    [Test]
    public void RecitedAnswer_SetsRecitedProbing_AndIsLeftOutOfTheEvidence()
    {
        var s = Session();
        var t = Answered(s, 0, "In the light-dependent reactions, chlorophyll absorbs light energy, water molecules are split, and oxygen is released as a by-product.", 3);
        var flag = RecitationCheck.Check(s, t, Source)!;
        t = s.RecordJudgment(Judgment(0.9) with { Recitation = flag }, 0.5);
        var d = AnswerDelivery.Read(s, t)!;
        Assert.That(d.Pressure, Is.EqualTo(ProbePressure.Recited));
        Assert.That(AnswerDelivery.Guidance(d), Does.Contain("own words").And.Contain("new case").And.Contain("why"));
        Assert.That(ObjectiveAssessor.IsRecited(t), Is.True);
    }
}
