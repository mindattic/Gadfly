using Gadfly.Server.Analytics;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// The fairness views in the analytics CSV export (GAD-T463): every table has its section, caveat and grouping line, a group under
/// five students keeps only its name and size (every other cell "withheld"), reported figures are written, and the cut-off rate
/// (GAD-T462) is a column of the path-driver table. Pure.
/// </summary>
[TestFixture]
public class FairnessCsvTests
{
    private static FairnessTables Tables()
    {
        var gaps = new ScoreGapsReport(
        [
            new ScoreGapGroup("No accommodation", 8, 20, 18, 3.1, 3.0, 0.1, 0.12, 0.6, 0.9, 0.05, 0.05),
            new ScoreGapGroup("Typed answers", 2, 0, 0, null, null, null, null, null, null, null, null),
        ], 20, 0, ["a warning"]);
        var paths = new PathDriversReport(
        [
            new PathDriverGroup("No accommodation", 8, 40, 0, 0.2, 0.1, 0.3, 0.1, 0.05, 0, 0.1, 0.02, 0.25, 0.1, 0.5, 0.2, CutOff: 0.125, CutOffMeasured: 32),
            new PathDriverGroup("Typed answers", 2, 0, null, null, null, null, null, null, null, null, null, null, null, null, null),
        ], 40, []);
        var modes = new TypedSpokenReport(ModeSide.Withheld with { Students = 2 }, new ModeSide(8, 8, 20, 3, 3.1, 0.6, 0.4, 0.5, 0.25), [], null, null, []);
        var gaze = new GazeRatesReport([new GazeRateGroup("No accommodation", 3, 0, 0, 0, 0, 0, 0, 0, 0)], 3, 0, []);
        var fade = new FadeCostReport(new FadeCostGroup("All students", 3, 1, 2, null, null, 0, null), [], 10, []);
        return new FairnessTables(gaps, paths, modes, gaze, fade);
    }

    [Test]
    public void Every_view_has_a_section_with_its_caveat()
    {
        var csv = FairnessCsv.ToCsv(Tables());
        foreach (var section in new[] { "fairness_score_gaps", "fairness_path_drivers", "fairness_typed_vs_spoken", "fairness_camera_episodes", "fairness_fade_cost" })
            Assert.That(csv, Does.Contain("section," + section + "\r\n"));
        Assert.That(csv, Does.Contain("cut_off_share_of_measured_spoken_answers"));
        Assert.That(csv, Does.Contain("warning,a warning"));
    }

    [Test]
    public void Small_groups_keep_only_name_and_size()
    {
        var lines = FairnessCsv.ToCsv(Tables()).Split("\r\n");
        var typedGap = lines.First(l => l.StartsWith("Typed answers,2,", StringComparison.Ordinal));
        Assert.That(typedGap.Split(',').Skip(2), Is.All.EqualTo(FairnessCsv.Withheld));
        var typedSide = lines.First(l => l.StartsWith("typed,2,", StringComparison.Ordinal));
        Assert.That(typedSide.Split(',').Skip(2), Is.All.EqualTo(FairnessCsv.Withheld));
        var camera = lines.First(l => l.StartsWith("No accommodation,3,", StringComparison.Ordinal));
        Assert.That(camera.Split(',').Skip(2), Is.All.EqualTo(FairnessCsv.Withheld));
        var fade = lines.First(l => l.StartsWith("All students,3,1,", StringComparison.Ordinal));
        Assert.That(fade.Split(',').Skip(3), Is.All.EqualTo(FairnessCsv.Withheld));
    }

    [Test]
    public void Reported_groups_carry_their_figures_and_cut_off_rate()
    {
        var row = FairnessCsv.ToCsv(Tables()).Split("\r\n").First(l => l.StartsWith("No accommodation,8,40,", StringComparison.Ordinal)).Split(',');
        Assert.That(row[13], Is.EqualTo("0.125"));
        Assert.That(row[14], Is.EqualTo("32"));
    }
}
