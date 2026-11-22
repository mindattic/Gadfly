using Gadfly.Server.Data;
using Gadfly.Server.Lti;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// LTI roster and role mapping (GAD-T312, GAD-T382, GAD-T458; under GAD-T399) on the test database: an LMS identity gets its own
/// account and membership on first sight, audited, and the same account every time after; a role Gadfly doesn't take part in is
/// refused; a matching email never links to an existing account but waits for confirmation; a role only moves up; and a member or
/// account an admin turned off stays out. Run in a scope pinned to the registration's organization, as the launch does.
/// </summary>
[TestFixture]
public class LtiUserProvisionerTests : ServerTest
{
    private static async Task<LtiRegistration> RegistrationAsync(Guid org)
    {
        var reg = new LtiRegistration
        {
            OrganizationId = org, Name = "Canvas test", Issuer = "https://canvas.example.edu", ClientId = "client-" + Guid.NewGuid().ToString("N")[..8],
            DeploymentId = "1", AuthLoginUrl = "https://canvas.example.edu/api/lti/authorize_redirect", AuthTokenUrl = "https://canvas.example.edu/login/oauth2/token",
            KeySetUrl = "https://canvas.example.edu/api/lti/security/jwks", CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var db = OrgDb(org);
        db.LtiRegistrations.Add(reg);
        await db.SaveChangesAsync();
        return reg;
    }

    private static LtiLaunchClaims Claims(string subject, string role, string? email = null, string? name = "Ada Lovelace") =>
        new(subject, email, null, null, name, [role], "course-1", null, null, "link-1", null, null, null, null, null, null);

    private static async Task<LtiProvisionResult> ProvisionAsync(LtiRegistration reg, LtiLaunchClaims claims, string source = LtiUserProvisioner.SourceLaunch)
    {
        await using var scope = Scope(TenantSnapshot.ForOrganization(reg.OrganizationId));
        return await scope.ServiceProvider.GetRequiredService<LtiUserProvisioner>().ProvisionAsync(reg, claims, CancellationToken.None, source);
    }

    private static string Subject() => "sub-" + Guid.NewGuid().ToString("N");
    private static string Unique() => "lms" + Guid.NewGuid().ToString("N")[..10];

    [Test]
    public async Task A_learner_seen_for_the_first_time_gets_an_account_a_student_membership_and_an_audit_row()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var local = Unique();
        // Display names are unique across the whole (shared) test database, so this one is the test's own (GAD-T520).
        var name = "Ada Lovelace " + local;
        var r = await ProvisionAsync(reg, Claims(Subject(), LtiClaims.RoleLearner, $"{local}@school.example", name));

        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.Created, Is.True);
        Assert.That(r.Role, Is.EqualTo(OrgRoles.Student));
        await using var db = SystemDb();
        var user = await db.AuthUsers.AsNoTracking().SingleAsync(u => u.Id == r.UserId);
        Assert.That(user.UserName, Is.EqualTo(local), "named after the email's local part");
        Assert.That(user.Email, Is.EqualTo($"{local}@school.example"));
        Assert.That(user.DisplayName, Is.EqualTo(name));
        var member = await db.OrganizationMembers.AsNoTracking().SingleAsync(m => m.OrganizationId == org && m.UserId == r.UserId);
        Assert.That(member.Role, Is.EqualTo(OrgRoles.Student));
        Assert.That(await db.LtiUserMappings.CountAsync(m => m.RegistrationId == reg.Id && m.GadflyUserId == r.UserId), Is.EqualTo(1));
        Assert.That(await db.AuditEvents.CountAsync(a => a.OrganizationId == org && a.Action == "lti.user-provisioned" && a.EntityId == r.UserId.ToString()), Is.EqualTo(1));
    }

    [Test]
    public async Task Two_people_with_the_same_name_both_get_accounts_the_second_name_numbered()
    {
        // GAD-T520: MindAttic.Authentication keeps display names unique, real people don't.
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var name = "Grace Hopper " + Unique();
        var first = await ProvisionAsync(reg, Claims(Subject(), LtiClaims.RoleLearner, $"{Unique()}@school.example", name));
        var second = await ProvisionAsync(reg, Claims(Subject(), LtiClaims.RoleLearner, $"{Unique()}@school.example", name));
        Assert.That(first.Ok, Is.True, first.Error);
        Assert.That(second.Ok, Is.True, second.Error);
        await using var db = SystemDb();
        Assert.That((await db.AuthUsers.AsNoTracking().SingleAsync(u => u.Id == first.UserId)).DisplayName, Is.EqualTo(name));
        Assert.That((await db.AuthUsers.AsNoTracking().SingleAsync(u => u.Id == second.UserId)).DisplayName, Is.EqualTo(name + " (2)"));
    }

    [Test]
    public async Task The_same_identity_always_reaches_the_same_account_whether_by_launch_or_roster()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var claims = Claims(Subject(), LtiClaims.RoleLearner, $"{Unique()}@school.example");
        var first = await ProvisionAsync(reg, claims, LtiUserProvisioner.SourceRoster);
        var second = await ProvisionAsync(reg, claims);
        Assert.That(second.Ok, Is.True, second.Error);
        Assert.That(second.UserId, Is.EqualTo(first.UserId));
        Assert.That(second.Created, Is.False);
        await using var db = SystemDb();
        Assert.That(await db.LtiUserMappings.CountAsync(m => m.RegistrationId == reg.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task A_role_Gadfly_does_not_take_part_in_is_refused_and_nothing_is_created()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var subject = Subject();
        var r = await ProvisionAsync(reg, Claims(subject, "http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor", $"{Unique()}@school.example"));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("is not one Gadfly takes part in").And.Contain("Mentor"));
        await using var db = SystemDb();
        Assert.That(await db.LtiUserMappings.AnyAsync(m => m.LtiSubject == subject), Is.False);
    }

    [Test]
    public async Task A_taken_user_name_gets_a_number_rather_than_reaching_the_other_account()
    {
        var org = await Seed.OrganizationAsync();
        var other = await Seed.OrganizationAsync();
        var local = Unique();
        var existing = await Seed.MemberAsync(Services, other, OrgRoles.Student, local);
        var reg = await RegistrationAsync(org);
        var r = await ProvisionAsync(reg, Claims(Subject(), LtiClaims.RoleLearner, $"{local}@school.example"));
        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.UserId, Is.Not.EqualTo(existing));
        await using var db = SystemDb();
        Assert.That(await db.AuthUsers.Where(u => u.Id == r.UserId).Select(u => u.UserName).SingleAsync(), Is.EqualTo(local + "2"));
    }

    [Test]
    public async Task Without_an_email_the_name_is_used_and_cleaned()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var name = "Zoë " + Unique();
        var r = await ProvisionAsync(reg, Claims(Subject(), LtiClaims.RoleLearner, email: null, name: name));
        Assert.That(r.Ok, Is.True, r.Error);
        await using var db = SystemDb();
        var userName = await db.AuthUsers.Where(u => u.Id == r.UserId).Select(u => u.UserName).SingleAsync();
        Assert.That(userName, Does.Match("^[A-Za-z0-9._-]+$"));
        Assert.That(userName, Does.EndWith(name.Split(' ')[1]));
    }

    [Test]
    public async Task A_matching_email_never_links_silently_but_waits_for_confirmation()
    {
        var org = await Seed.OrganizationAsync();
        var email = $"{Unique()}@school.example";
        Guid existing;
        await using (var scope = Scope(TenantSnapshot.System))
        {
            var added = await scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>().AddMemberAsync(org, Seed.UserName(), email, OrgRoles.Student, Seed.StrongPassword);
            Assert.That(added.Ok, Is.True, added.Message);
            existing = added.Id!.Value;
        }
        var reg = await RegistrationAsync(org);
        var claims = Claims(Subject(), LtiClaims.RoleLearner, email);

        var r = await ProvisionAsync(reg, claims);
        Assert.That(r.Ok, Is.False, "no account is handed over by a matching email alone");
        Assert.That(r.LinkRequestId, Is.Not.Null);
        Assert.That(r.Error, Does.Contain("same email address as an existing Gadfly account"));

        var again = await ProvisionAsync(reg, claims);
        Assert.That(again.LinkRequestId, Is.EqualTo(r.LinkRequestId), "the same pending request, not a second one");

        await using var db = SystemDb();
        var request = await db.LtiLinkRequests.AsNoTracking().SingleAsync(x => x.Id == r.LinkRequestId);
        Assert.That(request.CandidateUserId, Is.EqualTo(existing));
        Assert.That(request.Status, Is.EqualTo(LtiLinkRequest.Pending));
        Assert.That(await db.LtiUserMappings.AnyAsync(m => m.RegistrationId == reg.Id), Is.False);
        Assert.That(await db.AuthUsers.CountAsync(u => u.NormalizedEmail == MindAttic.Authentication.Services.IUserStore.Normalize(email)), Is.EqualTo(1), "no second account");
        Assert.That(await db.AuditEvents.CountAsync(a => a.OrganizationId == org && a.Action == "lti.link-requested"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_role_moves_up_from_the_LMS_but_never_down()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var subject = Subject();
        var email = $"{Unique()}@school.example";
        var asStudent = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner, email));
        Assert.That(asStudent.Role, Is.EqualTo(OrgRoles.Student));

        var asTa = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleInstructor, email));
        Assert.That(asTa.UserId, Is.EqualTo(asStudent.UserId));
        Assert.That(asTa.Role, Is.EqualTo(OrgRoles.Instructor), "a student who becomes staff in the course becomes an instructor");

        var backToLearner = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner, email));
        Assert.That(backToLearner.Role, Is.EqualTo(OrgRoles.Instructor), "never moved down automatically");
        await using var db = SystemDb();
        Assert.That(await db.OrganizationMembers.Where(m => m.OrganizationId == org && m.UserId == asStudent.UserId).Select(m => m.Role).SingleAsync(), Is.EqualTo(OrgRoles.Instructor));
    }

    [Test]
    public async Task A_role_an_admin_set_in_Gadfly_is_not_touched_by_the_LMS()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var subject = Subject();
        var first = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner, $"{Unique()}@school.example"));
        await SetMemberAsync(org, first.UserId!.Value, m => m.Role = OrgRoles.Examiner);
        var r = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleInstructor));
        Assert.That(r.Role, Is.EqualTo(OrgRoles.Examiner));
    }

    [Test]
    public async Task A_member_an_admin_removed_stays_out()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var subject = Subject();
        var first = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner, $"{Unique()}@school.example"));
        await SetMemberAsync(org, first.UserId!.Value, m => m.IsActive = false);
        var r = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("has been removed"));
    }

    [Test]
    public async Task An_account_an_admin_turned_off_stays_out()
    {
        var org = await Seed.OrganizationAsync();
        var reg = await RegistrationAsync(org);
        var subject = Subject();
        var first = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner, $"{Unique()}@school.example"));
        await using (var db = SystemDb())
        {
            var user = await db.AuthUsers.SingleAsync(u => u.Id == first.UserId);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }
        var r = await ProvisionAsync(reg, Claims(subject, LtiClaims.RoleLearner));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("turned off"));
    }

    private static async Task SetMemberAsync(Guid org, Guid user, Action<MindAttic.Authentication.Entities.AuthMembership> change)
    {
        await using var db = SystemDb();
        var member = await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == org && m.UserId == user);
        change(member);
        await db.SaveChangesAsync();
    }
}
