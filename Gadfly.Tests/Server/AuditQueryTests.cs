using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The audit log viewer (GAD-T301): its filters, search and paging in SQL, tenant isolation, who may read which log, the
/// redaction rule, and CSV safety. On LocalDB through the real AuditQueryService.
/// </summary>
[TestFixture]
public class AuditQueryTests : ServerTest
{
    private sealed record World(Guid Org, Guid Admin, Guid Other, Seed.ExamWorld Exam, Guid SessionId);

    private static AuditEvent Event(Guid? org, Guid? actor, string action, string entityType, string? entityId, DateTimeOffset at,
        Guid? session = null, string? reason = null, string? after = null) => new()
    {
        OrganizationId = org, ActorUserId = actor, ActorRole = OrgRoles.Admin, Action = action, EntityType = entityType, EntityId = entityId,
        SessionId = session, Reason = reason, AfterJson = after, At = at,
    };

    private async Task<World> SeedAsync()
    {
        var w = await Seed.ExamAsync();
        var admin = await Seed.AccountAsync();
        var other = await Seed.AccountAsync();
        var session = new ExamSession
        {
            OrganizationId = w.Org, AssignmentId = w.AssignmentId, ExamId = w.ExamId, StudentUserId = w.StudentId, State = SessionState.Ended,
            CreatedAt = Clock.GetUtcNow(), StartedAt = Clock.GetUtcNow(), EndedAt = Clock.GetUtcNow(),
        };
        var t0 = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        await using (var db = SystemDb())
        {
            db.ExamSessions.Add(session);
            db.AuditEvents.AddRange(
                Event(w.Org, admin, "assessment.published", "Exam", w.ExamId.ToString(), t0, after: """{"title":"Biology check"}"""),
                Event(w.Org, admin, "assessment.settings.updated", "Exam", w.ExamId.ToString(), t0.AddDays(1), reason: "Longer for the photosynthesis unit"),
                Event(w.Org, other, "grade.human.recorded", "HumanObjectiveGrade", Guid.NewGuid().ToString(), t0.AddDays(2), session.Id, after: """{"score":70}"""),
                Event(w.Org, other, "desk.attempts-changed", "ExamAssignment", w.AssignmentId.ToString(), t0.AddDays(3)),
                Event(w.Org, admin, "course.created", "Course", Guid.NewGuid().ToString(), t0.AddDays(4)),
                // another organization's event and a platform event: never in this organization's log
                Event(Guid.NewGuid(), admin, "assessment.published", "Exam", Guid.NewGuid().ToString(), t0.AddDays(1)),
                Event(null, admin, "platform.organization.created", "Organization", Guid.NewGuid().ToString(), t0.AddDays(1)));
            await db.SaveChangesAsync();
        }
        return new World(w.Org, admin, other, w, session.Id);
    }

    private static async Task<AuditPage> PageAsync(World w, AuditFilter f, int page = 1, int size = 50)
    {
        var me = Member(w.Org, w.Admin, OrgRoles.Admin);
        await using var db = Db(me);
        return await new AuditQueryService(db, me, Clock).PageAsync(new AuditTarget(w.Org), f, page, size);
    }

    [Test]
    public async Task NoFilter_ShowsOnlyThisOrganization_NewestFirst()
    {
        var w = await SeedAsync();
        var p = await PageAsync(w, new AuditFilter());
        Assert.That(p.Total, Is.EqualTo(5));
        Assert.That(p.Rows.Select(r => r.Action).First(), Is.EqualTo("course.created"));
        Assert.That(p.Rows.Select(r => r.At), Is.Ordered.Descending);
        Assert.That(p.Rows.Any(r => r.Action.StartsWith("platform.")), Is.False);
    }

    [Test]
    public async Task Filters_ByActor_Action_ActionFamily_Dates_AndSearch()
    {
        var w = await SeedAsync();
        Assert.That((await PageAsync(w, new AuditFilter { ActorUserId = w.Other })).Total, Is.EqualTo(2));
        Assert.That((await PageAsync(w, new AuditFilter { Action = "assessment.published" })).Total, Is.EqualTo(1));
        Assert.That((await PageAsync(w, new AuditFilter { Action = "assessment." })).Total, Is.EqualTo(2), "a family ending in a dot");
        var t0 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.That((await PageAsync(w, new AuditFilter { From = t0.AddDays(1), To = t0.AddDays(3) })).Total, Is.EqualTo(2));
        Assert.That((await PageAsync(w, new AuditFilter { Search = "photosynthesis" })).Total, Is.EqualTo(1), "found in the reason");
        Assert.That((await PageAsync(w, new AuditFilter { Search = "\"score\":70" })).Total, Is.EqualTo(1), "found in the detail JSON");
    }

    [Test]
    public async Task Filters_ByExam_Student_AndSession_FollowTheirRecords()
    {
        var w = await SeedAsync();
        // The exam: its own events, its assignment's and its session's.
        Assert.That((await PageAsync(w, new AuditFilter { ExamId = w.Exam.ExamId })).Total, Is.EqualTo(4));
        // The student: their session's event and their assignment's.
        Assert.That((await PageAsync(w, new AuditFilter { StudentUserId = w.Exam.StudentId })).Total, Is.EqualTo(2));
        var s = await PageAsync(w, new AuditFilter { SessionId = w.SessionId });
        Assert.That(s.Rows.Single().Action, Is.EqualTo("grade.human.recorded"));
    }

    [Test]
    public async Task Paging_SplitsTheResults()
    {
        var w = await SeedAsync();
        var first = await PageAsync(w, new AuditFilter(), page: 1, size: 2);
        var third = await PageAsync(w, new AuditFilter(), page: 3, size: 2);
        Assert.That((first.Total, first.Pages, first.Rows.Count), Is.EqualTo((5, 3, 2)));
        Assert.That(third.Rows, Has.Count.EqualTo(1));
        Assert.That(third.Rows.Single().Action, Is.EqualTo("assessment.published"), "the oldest is last");
    }

    [Test]
    public async Task OnlyOwnersAndAdmins_ReadTheLog_AndPlatformStaffMayChooseAny()
    {
        var w = await SeedAsync();
        var target = new AuditTarget(w.Org);
        Assert.That(AuditQueryService.MayRead(Member(w.Org, w.Admin, OrgRoles.Owner), target), Is.True);
        Assert.That(AuditQueryService.MayRead(Member(w.Org, w.Admin, OrgRoles.Admin), target), Is.True);
        Assert.That(AuditQueryService.MayRead(Member(w.Org, w.Admin, OrgRoles.Instructor), target), Is.False);
        Assert.That(AuditQueryService.MayRead(Member(w.Org, w.Admin, OrgRoles.Student), target), Is.False);
        Assert.That(AuditQueryService.MayRead(Member(Guid.NewGuid(), w.Admin, OrgRoles.Admin), target), Is.False, "another organization's admin");
        Assert.That(AuditQueryService.MayRead(Member(w.Org, w.Admin, OrgRoles.Admin), new AuditTarget(null)), Is.False, "platform events are for platform staff");
        var staff = new TenantSnapshot(false, w.Admin, null, null, [], IsSuperAdmin: true);
        Assert.That(AuditQueryService.MayRead(staff, target), Is.True);
        Assert.That(AuditQueryService.NeedsSystemScope(staff, target), Is.True);

        await using var sys = SystemDb();
        var p = await new AuditQueryService(sys, staff, Clock).PageAsync(target, new AuditFilter());
        Assert.That(p.Total, Is.EqualTo(5), "platform staff see exactly that organization's events");

        var instructor = Member(w.Org, w.Admin, OrgRoles.Instructor);
        await using var db = Db(instructor);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AuditQueryService(db, instructor, Clock).PageAsync(target, new AuditFilter()));
    }

    [Test]
    public void Redaction_IsDetectedFromThePrivacyJobsTraces()
    {
        var kept = Guid.NewGuid();
        var retained = Guid.NewGuid();
        var sessions = new Dictionary<Guid, DateTimeOffset?> { [kept] = null, [retained] = DateTimeOffset.UtcNow };
        Assert.That(AuditQueryService.RedactionOf("response.corrected", retained, hasJson: false, null, sessions), Is.EqualTo(AuditRedaction.RetentionPeriod));
        Assert.That(AuditQueryService.RedactionOf("grade.human.recorded", Guid.NewGuid(), hasJson: false, null, sessions), Is.EqualTo(AuditRedaction.StudentErased));
        Assert.That(AuditQueryService.RedactionOf("grade.appeal.opened", kept, hasJson: true, AuditQueryService.RetentionPlaceholder, sessions), Is.EqualTo(AuditRedaction.StudentErased));
        Assert.That(AuditQueryService.RedactionOf("response.corrected", kept, hasJson: true, null, sessions), Is.EqualTo(AuditRedaction.None));
        Assert.That(AuditQueryService.RedactionOf("assessment.published", null, hasJson: false, null, sessions), Is.EqualTo(AuditRedaction.None));
    }

    [TestCase("plain", "plain")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [TestCase("=HYPERLINK(1)", "'=HYPERLINK(1)")]
    [TestCase("-1+2", "'-1+2")]
    public void Csv_QuotesAndDisarmsFormulas(string value, string expected) =>
        Assert.That(AuditQueryService.Csv(value), Is.EqualTo(expected));
}
