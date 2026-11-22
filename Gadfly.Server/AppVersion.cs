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
}
