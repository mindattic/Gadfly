using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Translation;

/// <summary>
/// Exam-language data access for components (GAD-T539): every operation runs in its own DI scope acting as the circuit's pinned
/// tenant, so a panel never shares the circuit's DbContext with the page around it.
/// </summary>
internal static class LanguageOps
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<ExamLanguageService, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(ActivatorUtilities.GetServiceOrCreateInstance<ExamLanguageService>(scope.ServiceProvider));
    }

    /// <summary>Runs a machine translation for the circuit's organization (GAD-T548), metered to it.</summary>
    public static async Task<T> TranslateAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<TranslationService, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(ActivatorUtilities.GetServiceOrCreateInstance<TranslationService>(scope.ServiceProvider));
    }
}
