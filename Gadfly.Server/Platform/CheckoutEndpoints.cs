using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>
/// Card checkout (GAD-T372): an owner starts a Stripe Checkout Session for a plan, and Stripe's webhook applies
/// the upgrade once payment completes. Both endpoints exist and are mapped regardless of configuration, but do
/// nothing (a plain refusal, never a 500) when <see cref="PaymentSettings.Available"/> is false, so a server
/// with no payment keys never shows a broken button or a crashing webhook.
/// </summary>
public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/org/plan/checkout", async ([Microsoft.AspNetCore.Mvc.FromForm] string plan, HttpContext http, ITenantContext tenant, CheckoutService checkout, CancellationToken ct) =>
            {
                if (tenant.OrganizationId is not { } org) return Results.Forbid();
                var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
                var result = await checkout.StartAsync(org, plan, baseUrl, ct);
                return result.Ok ? Results.Redirect(result.Url!) : Results.LocalRedirect("/org/plan?checkout=error");
            })
            .RequireAuthorization(GadflyPolicies.OrgAdmin);

        app.MapPost("/payments/stripe/webhook", async (HttpRequest request, CheckoutService checkout, GadflyDbContext db, TimeProvider clock, ILogger<CheckoutCompleted> log, CancellationToken ct) =>
            {
                request.EnableBuffering();
                using var reader = new StreamReader(request.Body, leaveOpen: true);
                var payload = await reader.ReadToEndAsync(ct);
                var completed = checkout.VerifyAndParse(payload, request.Headers["Stripe-Signature"]);
                if (completed is null) return Results.BadRequest();

                var now = clock.GetUtcNow();
                var row = await db.OrganizationSettings.FirstOrDefaultAsync(s => s.OrganizationId == completed.OrganizationId, ct);
                if (row is null) db.OrganizationSettings.Add(row = new OrganizationSettings { OrganizationId = completed.OrganizationId });
                var before = new { row.Plan, row.StaffSeatLimit, row.StudentSeatLimit, row.MonthlyExamAllowance };
                var d = PlanCatalog.DefaultsFor(completed.Plan);
                row.Plan = completed.Plan;
                row.StaffSeatLimit = d.StaffSeats;
                row.StudentSeatLimit = d.StudentSeats;
                row.MonthlyExamAllowance = d.MonthlyExams;
                row.TrialStartedAt = null;
                row.PlanRenewsAt = now.AddMonths(1);
                row.UpdatedAt = now;
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganizationId = completed.OrganizationId, ActorRole = "Checkout", Action = "org.plan.upgraded-via-checkout",
                    EntityType = "OrganizationSettings", EntityId = completed.OrganizationId.ToString(),
                    BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
                    AfterJson = System.Text.Json.JsonSerializer.Serialize(new { row.Plan, row.StaffSeatLimit, row.StudentSeatLimit, row.MonthlyExamAllowance }),
                    At = now,
                });
                await db.SaveChangesAsync(ct);
                log.LogInformation("Organization {OrganizationId} upgraded to {Plan} via card checkout.", completed.OrganizationId, completed.Plan);
                return Results.Ok();
            })
            .AllowAnonymous();   // authenticated by its Stripe signature, not a Gadfly session (GAD-T372)
        return app;
    }
}
