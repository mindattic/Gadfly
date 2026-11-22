using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// Applies a verified Stripe <c>checkout.session.completed</c> event to the organization's plan (GAD-T372), exactly once
/// (GAD-T482). Stripe redelivers events for days, and a platform admin can resend one from the dashboard, each time freshly
/// signed; the event id is recorded with the change in one save (unique index), so a repeat changes nothing and audits nothing.
/// <list type="bullet">
/// <item>A new plan takes that plan's standard seats and exam allowance.</item>
/// <item>A payment for the plan the organization already has is a renewal: it moves the renewal date on and keeps every
/// limit as it is, so custom numbers a platform admin set are never reset.</item>
/// </list>
/// </summary>
public static class CheckoutWebhook
{
    public enum Outcome { Upgraded, Renewed, AlreadyApplied }

    public const string EventType = "checkout.session.completed";

    public static async Task<Outcome> ApplyAsync(GadflyDbContext db, CheckoutCompleted completed, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.ProcessedStripeEvents.AsNoTracking().AnyAsync(e => e.EventId == completed.EventId, ct)) return Outcome.AlreadyApplied;

        var row = await db.OrganizationSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.OrganizationId == completed.OrganizationId, ct);
        if (row is null) db.OrganizationSettings.Add(row = new OrganizationSettings { OrganizationId = completed.OrganizationId });
        var before = new { row.Plan, row.StaffSeatLimit, row.StudentSeatLimit, row.MonthlyExamAllowance, row.PlanRenewsAt };

        var renewal = string.Equals(row.Plan, completed.Plan, StringComparison.Ordinal) && !string.Equals(row.Plan, PlanCatalog.Trial, StringComparison.Ordinal);
        if (renewal)
        {
            // Paid ahead of time: the new month starts where the current one ends, not today.
            row.PlanRenewsAt = (row.PlanRenewsAt is { } renews && renews > now ? renews : now).AddMonths(1);
        }
        else
        {
            var d = PlanCatalog.DefaultsFor(completed.Plan);
            row.Plan = completed.Plan;
            row.StaffSeatLimit = d.StaffSeats;
            row.StudentSeatLimit = d.StudentSeats;
            row.MonthlyExamAllowance = d.MonthlyExams;
            row.TrialStartedAt = null;
            row.PlanRenewsAt = now.AddMonths(1);
        }
        row.UpdatedAt = now;

        db.ProcessedStripeEvents.Add(new ProcessedStripeEvent
        {
            EventId = completed.EventId, EventType = EventType, OrganizationId = completed.OrganizationId, ProcessedAt = now,
        });
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = completed.OrganizationId, ActorRole = "Checkout",
            Action = renewal ? "org.plan.renewed-via-checkout" : "org.plan.upgraded-via-checkout",
            EntityType = "OrganizationSettings", EntityId = completed.OrganizationId.ToString(),
            BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { row.Plan, row.StaffSeatLimit, row.StudentSeatLimit, row.MonthlyExamAllowance, row.PlanRenewsAt, StripeEvent = completed.EventId }),
            At = now,
        });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two deliveries of the same event at the same moment: the other one's save won, and this one changes nothing.
            db.ChangeTracker.Clear();
            if (await db.ProcessedStripeEvents.AsNoTracking().AnyAsync(e => e.EventId == completed.EventId, ct)) return Outcome.AlreadyApplied;
            throw;
        }
        return renewal ? Outcome.Renewed : Outcome.Upgraded;
    }
}
