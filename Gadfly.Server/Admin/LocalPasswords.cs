using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Admin;

/// <summary>
/// Development-only: sets the password of every local account (or the names after <c>--users</c>, comma-separated) so the
/// owner can sign in to a local copy (GAD-T801). The password comes from the <c>GADFLY_LOCAL_PASSWORD</c> environment variable,
/// never the command line. Refused outside Development, so it can never touch a hosted database.
/// </summary>
public static class LocalPasswords
{
    public const string Argument = "--set-local-passwords";
    public const string PasswordVariable = "GADFLY_LOCAL_PASSWORD";

    public static async Task RunAsync(WebApplication app, string[] args, CancellationToken ct = default)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.Logger.LogError("{Argument} runs only in Development; nothing changed.", Argument);
            return;
        }
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrEmpty(password))
        {
            app.Logger.LogError("Set {Variable} to the new password; nothing changed.", PasswordVariable);
            return;
        }

        var at = Array.IndexOf(args, "--users");
        var only = at >= 0 && at + 1 < args.Length
            ? args[at + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(IUserStore.Normalize).ToHashSet()
            : null;

        using var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateSystemScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<GadflyDbContext>();
        var users = sp.GetRequiredService<IUserAdminService>();

        var accounts = await db.AuthUsers.AsNoTracking().Select(u => new { u.Id, u.UserName, u.NormalizedUserName }).ToListAsync(ct);
        foreach (var a in accounts.Where(a => only is null || only.Contains(a.NormalizedUserName)))
        {
            await users.ResetPasswordAsync(a.Id, password, false, ct);
            await db.AuthUsers.Where(u => u.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, false), ct);
            app.Logger.LogInformation("Local password set for {UserName}.", a.UserName);
        }
    }
}
