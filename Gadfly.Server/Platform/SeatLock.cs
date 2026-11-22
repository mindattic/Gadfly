using System.Data;
using System.Data.Common;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using MindAttic.Authentication.Entities;

namespace Gadfly.Server.Platform;

/// <summary>
/// One organization's seat lock (GAD-T481): a SQL Server application lock (<c>sp_getapplock</c>) named after the organization,
/// held from the seat check until the new membership is saved, so two people joining at the same moment (two first LMS
/// launches, a roster sync and an invitation, two admins adding at once) can never both take the last free seat. The second
/// one waits, then counts the first one's seat and is refused if none is left.
/// <para>The lock belongs to the database session, not a transaction, so the saves in between keep EF's own retrying
/// transactions; the connection stays open while it is held, and closing it (or the process ending) always releases it.
/// Holding it again on the same context nests. Anything other than SQL Server (none in use today) runs without it.</para>
/// </summary>
public sealed class SeatLock : IAsyncDisposable
{
    /// <summary>How long a join waits for another join in the same organization to finish before giving up.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    public const string BusyMessage = "Several people are being added to this organization at the same moment. Please try again in a few seconds.";

    private readonly DbContext? _db;
    private readonly string _resource;
    private bool _released;

    private SeatLock(DbContext? db, string resource, bool acquired)
    {
        _db = db;
        _resource = resource;
        Acquired = acquired;
    }

    /// <summary>False when another join held the lock for longer than <see cref="Wait"/>: refuse with <see cref="BusyMessage"/>.</summary>
    public bool Acquired { get; }

    public static string ResourceFor(Guid organizationId) => "gadfly-seats:" + organizationId.ToString("N");

    public static async Task<SeatLock> AcquireAsync(DbContext db, Guid organizationId, CancellationToken ct = default)
    {
        var resource = ResourceFor(organizationId);
        if (!db.Database.IsSqlServer()) return new SeatLock(null, resource, acquired: true);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = Command(db, "sys.sp_getapplock");
            Add(cmd, "@Resource", resource);
            Add(cmd, "@LockMode", "Exclusive");
            Add(cmd, "@LockOwner", "Session");
            Add(cmd, "@LockTimeout", (int)Wait.TotalMilliseconds);
            var result = Return(cmd);
            cmd.CommandTimeout = (int)Wait.TotalSeconds + 15;
            await cmd.ExecuteNonQueryAsync(ct);
            // 0 granted at once, 1 granted after waiting; below 0 timed out, cancelled or deadlocked.
            if (result.Value is int code && code >= 0) return new SeatLock(db, resource, acquired: true);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
        await db.Database.CloseConnectionAsync();
        return new SeatLock(null, resource, acquired: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_released || _db is null) return;
        _released = true;
        try
        {
            await using var cmd = Command(_db, "sys.sp_releaseapplock");
            Add(cmd, "@Resource", _resource);
            Add(cmd, "@LockOwner", "Session");
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // The connection broke: SQL Server already dropped the session's locks with it.
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private static DbCommand Command(DbContext db, string procedure)
    {
        var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = procedure;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static DbParameter Return(DbCommand cmd)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@Return";
        p.DbType = DbType.Int32;
        p.Direction = ParameterDirection.ReturnValue;
        cmd.Parameters.Add(p);
        return p;
    }
}

/// <summary>
/// Accepting an email invitation spends a seat inside MindAttic.Authentication's own accept flow, which never calls back into
/// Gadfly (GAD-T481). This interceptor recognises that save (a new or restored membership saved together with the library's
/// <c>invite.accept</c> audit row), takes the organization's <see cref="SeatLock"/> and refuses the save when the plan has no free
/// seat of that kind left. The library then releases the invitation untouched, so it can be accepted once a seat is free.
/// Every other path checks its seat itself, under the same lock.
/// </summary>
public sealed class InvitationSeatInterceptor : SaveChangesInterceptor
{
    public static readonly InvitationSeatInterceptor Instance = new();

    private const string AcceptAction = "invite.accept";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, SeatLock> Held = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is GadflyDbContext db && Joining(db) is { } join)
        {
            var seats = await SeatLock.AcquireAsync(db, join.OrganizationId, cancellationToken);
            if (!seats.Acquired) throw new DbUpdateException(SeatLock.BusyMessage);
            try
            {
                if (await SeatTakenAsync(db, join.OrganizationId, join.Role, cancellationToken))
                    throw new DbUpdateException("This organization's plan has no free seat for the invitation's role; it can be accepted once a seat is free.");
            }
            catch
            {
                await seats.DisposeAsync();
                throw;
            }
            Held.AddOrUpdate(db, seats);
        }
        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        // The library's accept flow is asynchronous; a synchronous save of an acceptance would skip the lock, so refuse it.
        eventData.Context is GadflyDbContext db && Joining(db) is not null
            ? throw new InvalidOperationException("An invitation acceptance must be saved asynchronously so its seat can be checked.")
            : base.SavingChanges(eventData, result);

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await ReleaseAsync(eventData.Context);
        return result;
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) =>
        await ReleaseAsync(eventData.Context);

    public override async Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default) =>
        await ReleaseAsync(eventData.Context);

    private static async Task ReleaseAsync(DbContext? context)
    {
        if (context is null || !Held.TryGetValue(context, out var seats)) return;
        Held.Remove(context);
        await seats.DisposeAsync();
    }

    private sealed record Join(Guid OrganizationId, string Role);

    /// <summary>The membership an invitation acceptance is about to add or restore, or null when this save is not one.</summary>
    private static Join? Joining(GadflyDbContext db)
    {
        var accepted = db.ChangeTracker.Entries<AuthOrganizationAudit>()
            .Where(e => e.State == EntityState.Added && e.Entity.Action == AcceptAction)
            .Select(e => e.Entity.OrganizationId).ToHashSet();
        if (accepted.Count == 0) return null;
        foreach (var m in db.ChangeTracker.Entries<AuthMembership>())
        {
            if (!accepted.Contains(m.Entity.OrganizationId) || !m.Entity.IsActive) continue;
            var joins = m.State == EntityState.Added
                || (m.State == EntityState.Modified && (!(bool)m.Property(x => x.IsActive).OriginalValue!
                    || IsStaff((string)m.Property(x => x.Role).OriginalValue!) != IsStaff(m.Entity.Role)));
            if (joins) return new Join(m.Entity.OrganizationId, m.Entity.Role);
        }
        return null;
    }

    private static bool IsStaff(string role) => role != OrgRoles.Student;

    /// <summary>True when the plan's seats of <paramref name="role"/>'s kind are all in use (counted organization-wide, whoever is asking).</summary>
    internal static async Task<bool> SeatTakenAsync(GadflyDbContext db, Guid organizationId, string role, CancellationToken ct)
    {
        var settings = await db.OrganizationSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .Select(s => new { s.StaffSeatLimit, s.StudentSeatLimit }).FirstOrDefaultAsync(ct);
        var defaults = new OrganizationSettings();
        var staff = IsStaff(role);
        var limit = staff ? settings?.StaffSeatLimit ?? defaults.StaffSeatLimit : settings?.StudentSeatLimit ?? defaults.StudentSeatLimit;
        if (limit <= 0) return false;
        var used = await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive && (staff ? m.Role != OrgRoles.Student : m.Role == OrgRoles.Student), ct);
        return used >= limit;
    }
}
