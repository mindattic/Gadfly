using Gadfly.Server.Admin;
using Gadfly.Server.Desk;
using Gadfly.Server.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Authentication.Services;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// User names are case-insensitive and passwords are not (GAD-T776): 'GideonKain' and 'gideonkain' sign in as the same account,
/// a name that differs from a taken one only by case is refused, and a password in the wrong case never signs in.
/// </summary>
[TestFixture]
public class UserNameCaseTests : ServerTest
{
    private static async Task<(Guid Org, TenantSnapshot Admin)> OrgWithAdminAsync()
    {
        var org = await Seed.OrganizationAsync();
        var admin = await Seed.MemberAsync(Services, org, OrgRoles.Admin);
        return (org, Member(org, admin, OrgRoles.Admin));
    }

    private static async Task<DeskResult> CreateStudentAsync(TenantSnapshot admin, string userName)
    {
        await using var scope = Scope(admin);
        return await new StudentAdminService(scope.ServiceProvider).CreateAsync(new StudentForm { UserName = userName, Password = Seed.StrongPassword });
    }

    private static async Task<LoginResult> LoginAsync(string userName, string password)
    {
        await using var scope = Scope(TenantSnapshot.System);
        var auth = scope.ServiceProvider.GetRequiredService<MindAttic.Authentication.Services.IAuthenticationService>();
        return await auth.LoginAsync(userName, password, "203.0.113.7", "UserNameCaseTests");
    }

    [Test]
    public async Task SignIn_IgnoresTheCaseOfTheUserName_ButNotOfThePassword()
    {
        var (_, admin) = await OrgWithAdminAsync();
        var userName = "GideonKain" + Guid.NewGuid().ToString("N")[..8];
        var created = await CreateStudentAsync(admin, userName);
        Assert.That(created.Ok, Is.True, created.Message);

        foreach (var spelled in new[] { userName, userName.ToLowerInvariant(), userName.ToUpperInvariant() })
        {
            var r = await LoginAsync(spelled, Seed.StrongPassword);
            Assert.That(r.Status, Is.Not.EqualTo(LoginStatus.Failed), $"'{spelled}' signs in as '{userName}'");
            Assert.That(r.UserId, Is.EqualTo(created.Id));
        }

        var wrongCase = await LoginAsync(userName.ToLowerInvariant(), Seed.StrongPassword.ToUpperInvariant());
        Assert.That(wrongCase.Status, Is.EqualTo(LoginStatus.Failed), "the password is compared exactly");
    }

    [Test]
    public async Task Create_RefusesANameThatDiffersFromATakenOneOnlyByCase()
    {
        var (_, admin) = await OrgWithAdminAsync();
        var userName = "Ryan" + Guid.NewGuid().ToString("N")[..8];
        var first = await CreateStudentAsync(admin, userName);
        Assert.That(first.Ok, Is.True, first.Message);

        var again = await CreateStudentAsync(admin, userName.ToLowerInvariant());
        Assert.That(again.Ok, Is.False, "'ryan…' is the same user name as 'Ryan…'");
    }
}
