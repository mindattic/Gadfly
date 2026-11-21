using Gadfly.Server.Data;

namespace Gadfly.Server.Components.Pages.Admin;

/// <summary>
/// Org admin console (GAD-T55) data access. Every operation runs in its own DI scope, so a page never
/// shares the circuit's DbContext with the layout or another in-flight query, and every query names
/// the organization explicitly (inside a circuit the tenant filter may be off).
/// </summary>
internal static class AdminDb
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, Func<IServiceProvider, GadflyDbContext, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await work(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<GadflyDbContext>());
    }

    public static string When(DateTimeOffset? at) => at is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC" : "never";
}
