using Microsoft.Extensions.DependencyInjection;
using Gadfly.Server.Data;
using Gadfly.Server.Exams;
using Gadfly.Server.Sources;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Decision D18 (GAD-T170): an Instructor sees and manages only the courses, collections and assessments they created
/// (and the assignments and sessions under them); Owner and Admin see the whole organization; a direct visit to another
/// instructor's item is refused as not found and audited as access.denied; the organization setting reverses it.
/// </summary>
[TestFixture]
public class InstructorOwnershipTests : ServerTest
{
    private sealed record World(Guid Org, Guid TeacherA, Guid TeacherB, Guid ExamA, Guid ExamB, Guid CollA, Guid CollB, Guid CourseA, Guid CourseB,
        Guid AssignmentB, Guid SessionB);

    private static async Task<World> NewWorldAsync(bool scoped = true)
    {
        var org = await Seed.OrganizationAsync(instructorsSeeOnlyOwn: scoped);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var db = SystemDb();
        var now = DateTimeOffset.UtcNow;
        (Guid Course, Guid Coll, Guid Exam, Guid Assignment, Guid Session) Make(Guid teacher, string tag)
        {
            var course = new Course { OrganizationId = org, Title = "Course " + tag, Subject = "Biology", CreatedByUserId = teacher, CreatedAt = now };
            var coll = new SourceCollection { OrganizationId = org, CourseId = course.Id, Title = "Notes " + tag, CreatedByUserId = teacher, CreatedAt = now, Status = ProcessingStatus.Ready };
            var exam = new Exam { OrganizationId = org, CollectionId = coll.Id, CourseId = course.Id, Title = "Exam " + tag, Subject = "Biology", Status = ExamStatus.Published, CreatedByUserId = teacher, CreatedAt = now, PublishedAt = now };
            var assignment = new ExamAssignment { OrganizationId = org, ExamId = exam.Id, StudentUserId = Guid.NewGuid(), AssignedByUserId = teacher, AssignedAt = now };
            var session = new ExamSession { OrganizationId = org, AssignmentId = assignment.Id, ExamId = exam.Id, StudentUserId = assignment.StudentUserId, State = SessionState.Ended, CreatedAt = now };
            db.Courses.Add(course);
            db.SourceCollections.Add(coll);
            db.Exams.Add(exam);
            db.ExamAssignments.Add(assignment);
            db.ExamSessions.Add(session);
            return (course.Id, coll.Id, exam.Id, assignment.Id, session.Id);
        }
        var ta = Make(a, "A");
        var tb = Make(b, "B");
        await db.SaveChangesAsync();
        return new(org, a, b, ta.Exam, tb.Exam, ta.Coll, tb.Coll, ta.Course, tb.Course, tb.Assignment, tb.Session);
    }

    [Test]
    public async Task ScopedInstructor_SeesOnlyOwnCoursesCollectionsExamsAssignmentsAndSessions()
    {
        var w = await NewWorldAsync();
        await using var db = Db(Member(w.Org, w.TeacherA, OrgRoles.Instructor));
        Assert.That(await db.Exams.Select(x => x.Id).ToListAsync(), Is.EqualTo(new[] { w.ExamA }));
        Assert.That(await db.SourceCollections.Select(x => x.Id).ToListAsync(), Is.EqualTo(new[] { w.CollA }));
        Assert.That(await db.Courses.Select(x => x.Id).ToListAsync(), Is.EqualTo(new[] { w.CourseA }));
        Assert.That(await db.ExamAssignments.AnyAsync(x => x.ExamId == w.ExamB), Is.False);
        Assert.That(await db.ExamAssignments.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.ExamSessions.AnyAsync(x => x.Id == w.SessionB), Is.False);
        Assert.That(await db.ExamSessions.CountAsync(), Is.EqualTo(1));
    }

    [TestCase(OrgRoles.Owner)]
    [TestCase(OrgRoles.Admin)]
    [TestCase(OrgRoles.Examiner)]
    [TestCase(OrgRoles.SecondReviewer)]
    public async Task OwnerAdminAndGradingRoles_SeeTheWholeOrganization(string role)
    {
        var w = await NewWorldAsync();
        await using var db = Db(Member(w.Org, Guid.NewGuid(), role));
        Assert.That(await db.Exams.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.SourceCollections.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.Courses.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.ExamSessions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Students_AndSystemWork_AreNotNarrowedByOwnership()
    {
        var w = await NewWorldAsync();
        await using (var sys = SystemDb()) Assert.That(await sys.Exams.CountAsync(x => x.OrganizationId == w.Org), Is.EqualTo(2));
        await using var student = Db(Member(w.Org, Guid.NewGuid(), OrgRoles.Student));
        Assert.That(await student.Exams.CountAsync(), Is.EqualTo(2), "students are limited by their assignments, not by exam ownership");
    }

    [Test]
    public async Task OrganizationSettingOff_RestoresOrganizationWideInstructorAccess()
    {
        var w = await NewWorldAsync(scoped: false);
        await using var db = Db(Member(w.Org, w.TeacherA, OrgRoles.Instructor));
        Assert.That(await db.Exams.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.ExamSessions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task TurningTheSettingOn_AppliesOnceTheCacheIsInvalidated()
    {
        var w = await NewWorldAsync(scoped: false);
        await using (var db = Db(Member(w.Org, w.TeacherA, OrgRoles.Instructor))) Assert.That(await db.Exams.CountAsync(), Is.EqualTo(2));
        await using (var sys = SystemDb())
            await sys.OrganizationSettings.Where(s => s.OrganizationId == w.Org).ExecuteUpdateAsync(u => u.SetProperty(s => s.InstructorsSeeOnlyOwn, true));
        OwnershipPolicy.Invalidate(w.Org);
        await using (var db = Db(Member(w.Org, w.TeacherA, OrgRoles.Instructor))) Assert.That(await db.Exams.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DirectVisit_ToAnotherInstructorsItems_IsRefusedAndAuditedAsAccessDenied()
    {
        var w = await NewWorldAsync();
        var me = Member(w.Org, w.TeacherA, OrgRoles.Instructor);

        await using (var scope = Scope(me))
        {
            var sp = scope.ServiceProvider;
            Assert.That(await sp.GetRequiredService<Gadfly.Server.Assessments.AssessmentService>().GetAsync(w.ExamB), Is.Null);
            Assert.That(await sp.GetRequiredService<ExamService>().GetAsync(w.ExamB), Is.Null);
            Assert.That(await sp.GetRequiredService<SourceLibraryService>().GetCollectionAsync(w.Org, w.CollB), Is.Null);
            Assert.That(await sp.GetRequiredService<Gadfly.Server.Review.ReviewService>().GetAsync(w.SessionB), Is.Null);
            // Their own is still there, and an id that does not exist is just missing (no audit).
            Assert.That(await sp.GetRequiredService<ExamService>().GetAsync(w.ExamA), Is.Not.Null);
            Assert.That(await sp.GetRequiredService<ExamService>().GetAsync(Guid.NewGuid()), Is.Null);
        }

        await using var sys = SystemDb();
        var denied = await sys.AuditEvents.Where(a => a.OrganizationId == w.Org && a.Action == "access.denied" && a.ActorUserId == w.TeacherA).ToListAsync();
        Assert.That(denied.Select(d => d.EntityId), Is.SupersetOf(new[] { w.ExamB.ToString(), w.CollB.ToString(), w.SessionB.ToString() }));
        Assert.That(denied.Any(d => d.EntityId == w.ExamA.ToString()), Is.False);
        Assert.That(denied.All(d => d.ActorRole == OrgRoles.Instructor), Is.True);
    }

    [Test]
    public async Task OwnerOpeningAnotherInstructorsItem_IsNotRefusedOrAudited()
    {
        var w = await NewWorldAsync();
        await using var scope = Scope(Member(w.Org, Guid.NewGuid(), OrgRoles.Owner));
        Assert.That(await scope.ServiceProvider.GetRequiredService<ExamService>().GetAsync(w.ExamB), Is.Not.Null);
        await using var sys = SystemDb();
        Assert.That(await sys.AuditEvents.AnyAsync(a => a.OrganizationId == w.Org && a.Action == "access.denied"), Is.False);
    }

    [Test]
    public async Task Instructor_CannotBuildOnAnotherInstructorsCollection()
    {
        var w = await NewWorldAsync();
        await using var scope = Scope(Member(w.Org, w.TeacherA, OrgRoles.Instructor));
        var options = await scope.ServiceProvider.GetRequiredService<ExamService>().ReadyCollectionsAsync();
        Assert.That(options.Select(o => o.Id), Does.Not.Contain(w.CollB));
    }

    [Test]
    public async Task GradingActions_OnAnotherInstructorsSession_AreRefusedAndAudited()
    {
        var w = await NewWorldAsync();
        await using (var sys = SystemDb())
        {
            sys.FinalGrades.Add(new FinalGrade { SessionId = w.SessionB, OrganizationId = w.Org, Status = Gadfly.Assessment.GradeStatus.Finalized, UpdatedAt = DateTimeOffset.UtcNow });
            sys.ApprovalRecords.Add(new ApprovalRecord { OrganizationId = w.Org, SessionId = w.SessionB, Action = ApprovalAction.Finalize, ActorUserId = w.TeacherB, ActorRole = OrgRoles.Instructor, At = DateTimeOffset.UtcNow });
            await sys.SaveChangesAsync();
        }
        await using (var scope = Scope(Member(w.Org, w.TeacherA, OrgRoles.Instructor)))
        {
            var grading = scope.ServiceProvider.GetRequiredService<Gadfly.Server.Grading.GradingService>();
            Assert.That((await grading.PublishAsync(w.SessionB, null)).Ok, Is.False);
            Assert.That((await grading.ReopenHumanReviewAsync(w.SessionB, "because")).Ok, Is.False);
            Assert.That((await grading.ProceedWithUnresolvedAsync(w.SessionB, "because")).Ok, Is.False);
        }
        await using var check = SystemDb();
        Assert.That(await check.FinalGrades.Where(g => g.SessionId == w.SessionB).Select(g => g.Status).SingleAsync(), Is.EqualTo(Gadfly.Assessment.GradeStatus.Finalized));
        Assert.That(await check.AuditEvents.AnyAsync(a => a.OrganizationId == w.Org && a.Action == "access.denied" && a.ActorUserId == w.TeacherA && a.EntityId == w.SessionB.ToString()), Is.True);
    }
}
