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
                var signature = request.Headers["Stripe-Signature"].ToString();
                if (!checkout.IsSignatureValid(payload, signature)) return Results.BadRequest();
                // A genuine event that grants nothing (another type, an unpaid session, an unknown plan) is acknowledged so Stripe stops retrying it.
                var completed = checkout.VerifyAndParse(payload, signature);
                if (completed is null) return Results.Ok();

                // Applied once per Stripe event (GAD-T482); a redelivery is acknowledged (200) so Stripe stops sending it.
                var outcome = await CheckoutWebhook.ApplyAsync(db, completed, clock.GetUtcNow(), ct);
                log.LogInformation("Stripe event {EventId} for organization {OrganizationId} ({Plan}): {Outcome}.",
                    completed.EventId, completed.OrganizationId, completed.Plan, outcome);
                return Results.Ok();
            })
            .AllowAnonymous();   // authenticated by its Stripe signature, not a Gadfly session (GAD-T372)
        return app;
    }
}
