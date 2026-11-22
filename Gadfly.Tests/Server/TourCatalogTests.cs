using Gadfly.Server.Help;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Tours;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The guided-tour catalog and which tours Help offers each reader (GAD-T293). Pure, no browser.</summary>
[TestFixture]
public class TourCatalogTests
{
    [Test]
    public void Every_tour_has_a_unique_id_steps_and_ends_with_where_to_replay_it()
    {
        Assert.That(TourCatalog.All.Select(t => t.Id), Is.Unique);
        foreach (var t in TourCatalog.All)
        {
            Assert.That(t.Steps, Is.Not.Empty, t.Id);
            Assert.That(t.Steps[^1].Anchor, Is.EqualTo(".tour-launch"), t.Id);
            Assert.That(t.Steps.All(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Body)), t.Id);
            Assert.That(t.Page, Does.Not.StartWith("/"), t.Id);
            Assert.That(TourCatalog.Find(t.Id), Is.SameAs(t));
        }
    }

    [Test]
    public void No_tour_is_defined_for_the_exam_room_or_the_practice_interview()
    {
        Assert.That(TourCatalog.All.Select(t => t.Page), Has.None.StartsWith("exam/"));
        Assert.That(TourCatalog.All.Select(t => t.Page), Has.None.StartsWith("my-exams/practice"));
    }

    [Test]
    public void Replay_link_starts_the_tour_on_its_page()
    {
        Assert.That(TourCatalog.Find(TourIds.AdminStudents)!.ReplayHref, Is.EqualTo("admin/students?tour=admin-students"));
        Assert.That(TourCatalog.Find("unknown"), Is.Null);
        Assert.That(TourCatalog.Find(null), Is.Null);
    }

    [Test]
    public void Signed_out_readers_get_no_tours()
        => Assert.That(TourCatalog.ForReader(HelpViewer.Anonymous, isStudent: false), Is.Empty);

    [Test]
    public void A_student_sees_only_the_student_tours()
    {
        var ids = TourCatalog.ForReader(HelpViewer.For(true, OrgRoles.Student, false), isStudent: true).Select(t => t.Id).ToList();
        Assert.That(ids, Is.EquivalentTo(new[] { TourIds.MyExams, TourIds.GetReady }));
    }

    [Test]
    public void An_examiner_sees_the_grading_tours_but_not_the_admin_or_student_ones()
    {
        var ids = TourCatalog.ForReader(HelpViewer.For(true, OrgRoles.Examiner, false), isStudent: false).Select(t => t.Id).ToList();
        Assert.That(ids, Is.EquivalentTo(new[] { TourIds.GradingQueue, TourIds.GradingSession }));
    }

    [Test]
    public void An_admin_sees_every_staff_tour_and_no_student_tour()
    {
        var ids = TourCatalog.ForReader(HelpViewer.For(true, OrgRoles.Admin, false), isStudent: false).Select(t => t.Id).ToList();
        Assert.That(ids, Does.Contain(TourIds.AdminDashboard).And.Contain(TourIds.ReviewSession).And.Contain(TourIds.GradingQueue));
        Assert.That(ids, Does.Not.Contain(TourIds.MyExams).And.Not.Contain(TourIds.GetReady));
    }

    [Test]
    public void Platform_staff_see_every_tour()
        => Assert.That(TourCatalog.ForReader(HelpViewer.Everything, isStudent: false), Has.Count.EqualTo(TourCatalog.All.Count));
}
