using Gadfly.Server.Admin;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The students directory and profile (GAD-T296): paged search and filters, tenant isolation, deactivation with a reason, the CSV
/// import's dry run and idempotent apply, and that only owners and admins get in.
/// </summary>
[TestFixture]
public class StudentAdminTests : ServerTest
{
    private static async Task<(Guid Org, TenantSnapshot Admin)> OrgWithAdminAsync()
    {
        var org = await Seed.OrganizationAsync();
        var admin = await Seed.MemberAsync(Services, org, OrgRoles.Admin);
        return (org, Member(org, admin, OrgRoles.Admin));
    }

    private static async Task<T> AsAsync<T>(TenantSnapshot who, Func<StudentAdminService, Task<T>> work)
    {
        await using var scope = Scope(who);
        return await work(new StudentAdminService(scope.ServiceProvider));
    }

    [Test]
    public async Task List_SearchesFiltersAndPages_WithinTheOrganizationOnly()
    {
        var (org, admin) = await OrgWithAdminAsync();
        var names = new List<string>();
        for (var i = 0; i < 7; i++) names.Add(await SeedStudentAsync(org, $"pupil{i}-"));
        var (otherOrg, _) = await OrgWithAdminAsync();
        await SeedStudentAsync(otherOrg, "pupil-elsewhere-");

        var all = await AsAsync(admin, s => s.ListAsync(new StudentQuery { PageSize = 5 }));
        Assert.That(all.Total, Is.EqualTo(7), "staff and other organizations' students are not listed");
        Assert.That(all.Rows, Has.Count.EqualTo(5));
        Assert.That(all.Pages, Is.EqualTo(2));
        var second = await AsAsync(admin, s => s.ListAsync(new StudentQuery { PageSize = 5, Page = 2 }));
        Assert.That(second.Rows, Has.Count.EqualTo(2));
        Assert.That(second.Rows.Select(r => r.UserId).Intersect(all.Rows.Select(r => r.UserId)), Is.Empty);

        var found = await AsAsync(admin, s => s.ListAsync(new StudentQuery { Search = names[3] }));
        Assert.That(found.Rows.Single().UserName, Is.EqualTo(names[3]));

        var never = await AsAsync(admin, s => s.ListAsync(new StudentQuery { Status = StudentStatusFilter.NeverSignedIn }));
        Assert.That(never.Total, Is.EqualTo(7));
    }

    [Test]
    public async Task List_FiltersByExamAndAccommodation()
    {
        var w = await Seed.ExamAsync();
        var admin = Member(w.Org, await Seed.MemberAsync(Services, w.Org, OrgRoles.Admin), OrgRoles.Admin);
        await using (var db = SystemDb())
        {
            // The seeded student is a bare account: make them a student member here so the directory lists them.
            db.OrganizationMembers.Add(new MindAttic.Authentication.Entities.AuthMembership
                { OrganizationId = w.Org, UserId = w.StudentId, Role = OrgRoles.Student, CreatedUtc = DateTime.UtcNow });
            await db.ExamAssignments.Where(a => a.Id == w.AssignmentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.TimeMultiplier, 1.5m));
            await db.SaveChangesAsync();
        }
        await SeedStudentAsync(w.Org, "unassigned-");

        var byExam = await AsAsync(admin, s => s.ListAsync(new StudentQuery { ExamId = w.ExamId }));
        Assert.That(byExam.Rows.Single().UserId, Is.EqualTo(w.StudentId));
        var extra = await AsAsync(admin, s => s.ListAsync(new StudentQuery { Accommodation = StudentAccommodationFilter.ExtraTime }));
        Assert.That(extra.Rows.Single().ExtraTime, Is.True);
        var gaze = await AsAsync(admin, s => s.ListAsync(new StudentQuery { Accommodation = StudentAccommodationFilter.EyeContact }));
        Assert.That(gaze.Total, Is.Zero);

        var profile = await AsAsync(admin, s => s.ProfileAsync(w.StudentId));
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.Assignments.Single().Progress, Is.EqualTo(AssignmentProgress.NotStarted));
        Assert.That(profile.Assignments.Single().TimeMultiplier, Is.EqualTo(1.5m));
    }

    [Test]
    public async Task Profile_OfAnotherOrganizationsStudent_IsNotFound()
    {
        var (_, admin) = await OrgWithAdminAsync();
        var (otherOrg, _) = await OrgWithAdminAsync();
        var name = await SeedStudentAsync(otherOrg, "foreign-");
        Guid id;
        await using (var db = SystemDb()) id = await db.AuthUsers.Where(u => u.UserName == name).Select(u => u.Id).SingleAsync();
        Assert.That(await AsAsync(admin, s => s.ProfileAsync(id)), Is.Null);
        var r = await AsAsync(admin, s => s.SetActiveAsync(id, false, "testing"));
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task Deactivate_NeedsAReason_IsAudited_AndReactivateGivesItBack()
    {
        var (org, admin) = await OrgWithAdminAsync();
        var name = await SeedStudentAsync(org, "leaver-");
        Guid id;
        await using (var db = SystemDb()) id = await db.AuthUsers.Where(u => u.UserName == name).Select(u => u.Id).SingleAsync();

        var noReason = await AsAsync(admin, s => s.SetActiveAsync(id, false, "  "));
        Assert.That(noReason.Ok, Is.False);

        var off = await AsAsync(admin, s => s.SetActiveAsync(id, false, "Left the course"));
        Assert.That(off.Ok, Is.True, off.Message);
        var deactivated = await AsAsync(admin, s => s.ListAsync(new StudentQuery { Status = StudentStatusFilter.Deactivated }));
        Assert.That(deactivated.Rows.Single().UserId, Is.EqualTo(id));

        await using (var db = OrgDb(org))
        {
            var e = await db.AuditEvents.SingleAsync(x => x.OrganizationId == org && x.Action == "admin.student-deactivated" && x.EntityId == id.ToString());
            Assert.That(e.Reason, Is.EqualTo("Left the course"));
        }

        var on = await AsAsync(admin, s => s.SetActiveAsync(id, true, null));
        Assert.That(on.Ok, Is.True, on.Message);
        var profile = await AsAsync(admin, s => s.ProfileAsync(id));
        Assert.That(profile!.Active, Is.True);
        Assert.That(profile.Audit.Select(a => a.Action), Does.Contain("admin.student-deactivated").And.Contain("admin.student-reactivated"));
    }

    [Test]
    public async Task Import_DryRunSavesNothing_ApplyCreates_AndASecondRunChangesNothing()
    {
        var (org, admin) = await OrgWithAdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var csv = "email,name,username,password\n" +
                  $"ada-{tag}@example.edu,Ada,ada-{tag},{Seed.StrongPassword}\n" +
                  $"alan-{tag}@example.edu,Alan,,{Seed.StrongPassword}\n" +
                  "broken,Nobody,,\n";

        var preview = await AsAsync(admin, s => s.PreviewImportAsync(csv));
        Assert.That(preview.Applied, Is.False);
        Assert.That(preview.Count(ImportAction.Create), Is.EqualTo(2));
        Assert.That(preview.Errors, Is.EqualTo(1));
        await using (var db = SystemDb())
            Assert.That(await db.AuthUsers.AnyAsync(u => u.Email == $"ada-{tag}@example.edu"), Is.False, "a dry run saves nothing");

        var applied = await AsAsync(admin, s => s.ApplyImportAsync(csv));
        Assert.That(applied.Rows.Count(r => r.Done), Is.EqualTo(2), string.Join("; ", applied.Rows.Select(r => r.Message)));
        await using (var db = SystemDb())
        {
            var alan = await db.AuthUsers.SingleAsync(u => u.Email == $"alan-{tag}@example.edu");
            Assert.That(alan.UserName, Is.EqualTo($"alan-{tag}@example.edu"), "no user name given: the email is the user name");
            Assert.That(alan.MustChangePassword, Is.True);
            Assert.That(await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == org && m.UserId == alan.Id && m.Role == OrgRoles.Student), Is.True);
            Assert.That(await db.AuditEvents.AnyAsync(e => e.OrganizationId == org && e.Action == "admin.students-imported"), Is.True);
        }

        var again = await AsAsync(admin, s => s.PreviewImportAsync(csv));
        Assert.That(again.Changes, Is.Zero, "importing the same file twice changes nothing the second time");
        Assert.That(again.Count(ImportAction.Unchanged), Is.EqualTo(2));

        var renamed = await AsAsync(admin, s => s.PreviewImportAsync($"email,name\nada-{tag}@example.edu,Ada Lovelace\n"));
        Assert.That(renamed.Rows.Single().Action, Is.EqualTo(ImportAction.UpdateName));
    }

    [Test]
    public async Task Import_RefusesStaffAndInvitesAddressesThatHaveAnAccountElsewhere()
    {
        var (org, admin) = await OrgWithAdminAsync();
        var (otherOrg, _) = await OrgWithAdminAsync();
        var staffEmail = $"staff-{Guid.NewGuid():N}@example.edu";
        var outsiderEmail = $"outsider-{Guid.NewGuid():N}@example.edu";
        await using (var scope = Scope(TenantSnapshot.System))
        {
            var dir = scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>();
            await dir.AddMemberAsync(org, Seed.UserName(), staffEmail, OrgRoles.Instructor, Seed.StrongPassword);
            await dir.AddMemberAsync(otherOrg, Seed.UserName(), outsiderEmail, OrgRoles.Student, Seed.StrongPassword);
        }
        var plan = await AsAsync(admin, s => s.PreviewImportAsync($"email,password\n{staffEmail},{Seed.StrongPassword}\n{outsiderEmail},{Seed.StrongPassword}\n"));
        Assert.That(plan.Rows[0].Action, Is.EqualTo(ImportAction.Error));
        Assert.That(plan.Rows[0].Message, Does.Contain(OrgRoles.Instructor));
        Assert.That(plan.Rows[1].Action, Is.EqualTo(ImportAction.Invite), "an organization never pulls an outside account in");
    }

    [Test]
    public async Task OnlyOwnersAndAdmins_GetIn()
    {
        var org = await Seed.OrganizationAsync();
        var instructor = Member(org, await Seed.MemberAsync(Services, org, OrgRoles.Instructor), OrgRoles.Instructor);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => AsAsync(instructor, s => s.ListAsync(new StudentQuery())));
        var r = await AsAsync(instructor, s => s.CreateAsync(new StudentForm { UserName = Seed.UserName(), Password = Seed.StrongPassword }));
        Assert.That(r.Ok, Is.False);
        await using var db = OrgDb(org);
        Assert.That(await db.AuditEvents.AnyAsync(e => e.OrganizationId == org && e.Action == "access.denied" && e.ActorRole == OrgRoles.Instructor), Is.True);
    }

    [Test]
    public async Task Create_AddsAStudent_AndExportListsThem()
    {
        var (org, admin) = await OrgWithAdminAsync();
        var userName = Seed.UserName();
        var r = await AsAsync(admin, s => s.CreateAsync(new StudentForm { Name = "Grace Hopper", UserName = userName, Password = Seed.StrongPassword, Email = $"{userName}@example.edu" }));
        Assert.That(r.Ok, Is.True, r.Message);
        var csv = await AsAsync(admin, s => s.ExportCsvAsync(new StudentQuery()));
        Assert.That(csv, Does.StartWith("email,name,username,"));
        Assert.That(csv, Does.Contain($"{userName}@example.edu,Grace Hopper,{userName},Never signed in"));
    }

    /// <summary>A student member through the directory; returns the user name.</summary>
    private static async Task<string> SeedStudentAsync(Guid org, string prefix)
    {
        var name = prefix + Guid.NewGuid().ToString("N")[..8];
        await Seed.MemberAsync(Services, org, OrgRoles.Student, name);
        return name;
    }
}
