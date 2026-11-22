using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>GAD-T487: restoring a removed member spends a seat like adding one; a CSV of invitations is checked against the free seats first.</summary>
public class SeatRestoreTests : ServerTest
{
    private static async Task<T> WithDirectory<T>(Func<IOrganizationDirectory, Task<T>> act)
    {
        await using var scope = Scope(TenantSnapshot.System);
        return await act(scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>());
    }

    [Test]
    public async Task Restoring_a_removed_member_is_refused_when_the_plan_is_full()
    {
        var org = await Seed.OrganizationAsync();
        await using (var sys = SystemDb())
            await sys.OrganizationSettings.Where(s => s.OrganizationId == org).ExecuteUpdateAsync(u => u.SetProperty(s => s.StudentSeatLimit, 1));

        var first = await WithDirectory(d => d.AddMemberAsync(org, Seed.UserName(), null, OrgRoles.Student, Seed.StrongPassword, actingRole: OrgRoles.Admin));
        Assert.That(first.Ok, Is.True, first.Message);
        Guid memberId;
        await using (var db = SystemDb())
            memberId = (await db.OrganizationMembers.AsNoTracking().SingleAsync(m => m.OrganizationId == org && m.UserId == first.Id)).Id;
        Assert.That((await WithDirectory(d => d.SetMemberActiveAsync(org, memberId, false, actingRole: OrgRoles.Admin))).Ok, Is.True);

        var second = await WithDirectory(d => d.AddMemberAsync(org, Seed.UserName(), null, OrgRoles.Student, Seed.StrongPassword, actingRole: OrgRoles.Admin));
        Assert.That(second.Ok, Is.True, "the freed seat goes to someone else");

        var restore = await WithDirectory(d => d.SetMemberActiveAsync(org, memberId, true, actingRole: OrgRoles.Admin));
        Assert.That(restore.Ok, Is.False);
        Assert.That(restore.Message, Does.Contain("can't be restored").And.Contain("seat"));
        await using (var db = SystemDb())
        {
            Assert.That((await db.OrganizationMembers.AsNoTracking().SingleAsync(m => m.Id == memberId)).IsActive, Is.False);
            Assert.That(await db.OrganizationMembers.CountAsync(m => m.OrganizationId == org && m.IsActive && m.Role == OrgRoles.Student), Is.EqualTo(1));
        }

        var byPlatform = await WithDirectory(d => d.SetMemberActiveAsync(org, memberId, true, actingRole: null));
        Assert.That(byPlatform.Ok, Is.True, "platform staff can always fix a seat problem, as with an add");
    }

    [Test]
    public void The_CSV_preview_names_the_rows_past_the_free_seats()
    {
        const string csv = "email,role\na@x.co\nb@x.co,Instructor\nc@x.co\nA@x.co\nnot-an-email\nd@x.co\nmember@x.co";
        var rows = BulkInvitePreview.Parse(csv, OrgRoles.Student, OrgRoles.Admin);
        var members = new HashSet<string>(StringComparer.Ordinal) { "MEMBER@X.CO" };

        var preview = BulkInvitePreview.Build(rows, members, staffLimit: 5, staffUsed: 5, studentLimit: 10, studentUsed: 9);

        Assert.That(preview.FreeStudentSeats, Is.EqualTo(1));
        Assert.That(preview.FreeStaffSeats, Is.EqualTo(0));
        Assert.That(preview.AlreadyMembers, Is.EqualTo(1));
        Assert.That(preview.Rows.Select(r => r.Email), Is.EqualTo(new[] { "a@x.co", "b@x.co", "c@x.co", "d@x.co" }), "header, repeats and invalid rows are left out");
        Assert.That(preview.Over.Select(r => (r.Line, r.Email)), Is.EqualTo(new[] { (3, "b@x.co"), (4, "c@x.co"), (7, "d@x.co") }));
        Assert.That(preview.OverStaff, Is.EqualTo(1));
        Assert.That(preview.OverStudent, Is.EqualTo(2));
    }

    [Test]
    public void The_CSV_preview_finds_nothing_over_when_seats_are_unlimited()
    {
        var rows = BulkInvitePreview.Parse("a@x.co\nb@x.co", OrgRoles.Student, OrgRoles.Admin);
        var preview = BulkInvitePreview.Build(rows, new HashSet<string>(), staffLimit: 0, staffUsed: 3, studentLimit: 0, studentUsed: 900);
        Assert.That(preview.Over, Is.Empty);
        Assert.That(preview.FreeStudentSeats, Is.Null);
    }
}
