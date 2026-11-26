using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Admin;

/// <summary>
/// Leaves the platform's admin rights with one account (GAD-T791): every other account loses the platform Admin role and
/// every organization role above Student, and keeps an active Student membership in the classroom organization. Where a
/// demoted account was an organization's only owner, the kept account becomes its owner first, so no organization is left
/// without one. Roles change through MindAttic.Authentication's own services (which audit organization changes and sign the
/// account's sessions out); each change also gets a Gadfly audit entry. Passwords, MFA enrollments and emails are not touched.
/// <para>
/// Run: <c>Gadfly.Server --admin-only-roles --dry-run --then-exit</c> lists every account's roles and what would change.
/// Without <c>--dry-run</c> it changes them, and only with <c>Gadfly:Admin:AllowAdminOnlyRoles=true</c> set for that one run.
/// <c>--keep-admin NAME</c> (default <c>admin</c>) and <c>--classroom-org "NAME"</c> (default <c>Gadfly Classroom</c>) pick the accounts.
/// </para>
/// </summary>
public static class AdminOnlyRoles
{
    public const string Argument = "--admin-only-roles";
    public const string DryRunArgument = "--dry-run";
    public const string KeepArgument = "--keep-admin";
    public const string ClassroomArgument = "--classroom-org";
    public const string AllowSetting = "Gadfly:Admin:AllowAdminOnlyRoles";
    public const string Action = "account.role-reduced";

    public const string DefaultKeep = "admin";
    public const string DefaultClassroom = "Gadfly Classroom";

    /// <summary>One planned change: the platform role (<see cref="Organization"/> null) or a membership, from one role to another.</summary>
    public sealed record Change(Guid UserId, string UserName, Guid? OrganizationId, string? Organization, string? From, string To);

    public static async Task RunAsync(WebApplication app, string[] args)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.AdminOnlyRoles");
        var dryRun = args.Contains(DryRunArgument);
        if (!dryRun && app.Configuration[AllowSetting] != "true")
        {
            log.LogError("{Arg} changes roles only with {Setting}=true (or run it with {DryRun}); nothing was changed.", Argument, AllowSetting, DryRunArgument);
            return;
        }
        var keep = ValueAfter(args, KeepArgument) ?? DefaultKeep;
        var classroom = ValueAfter(args, ClassroomArgument) ?? DefaultClassroom;

        using var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<GadflyDbContext>();

        Console.WriteLine("Roles before:");
        await PrintAsync(db, CancellationToken.None);
        var (changes, problem) = await PlanAsync(db, keep, classroom, CancellationToken.None);
        if (problem is not null) { log.LogError("{Arg}: {Problem} Nothing was changed.", Argument, problem); return; }
        Console.WriteLine(dryRun ? $"Planned changes ({changes.Count}, dry run, nothing changed):" : $"Changes ({changes.Count}):");
        foreach (var c in changes) Console.WriteLine($"  {c.UserName,-20} {c.Organization ?? "(platform)",-30} {c.From ?? "(none)",-15} -> {c.To}");
        if (dryRun) return;

        await ApplyAsync(db, sp.GetRequiredService<IUserAdminService>(), sp.GetRequiredService<IOrganizationService>(),
            sp.GetRequiredService<TimeProvider>(), keep, changes, CancellationToken.None);
        Console.WriteLine("Roles after:");
        await PrintAsync(db, CancellationToken.None);
        log.LogWarning("{Arg}: {Count} role change(s) made; only '{Keep}' keeps admin rights.", Argument, changes.Count, keep);
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Every account with its platform role and its memberships (role, active or not).</summary>
    public static async Task PrintAsync(GadflyDbContext db, CancellationToken ct)
    {
        var users = await db.AuthUsers.AsNoTracking().OrderBy(u => u.UserName).Select(u => new { u.Id, u.UserName, u.Role, u.IsActive }).ToListAsync(ct);
        var members = await (from m in db.OrganizationMembers.AsNoTracking()
                             join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                             orderby o.Name
                             select new { m.UserId, o.Name, m.Role, m.IsActive }).ToListAsync(ct);
        foreach (var u in users)
        {
            var mine = members.Where(m => m.UserId == u.Id).Select(m => $"{m.Name}: {m.Role}{(m.IsActive ? "" : " (removed)")}");
            Console.WriteLine($"  {u.UserName,-20} platform {u.Role}{(u.IsActive ? "" : " (inactive)")}; {string.Join("; ", mine.DefaultIfEmpty("no memberships"))}");
        }
    }

    /// <summary>The changes that leave admin rights with <paramref name="keep"/> only, in the order they must be made.</summary>
    public static async Task<(IReadOnlyList<Change> Changes, string? Problem)> PlanAsync(GadflyDbContext db, string keep, string classroom, CancellationToken ct)
    {
        var normalized = IUserStore.Normalize(keep);
        var kept = await db.AuthUsers.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
        if (kept is null) return ([], $"No account is named '{keep}'.");
        var classroomOrg = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Name == classroom && o.IsActive, ct);
        if (classroomOrg is null) return ([], $"No active organization is named '{classroom}'.");

        var users = await db.AuthUsers.AsNoTracking().Where(u => u.Id != kept.Id).OrderBy(u => u.UserName).ToListAsync(ct);
        var orgs = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var members = await db.OrganizationMembers.AsNoTracking().ToListAsync(ct);

        var demotions = new List<Change>();
        foreach (var u in users)
        {
            if (u.Role != OrganizationDirectory.AccountRole) demotions.Add(new(u.Id, u.UserName, null, null, u.Role, OrganizationDirectory.AccountRole));
            foreach (var m in members.Where(m => m.UserId == u.Id && m.IsActive && m.Role != OrgRoles.Student))
                demotions.Add(new(u.Id, u.UserName, m.OrganizationId, orgs[m.OrganizationId], m.Role, OrgRoles.Student));
            if (!members.Any(m => m.UserId == u.Id && m.OrganizationId == classroomOrg.Id && m.IsActive))
            {
                var old = members.FirstOrDefault(m => m.UserId == u.Id && m.OrganizationId == classroomOrg.Id);
                demotions.Add(new(u.Id, u.UserName, classroomOrg.Id, classroomOrg.Name, old is null ? null : old.Role + " (removed)", OrgRoles.Student));
            }
        }

        // An organization keeps an active owner: where every active owner is being demoted, the kept account takes it first.
        var owners = new List<Change>();
        foreach (var org in demotions.Where(c => c.From == OrgRoles.Owner).Select(c => c.OrganizationId!.Value).Distinct())
        {
            var keptMember = members.FirstOrDefault(m => m.UserId == kept.Id && m.OrganizationId == org);
            if (keptMember is { IsActive: true, Role: OrgRoles.Owner }) continue;
            owners.Add(new(kept.Id, kept.UserName, org, orgs[org], keptMember is null ? null : keptMember.Role + (keptMember.IsActive ? "" : " (removed)"), OrgRoles.Owner));
        }
        return ([.. owners, .. demotions], null);
    }

    /// <summary>Makes the planned changes through the auth library's services, auditing each one; stops at the first refusal.</summary>
    public static async Task ApplyAsync(GadflyDbContext db, IUserAdminService users, IOrganizationService orgs, TimeProvider clock, string keep,
        IReadOnlyList<Change> changes, CancellationToken ct)
    {
        foreach (var c in changes)
        {
            if (c.OrganizationId is null)
            {
                var r = await users.SetRoleAsync(c.UserId, c.To, ct);
                if (!r.Ok) throw new InvalidOperationException($"{c.UserName}'s platform role was not changed: {r.Error}");
            }
            else
            {
                var r = await orgs.AddMemberAsync(c.OrganizationId.Value, c.UserId, c.To, actingRole: null, actorUserId: null, ct);
                if (!r.Ok) throw new InvalidOperationException($"{c.UserName} in {c.Organization} was not changed: {r.Message}");
            }
            db.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = c.OrganizationId,
                ActorRole = "system",
                Action = Action,
                EntityType = c.OrganizationId is null ? "Account" : "Membership",
                EntityId = c.UserId.ToString(),
                BeforeJson = JsonSerializer.Serialize(new { c.UserName, Role = c.From }),
                AfterJson = JsonSerializer.Serialize(new { c.UserName, Role = c.To }),
                Reason = $"Only the '{keep}' account keeps admin rights ({Argument}, GAD-T791).",
                At = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
