namespace Gadfly.Server.Data;

// ------------------------------------------------------------------ public site (GAD-T308)

/// <summary>
/// A demo request or question sent from the public site's contact form. Platform data, not an organization's: it is
/// written by signed-out visitors and read only by the platform SuperAdmin, so it carries no tenant filter
/// (TenancyTests lists it as unfiltered by design). Holds only what the visitor typed, no address or device data.
/// </summary>
public sealed class ContactRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ReceivedAt { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Institution { get; set; } = "";
    public string? Role { get; set; }
    /// <summary>What they asked for: Demo, Pricing, Trial or Other.</summary>
    public string Topic { get; set; } = "Demo";
    /// <summary>The plan they came from on the pricing page, if any.</summary>
    public string? Plan { get; set; }
    /// <summary>Roughly how many students, as they chose it.</summary>
    public string? Students { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public Guid? HandledByUserId { get; set; }
    /// <summary>The platform admin's own note (who replied, what was agreed).</summary>
    public string? Note { get; set; }
}
