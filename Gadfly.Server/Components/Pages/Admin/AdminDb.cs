using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Components.Pages.Admin;

/// <summary>
/// Org admin console (GAD-T55) data access. Every operation runs in its own DI scope, so a page never
/// shares the circuit's DbContext with the layout or another in-flight query. The scope acts as the
/// circuit's tenant (GAD-T73: a child scope never reads the possibly-absent HttpContext), and every query
/// still names the organization explicitly.
/// </summary>
internal static class AdminDb
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<IServiceProvider, GadflyDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<GadflyDbContext>());
    }

    public static string When(DateTimeOffset? at) => at is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC" : "never";
}
