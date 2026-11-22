using System.Security.Claims;
using Gadfly.Server.Desk;
using Gadfly.Server.Tenancy;
using Microsoft.Extensions.Configuration;
using MindAttic.Authentication;
using NUnit.Framework;

namespace Gadfly.Tests.Tenancy;

/// <summary>
/// GAD-T459: the admin desk is for the organization's Owners and Admins (with two-step verification); the old
/// Gadfly:AdminDesk:Admins list is deprecated, still honoured for one release, and warned about while it is set.
/// </summary>
[TestFixture]
public class DeskAccessTests
{
    private sealed class Tenant(Guid org, string role) : ITenantContext
    {
        public bool IsSystem => false;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? OrganizationId => org;
        public string? Role => role;
        public IReadOnlyList<Membership> Memberships => [new Membership(org, role)];
        public bool IsSuperAdmin => false;
    }

    private static IConfiguration Config(params string[] listed)
    {
        var values = new Dictionary<string, string?> { [DeskAccess.EnabledSetting] = "true" };
        for (var i = 0; i < listed.Length; i++) values[$"{DeskAccess.AdminsSetting}:{i}"] = listed[i];
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ClaimsPrincipal User(string name, bool mfa = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(MaClaims.UserId, Guid.NewGuid().ToString()) };
        if (mfa) claims.Add(new Claim(MaClaims.Amr, "mfa"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
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

    [TestCase(OrgRoles.Owner, true)]
    [TestCase(OrgRoles.Admin, true)]
    [TestCase(OrgRoles.Instructor, false)]
    [TestCase(OrgRoles.Student, false)]
    public void OnlyOwnersAndAdmins_UseTheDesk_WithNoListConfigured(string role, bool allowed)
    {
        Assert.That(DeskAccess.IsDeskAdmin(User("someone"), new Tenant(Guid.NewGuid(), role), Config()), Is.EqualTo(allowed));
    }

    [Test]
    public void AnOwnerWithoutTwoStepVerification_IsSentToSetItUp()
    {
        var tenant = new Tenant(Guid.NewGuid(), OrgRoles.Owner);
        Assert.That(DeskAccess.IsDeskAdmin(User("owner", mfa: false), tenant, Config()), Is.False);
        Assert.That(DeskAccess.NeedsMfaForDesk(User("owner", mfa: false), tenant, Config()), Is.True);
    }

    [Test]
    public void ADeprecatedListedStudent_IsStillHonouredForOneRelease()
    {
        var tenant = new Tenant(Guid.NewGuid(), OrgRoles.Student);
        Assert.That(DeskAccess.IsDeskAdmin(User("ryan"), tenant, Config("ryan")), Is.True);
        Assert.That(DeskAccess.IsDeskAdmin(User("ryan", mfa: false), tenant, Config("ryan")), Is.False, "still behind two-step verification");
        Assert.That(DeskAccess.IsDeskAdmin(User("other"), tenant, Config("ryan")), Is.False);
    }

    [Test]
    public void TheWarning_NamesTheListedAccounts_OnlyWhileTheListIsSet()
    {
        Assert.That(DeskAccess.DeprecatedListWarning(Config()), Is.Null);
        var warning = DeskAccess.DeprecatedListWarning(Config("ryan", "pilot"));
        Assert.That(warning, Does.Contain("deprecated").And.Contain("ryan, pilot").And.Contain(DeskAccess.ListHonouredUntil));
    }
}
