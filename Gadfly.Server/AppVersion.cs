using System.Reflection;

namespace Gadfly.Server;

/// <summary>
/// The app's deploy version for the browser tab title and the header (GAD-T291, docs/DEPLOY.md "Versions"):
/// "V<N>" on a deployed build (tools/deploy-azure.ps1 passes /p:GadflyVersion=N and tags the commit vN),
/// "V<N> dev" on a local build (N = the highest vN tag in the checkout), or "dev" when there is no tag yet.
/// Baked in at build time as the assembly metadata "GadflyVersion" by Gadfly.Server.csproj.
/// </summary>
public static class AppVersion
{
    public static string Label { get; } =
        typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GadflyVersion")?.Value is { Length: > 0 } v ? v : "dev";

    /// <summary>
    /// The full commit hash a deployed build was made from (tools/deploy-azure.ps1 passes /p:GadflyCommit=&lt;sha&gt;, GAD-T336),
    /// or empty on a local build. Served by <c>/healthz/version</c> so a staging-to-production swap knows what it promotes.
    /// </summary>
    public static string Commit { get; } =
        typeof(AppVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GadflyCommit")?.Value ?? "";
}
