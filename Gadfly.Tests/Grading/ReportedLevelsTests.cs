using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Review;
using NUnit.Framework;
using SessionReport = Gadfly.Engine.SessionReport;
using static Gadfly.Tests.Engine.EngineKit;

namespace Gadfly.Tests.Grading;

/// <summary>
/// GAD-T425: what is reported before a person grades is each learning objective's corroborated level (Level 0 = insufficient
/// evidence), never the examiner's running estimate or a mean answer score.
/// </summary>
[TestFixture]
public class ReportedLevelsTests
{
    private static SessionReport Report(params (string Key, int Level)[] objectives)
    {
        var r = SessionReport.From(Session());
        return r with
        {
            Recommendation = SessionReport.Mastered,
            OverallScore = 0.93,
            Objectives = objectives.Select(o => new ObjectiveReport
            {
                ObjectiveKey = o.Key, Title = o.Key, Status = o.Level == 0 ? "inconclusive" : "sufficient", Level = o.Level,
                LevelLabel = MasteryLevels.Label((MasteryLevel)o.Level),
            }).ToList(),
        };
    }

    [Test]
    public void Levels_ComeFromTheObjectives_WithLevelZeroAsInsufficientEvidence()
    {
        var levels = ReportedLevels.FromReportJson(Report(("a", 4), ("b", 0)).ToJson());
        Assert.That(levels.Select(l => l.Level), Is.EqualTo(new[] { 4, 0 }));
        Assert.That(levels[1].Insufficient, Is.True);
        Assert.That(ReportedLevels.Short(levels), Is.EqualTo("Levels 4, 0 (1 with insufficient evidence)"));
        Assert.That(ReportedLevels.Detail(levels), Does.Contain("b: Level 0"));
    }

    [Test]
    public void NoObjectives_OrUnreadableJson_GiveNoLevels()
    {
        Assert.That(ReportedLevels.FromReportJson(Report().ToJson()), Is.Empty);
        Assert.That(ReportedLevels.FromReportJson("not json"), Is.Empty);
        Assert.That(ReportedLevels.FromReportJson(null), Is.Empty);
        Assert.That(ReportedLevels.Short([]), Is.Null);
    }

    [Test]
    public void AdminScore_WithoutAFinalGrade_ShowsLevels_NeverTheRunningEstimate()
    {
        var shown = AdminPeople.Score(null, null, Report(("a", 3)).ToJson());
        Assert.That(shown, Does.Contain("level 3"));
        Assert.That(shown, Does.Not.Contain("%"), "no percentage stands in for a level");
        Assert.That(AdminPeople.Score(null, null, Report().ToJson()), Is.Null, "a report without levels shows nothing");
        Assert.That(AdminPeople.Score(82.5m, GradeStatus.Finalized, Report(("a", 3)).ToJson()), Does.StartWith("82.5%"));
    }
}
