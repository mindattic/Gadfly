using System.Security.Claims;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Authorization;
using MindAttic.Authentication;
using NUnit.Framework;

namespace Gadfly.Tests.Tenancy;

/// <summary>
/// The organization-role policies (GAD-T41): every role against every policy, and the staff MFA rule. Pure:
/// the handler is driven with a fixed tenant and a hand-made principal.
/// </summary>
[TestFixture]
public class OrgRolePolicyTests
{
    private sealed class Tenant(Guid? org, string? role) : ITenantContext
    {
        public bool IsSystem => false;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? OrganizationId => org;
        public string? Role => role;
        public IReadOnlyList<Membership> Memberships => org is { } o && role is not null ? [new Membership(o, role)] : [];
        public bool IsSuperAdmin => false;
    }

    private static readonly Dictionary<string, string[]> PolicyRoles = new()
    {
        ["OrgAdmin"] = [OrgRoles.Owner, OrgRoles.Admin],
        ["Instructor"] = [OrgRoles.Owner, OrgRoles.Admin, OrgRoles.Instructor],
        ["Student"] = [OrgRoles.Student],
        ["Member"] = [.. OrgRoles.All],
        ["Api"] = [OrgRoles.Api],
    };

    private static bool Allowed(string policy, string? role, bool mfa, bool hasOrg = true)
    {
        var requirement = new OrgRoleRequirement(PolicyRoles[policy]);
        var claims = new List<Claim> { new(MaClaims.UserId, Guid.NewGuid().ToString()) };
        if (mfa) claims.Add(new Claim(MaClaims.Amr, "mfa"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        new OrgRoleHandler(new Tenant(hasOrg ? Guid.NewGuid() : null, role)).HandleAsync(context).GetAwaiter().GetResult();
        return context.HasSucceeded;
    }

    private bool _mfaBefore;

    [SetUp]
    public void RequireMfa()
    {
        _mfaBefore = OrgRoleHandler.MfaRequired;
        OrgRoleHandler.MfaRequired = true;
    }

    [TearDown]
    public void Restore() => OrgRoleHandler.MfaRequired = _mfaBefore;

    public static IEnumerable<TestCaseData> Matrix() =>
        from policy in PolicyRoles.Keys
        from role in OrgRoles.All.Append(OrgRoles.Api)
        let granted = PolicyRoles[policy].Contains(role)
        select new TestCaseData(policy, role, granted).SetName($"Policy_{policy}_{role}_{(granted ? "Allowed" : "Denied")}");

    [TestCaseSource(nameof(Matrix))]
    public void RoleAgainstPolicy_WithMfa(string policy, string role, bool granted) =>
        Assert.That(Allowed(policy, role, mfa: true), Is.EqualTo(granted));

    [TestCase(OrgRoles.Owner)]
    [TestCase(OrgRoles.Admin)]
    [TestCase(OrgRoles.Instructor)]
    [TestCase(OrgRoles.Examiner)]
    [TestCase(OrgRoles.SecondReviewer)]
    public void StaffWithoutMfa_AreRefused(string role) => Assert.That(Allowed("Member", role, mfa: false), Is.False);

    [Test]
    public void StudentsDoNotNeedMfa() => Assert.That(Allowed("Student", OrgRoles.Student, mfa: false), Is.True);

    [Test]
    public void ApiKeysDoNotNeedMfa() => Assert.That(Allowed("Api", OrgRoles.Api, mfa: false), Is.True);

    [Test]
    public void DevelopmentWithoutAnAuthenticator_LetsStaffIn()
    {
        OrgRoleHandler.MfaRequired = false;
        Assert.That(Allowed("OrgAdmin", OrgRoles.Owner, mfa: false), Is.True);
    }

    [Test]
    public void NoActiveOrganization_IsRefused() => Assert.That(Allowed("Member", OrgRoles.Owner, mfa: true, hasOrg: false), Is.False);

    [Test]
    public void NoRole_IsRefused() => Assert.That(Allowed("Member", null, mfa: true), Is.False);

    [Test]
    public void ExaminersAndSecondReviewers_AreNotInstructorsOrAdmins()
    {
        foreach (var role in new[] { OrgRoles.Examiner, OrgRoles.SecondReviewer })
        {
            Assert.That(Allowed("Instructor", role, mfa: true), Is.False, role);
            Assert.That(Allowed("OrgAdmin", role, mfa: true), Is.False, role);
            Assert.That(Allowed("Member", role, mfa: true), Is.True, role);
        }
    }
}
