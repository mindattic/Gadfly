using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>GAD-T481: the seat check and the add that follows it are one step, so simultaneous joins can't exceed the plan.</summary>
public class SeatLockTests : ServerTest
{
    [Test]
    public async Task Two_admins_adding_at_once_cannot_both_take_the_last_seat()
    {
        var org = await Seed.OrganizationAsync();
        await using (var sys = SystemDb())
            await sys.OrganizationSettings.Where(s => s.OrganizationId == org).ExecuteUpdateAsync(u => u.SetProperty(s => s.StudentSeatLimit, 1));

        async Task<DirectoryResult> AddAsync()
        {
            await using var scope = Scope(Member(org, Guid.NewGuid(), OrgRoles.Admin));
            var directory = scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>();
            return await directory.AddMemberAsync(org, Seed.UserName(), null, OrgRoles.Student, Seed.StrongPassword,
                actingRole: OrgRoles.Admin, actorUserId: null);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(AddAsync)));

        Assert.That(results.Count(r => r.Ok), Is.EqualTo(1), "only one of the simultaneous adds gets the one free seat");
        await using var check = SystemDb();
        Assert.That(await check.OrganizationMembers.CountAsync(m => m.OrganizationId == org && m.IsActive && m.Role == OrgRoles.Student), Is.EqualTo(1));
    }

    [Test]
    public async Task The_seat_lock_nests_and_is_released_for_the_next_caller()
    {
        var org = Guid.NewGuid();
        await using var first = SystemDb();
        await using (var outer = await SeatLock.AcquireAsync(first, org))
        await using (var inner = await SeatLock.AcquireAsync(first, org))
            Assert.That(outer.Acquired && inner.Acquired, Is.True, "the same context may hold it twice");

        await using var second = SystemDb();
        await using var next = await SeatLock.AcquireAsync(second, org);
        Assert.That(next.Acquired, Is.True, "released once the holder is done");
    }
}
