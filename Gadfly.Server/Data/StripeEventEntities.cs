using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Data;

/// <summary>
/// A Stripe webhook event Gadfly has already applied (GAD-T482). Stripe re-sends an event for days when it isn't sure it
/// arrived, and the dashboard can resend it by hand, each time with a fresh signature; the event's own id is the same every
/// time, so a second delivery finds its row here and changes nothing. Platform-level bookkeeping, not tenant data: no
/// organization reads it, and it holds no personal data (ids only).
/// </summary>
public sealed class ProcessedStripeEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Stripe's event id (<c>evt_...</c>), unique.</summary>
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public Guid? OrganizationId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public static class StripeEventModel
{
    public const int MaxEventIdLength = 255;

    public static void Configure(ModelBuilder b) =>
        b.Entity<ProcessedStripeEvent>(e =>
        {
            e.ToTable("ProcessedStripeEvents", GadflyDbContext.Schema);
            e.Property(x => x.EventId).HasMaxLength(MaxEventIdLength);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.HasIndex(x => x.EventId).IsUnique();
        });
}
