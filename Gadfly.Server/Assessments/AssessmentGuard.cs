using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Assessments;

/// <summary>
/// Permission checks and audit writing for assessment setup (GAD-T83 enforcement, GAD-T84 audit). Every
/// permission-checked action asks <see cref="PermissionMatrix"/> with the caller's role in the active
/// organization; a refusal is written to the append-only audit log as <c>access.denied</c> and saved at
/// once, before (and independently of) any other change.
/// </summary>
public sealed class AssessmentGuard(GadflyDbContext db, ITenantContext tenant, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Guid? OrganizationId => tenant.OrganizationId;
    public Guid? UserId => tenant.UserId;
    public string? Role => tenant.Role;

    /// <summary>True when the caller's role grants <paramref name="permission"/> (no audit).</summary>
    public bool Has(Permission permission) =>
        tenant.OrganizationId is not null && tenant.UserId is not null && PermissionMatrix.Has(tenant.Role, permission);

    /// <summary>
    /// Checks <paramref name="permission"/>; on refusal writes and saves an <c>access.denied</c> audit event
    /// naming the attempted action, and returns false.
    /// </summary>
    public async Task<bool> RequireAsync(Permission permission, string attemptedAction, string entityType, string? entityId, CancellationToken ct = default)
    {
        if (Has(permission)) return true;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = Trim(tenant.Role, 40),
            Action = "access.denied",
            EntityType = Trim(entityType, 80)!,
            EntityId = Trim(entityId, 80),
            Reason = Trim($"{attemptedAction} needs the {permission} permission; role {tenant.Role ?? "(none)"} does not have it.", 1000),
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return false;
    }

    /// <summary>The message shown to someone refused <paramref name="permission"/>.</summary>
    public static string Denied(Permission permission) =>
        $"Your role in this organization does not allow this ({permission}). The attempt was recorded.";

    /// <summary>Adds (does not save) an audit event; the caller saves it with the change it describes.</summary>
    public void Audit(string action, string entityType, string? entityId, object? before = null, object? after = null, string? reason = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = tenant.OrganizationId,
            ActorUserId = tenant.UserId,
            ActorRole = Trim(tenant.Role, 40),
            Action = Trim(action, 80)!,
            EntityType = Trim(entityType, 80)!,
            EntityId = Trim(entityId, 80),
            BeforeJson = before is null ? null : before as string ?? JsonSerializer.Serialize(before, Json),
            AfterJson = after is null ? null : after as string ?? JsonSerializer.Serialize(after, Json),
            Reason = Trim(reason, 1000),
            At = clock.GetUtcNow(),
        });
    }

    public static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
