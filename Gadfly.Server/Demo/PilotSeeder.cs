using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Demo;

/// <summary>
/// Pilot accounts (GAD-T226, for the pilot GAD-T66): Erin's own organization with Erin as its Owner and Ryan as a Student in it,
/// separate from the "Gadfly Classroom" test data. Accounts are shared across organizations, so an existing <c>erin</c> or
/// <c>ryan</c> (for example the ones on the live app) is reused with its password and flags untouched and only gains the new
/// membership; an account that does not exist yet is created with the temporary password in <c>Gadfly:Demo:Password</c> (or a
/// generated one that is not logged) and must change it, and Erin, being an Owner, must enroll MFA.
/// Run: <c>Gadfly.Server --seed-pilot --then-exit</c>, in development or with <c>Gadfly:Demo:AllowPilotSeed=true</c>.
/// Re-running is safe: the organization and memberships are reused.
/// </summary>
public static class PilotSeeder
{
    public const string SeedArgument = "--seed-pilot";
    public const string OrganizationName = "Erin's Pilot";
    public const string OwnerUserName = "erin";
    public const string StudentUserName = "ryan";
    public const string SeededAction = "pilot.seeded";

    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gadfly.Pilot");
        if (!app.Environment.IsDevelopment() && app.Configuration["Gadfly:Demo:AllowPilotSeed"] != "true")
        {
            log.LogError("--seed-pilot runs in development, or with Gadfly:Demo:AllowPilotSeed=true; nothing was seeded.");
            return;
        }
        await SeedAsync(app.Services.GetRequiredService<IServiceScopeFactory>(), app.Configuration, log, CancellationToken.None);
    }

    public static async Task SeedAsync(IServiceScopeFactory scopes, IConfiguration config, ILogger log, CancellationToken ct)
    {
        var configured = config[DemoSeeder.PasswordSetting];
        var password = string.IsNullOrWhiteSpace(configured) ? DemoSeeder.GeneratePassword() : configured.Trim();

        using var scope = scopes.CreateSystemScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<GadflyDbContext>();
        var directory = sp.GetRequiredService<IOrganizationDirectory>();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Name == OrganizationName, ct);
        if (org is null)
        {
            var created = await directory.CreateOrganizationAsync(OrganizationName, OwnerUserName, "", password, Guid.Empty, ct);
            if (!created.Ok || created.Id is null) throw new InvalidOperationException("Pilot organization not created: " + created.Message);
            org = await db.Organizations.FirstAsync(o => o.Id == created.Id, ct);
        }
        else
        {
            var owner = await directory.AddMemberAsync(org.Id, OwnerUserName, null, OrgRoles.Owner, password, ct);
            if (!owner.Ok) throw new InvalidOperationException($"{OwnerUserName} not made owner: {owner.Message}");
        }
        var student = await directory.AddMemberAsync(org.Id, StudentUserName, null, OrgRoles.Student, password, ct);
        if (!student.Ok) throw new InvalidOperationException($"{StudentUserName} not added as a student: {student.Message}");

        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org.Id, Action = SeededAction, EntityType = "Organization", EntityId = org.Id.ToString(),
            At = sp.GetRequiredService<TimeProvider>().GetUtcNow(), Reason = $"Pilot accounts seeded by {SeedArgument} (GAD-T226).",
            AfterJson = JsonSerializer.Serialize(new { owner = OwnerUserName, student = StudentUserName }),
        });
        await db.SaveChangesAsync(ct);
        log.LogWarning("Pilot organization \"{Org}\": {Owner} is Owner and {Student} is Student. Accounts that already existed keep their password; new ones use {Source}.",
            OrganizationName, OwnerUserName, StudentUserName, string.IsNullOrWhiteSpace(configured) ? "a generated temporary password that is not logged" : "Gadfly:Demo:Password");
    }
}
