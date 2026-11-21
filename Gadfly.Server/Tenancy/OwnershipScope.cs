using System.Collections.Concurrent;
using Gadfly.Server.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Tenancy;

/// <summary>
/// Decision D18 (GAD-T170), default A: an Instructor sees and manages only what they own; Owner and Admin keep
/// the whole organization. The rule lives here and in <see cref="GadflyDbContext"/>'s global query filter, which
/// narrows Course, SourceCollection and Exam to the rows the caller created and ExamAssignment and ExamSession to
/// those of owned exams. Every service, page, endpoint and report reads through that filter, so there is no
/// per-page check to forget.
/// <para>Who is scoped: a signed-in caller whose role in the active organization is Instructor, while the
/// organization's <c>InstructorsSeeOnlyOwn</c> setting is on (the default). Not scoped: Owner, Admin, platform
/// API keys (organization-level integrations), system work, and Student (students already see only their own
/// assignments). Examiner and SecondReviewer stay organization-wide: grading is not assigned to individual
/// examiners today, so there is no assignment to scope them by.</para>
/// </summary>
public static class OwnershipPolicy
{
    private static readonly ConcurrentDictionary<Guid, (bool Scoped, long ExpiresAt)> Cache = new();
    private static readonly long TtlMs = 15_000;

    /// <summary>
    /// Whether instructors of <paramref name="organizationId"/> are limited to their own items. Read with a short-lived
    /// connection of its own (a query filter runs inside another operation on the context, which may not be re-entered),
    /// cached for a few seconds, and fails closed (scoped) when the setting cannot be read.
    /// </summary>
    internal static bool InstructorsScoped(GadflyDbContext db, Guid organizationId)
    {
        var now = Environment.TickCount64;
        if (Cache.TryGetValue(organizationId, out var hit) && hit.ExpiresAt > now) return hit.Scoped;
        try
        {
            var cs = db.Database.GetConnectionString();
            if (string.IsNullOrEmpty(cs)) return true;
            using var conn = new SqlConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT InstructorsSeeOnlyOwn FROM [" + GadflyDbContext.Schema + "].[OrganizationSettings] WHERE OrganizationId = @o";
            cmd.Parameters.AddWithValue("@o", organizationId);
            var scoped = cmd.ExecuteScalar() is not bool b || b;   // no settings row: the default, scoped
            Cache[organizationId] = (scoped, now + TtlMs);
            return scoped;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Forget the cached setting (the settings page calls this after saving, so the change applies at once here).</summary>
    public static void Invalidate(Guid organizationId) => Cache.TryRemove(organizationId, out _);
}

/// <summary>Audit of a refused direct visit to another instructor's item.</summary>
public static class OwnershipScope
{
    public enum Kind { Course, Collection, Exam, Assignment, Session }

    /// <summary>True when the caller is an instructor limited to their own items in the active organization.</summary>
    public static bool Applies(this GadflyDbContext db) => db.OwnerScoped;

    /// <summary>
    /// Call where a lookup by id came back empty. If the item exists in the caller's organization but belongs to
    /// another instructor, writes and saves an <c>access.denied</c> audit event and returns true (the caller then
    /// reports it as not found, exactly as for an id that does not exist, so nothing is revealed). Returns false,
    /// writing nothing, for ids that do not exist or when the caller is not scoped.
    /// </summary>
    public static async Task<bool> RefuseForeignAsync(this GadflyDbContext db, Kind kind, Guid id, string attempted, CancellationToken ct = default)
    {
        if (!db.OwnerScoped || db.TenantOrganizationId is not { } org || db.TenantUserId is not { } me) return false;
        Guid? owner = kind switch
        {
            Kind.Course => await db.Courses.IgnoreQueryFilters().Where(x => x.Id == id && x.OrganizationId == org).Select(x => (Guid?)x.CreatedByUserId).FirstOrDefaultAsync(ct),
            Kind.Collection => await db.SourceCollections.IgnoreQueryFilters().Where(x => x.Id == id && x.OrganizationId == org).Select(x => (Guid?)x.CreatedByUserId).FirstOrDefaultAsync(ct),
            Kind.Exam => await db.Exams.IgnoreQueryFilters().Where(x => x.Id == id && x.OrganizationId == org).Select(x => (Guid?)x.CreatedByUserId).FirstOrDefaultAsync(ct),
            Kind.Assignment => await (from a in db.ExamAssignments.IgnoreQueryFilters()
                                      join e in db.Exams.IgnoreQueryFilters() on a.ExamId equals e.Id
                                      where a.Id == id && a.OrganizationId == org && e.OrganizationId == org
                                      select (Guid?)e.CreatedByUserId).FirstOrDefaultAsync(ct),
            Kind.Session => await (from s in db.ExamSessions.IgnoreQueryFilters()
                                   join e in db.Exams.IgnoreQueryFilters() on s.ExamId equals e.Id
                                   where s.Id == id && s.OrganizationId == org && e.OrganizationId == org
                                   select (Guid?)e.CreatedByUserId).FirstOrDefaultAsync(ct),
            _ => null,
        };
        if (owner is null || owner == me) return false;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org,
            ActorUserId = me,
            ActorRole = db.TenantRole,
            Action = "access.denied",
            EntityType = kind.ToString(),
            EntityId = id.ToString(),
            Reason = $"{attempted}: this {kind.ToString().ToLowerInvariant()} belongs to another instructor, and this organization lets instructors see only their own.",
            At = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
