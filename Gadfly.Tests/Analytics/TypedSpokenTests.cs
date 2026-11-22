using Gadfly.Server.Analytics;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// Typed versus spoken answers (GAD-T438): levels and panel-human agreement per side, the human gap at the same objective,
/// integrity-note rates, small sides withheld, and warnings when agreement or note rates differ. Pure.
/// </summary>
[TestFixture]
public class TypedSpokenTests
{
    private static readonly Guid Easy = Guid.NewGuid(), Hard = Guid.NewGuid();

    private static IEnumerable<ModeGrade> Side(bool typed, int students, int easyHuman, int hardHuman, int? panelOffset = 0)
    {
        for (var s = 0; s < students; s++)
        {
            var id = Guid.NewGuid();
            yield return new ModeGrade(typed, id, Easy, "Easy", panelOffset is { } o ? easyHuman + o : null, easyHuman);
            yield return new ModeGrade(typed, id, Hard, "Hard", panelOffset is { } p ? hardHuman + p : null, hardHuman);
        }
    }

    private static IEnumerable<ModeSession> Sessions(bool typed, int students, int notesEach) =>
        Enumerable.Range(0, students).Select(_ => new ModeSession(typed, Guid.NewGuid(), 10, notesEach));

    [Test]
    public void The_same_objective_gap_compares_like_with_like()
    {
        var grades = Side(true, 5, 4, 2).Concat(Side(false, 6, 5, 3)).ToList();
        var r = TypedSpoken.Compare(grades, []);
        Assert.That(r.Typed.MeanHuman, Is.EqualTo(3));
        Assert.That(r.Spoken.MeanHuman, Is.EqualTo(4));
        Assert.That(r.HumanGapSameObjective, Is.EqualTo(-1));
        Assert.That(r.Objectives, Has.Count.EqualTo(2));
        Assert.That(r.Typed.SameLevel, Is.EqualTo(1), "panel equal to human");
    }

    [Test]
    public void A_side_under_five_students_is_withheld()
    {
        var r = TypedSpoken.Compare(Side(true, 4, 4, 2).Concat(Side(false, 6, 5, 3)).ToList(), []);
        Assert.That(r.Typed.TooFew, Is.True);
        Assert.That(r.Typed.Students, Is.EqualTo(4));
        Assert.That(r.Typed.MeanHuman, Is.Null);
        Assert.That(r.HumanGapSameObjective, Is.Null);
        Assert.That(r.Warnings, Has.Some.Contains("can be compared once each side has"));
    }

    [Test]
    public void Agreement_that_differs_by_side_is_warned()
    {
        var r = TypedSpoken.Compare(Side(true, 5, 4, 2, panelOffset: 1).Concat(Side(false, 6, 4, 2)).ToList(), []);
        Assert.That(r.Typed.SameLevel, Is.EqualTo(0));
        Assert.That(r.Spoken.SameLevel, Is.EqualTo(1));
        Assert.That(r.PanelGapSameObjective, Is.EqualTo(1));
        Assert.That(r.Warnings, Has.Some.Contains("The panel and the human agree on"));
    }

    [Test]
    public void Integrity_note_rates_are_per_ten_answering_minutes()
    {
        var r = TypedSpoken.Compare(Side(true, 5, 3, 3).Concat(Side(false, 5, 3, 3)).ToList(),
            Sessions(true, 5, 2).Concat(Sessions(false, 5, 1)).ToList());
        Assert.That(r.Typed.NotesPer10Min, Is.EqualTo(2));
        Assert.That(r.Spoken.NotesPer10Min, Is.EqualTo(1));
        Assert.That(r.Typed.SessionsWithNotes, Is.EqualTo(1));
        Assert.That(r.Warnings, Has.Some.Contains("Integrity notes come more often with typed answers"));
    }

    [Test]
    public void Level_zero_and_missing_levels_stay_out_of_the_means()
    {
        var s = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var grades = s.SelectMany(id => new[]
        {
            new ModeGrade(true, id, Easy, "Easy", 3, 3),
            new ModeGrade(true, id, Hard, "Hard", null, 0),
        }).ToList();
        var side = TypedSpoken.Side(grades, []);
        Assert.That(side.MeanHuman, Is.EqualTo(3));
        Assert.That(side.Graded, Is.EqualTo(10));
        Assert.That(side.SameLevel, Is.EqualTo(1));
    }
}
