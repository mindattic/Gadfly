using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>GAD-T482: a Stripe checkout event changes the plan once, and a renewal never resets custom limits.</summary>
public class CheckoutWebhookTests : ServerTest
{
    private static async Task<CheckoutWebhook.Outcome> ApplyAsync(Guid org, string plan, string eventId)
    {
        await using var db = SystemDb();
        return await CheckoutWebhook.ApplyAsync(db, new CheckoutCompleted(org, plan, eventId), DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private static async Task<OrganizationSettings> SettingsAsync(Guid org)
    {
        await using var db = SystemDb();
        return await db.OrganizationSettings.AsNoTracking().SingleAsync(s => s.OrganizationId == org);
    }

    [Test]
    public async Task A_redelivered_event_does_not_overwrite_limits_a_platform_admin_set_since()
    {
        var org = await Seed.OrganizationAsync();
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        Assert.That(await ApplyAsync(org, PlanCatalog.Standard, eventId), Is.EqualTo(CheckoutWebhook.Outcome.Upgraded));

        await using (var sys = SystemDb())
            await sys.OrganizationSettings.Where(s => s.OrganizationId == org)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StudentSeatLimit, 777).SetProperty(s => s.StaffSeatLimit, 77));

        Assert.That(await ApplyAsync(org, PlanCatalog.Standard, eventId), Is.EqualTo(CheckoutWebhook.Outcome.AlreadyApplied));
        var after = await SettingsAsync(org);
        Assert.That((after.StudentSeatLimit, after.StaffSeatLimit), Is.EqualTo((777, 77)));

        await using var check = SystemDb();
        Assert.That(await check.ProcessedStripeEvents.CountAsync(e => e.EventId == eventId), Is.EqualTo(1));
        Assert.That(await check.AuditEvents.CountAsync(a => a.OrganizationId == org && a.Action == "org.plan.upgraded-via-checkout"), Is.EqualTo(1),
            "the upgrade is audited once");
    }

    [Test]
    public async Task Renewing_the_same_plan_keeps_custom_limits_and_moves_the_renewal_date_on()
    {
        var org = await Seed.OrganizationAsync();
        await ApplyAsync(org, PlanCatalog.Standard, "evt_" + Guid.NewGuid().ToString("N"));
        await using (var sys = SystemDb())
            await sys.OrganizationSettings.Where(s => s.OrganizationId == org)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StudentSeatLimit, 400).SetProperty(s => s.MonthlyExamAllowance, (int?)900));
        var renewsBefore = (await SettingsAsync(org)).PlanRenewsAt;

        Assert.That(await ApplyAsync(org, PlanCatalog.Standard, "evt_" + Guid.NewGuid().ToString("N")), Is.EqualTo(CheckoutWebhook.Outcome.Renewed));
        var after = await SettingsAsync(org);
        Assert.That(after.StudentSeatLimit, Is.EqualTo(400));
        Assert.That(after.MonthlyExamAllowance, Is.EqualTo(900));
        Assert.That(after.PlanRenewsAt, Is.GreaterThan(renewsBefore), "a renewal paid early adds a month to the current period");
    }
}
