using MindAttic.Legion;

namespace Gadfly.Socrates;

/// <summary>
/// Resolves a <see cref="ModelTier"/> into per-provider model overrides for the
/// trusted-four panel. <c>legion.json</c>'s <c>"tier"</c> key is not a real field
/// (confirmed against <see cref="MindAttic.Legion.LegionConfig"/> — it only
/// deserializes voters/judge/models/apiKeys/maxConcurrency), so tier selection
/// happens here, in code, the same way <c>MindAttic.Legion.Cli</c>'s
/// <c>AskCommand.BuildTierModelOverrides</c> does it for <c>legion ask</c>.
/// </summary>
public static class LegionTierResolver
{
    public static readonly string[] TrustedProviderIds = { "claude", "openai", "gemini", "deepseek" };

    /// <summary>
    /// Gadfly's default tier. This is a reflective, high-stakes personal-insight
    /// tool — it runs on flagship reasoning models, not each provider's cheap
    /// default, the same reasoning <c>legion ask</c> applies to architecture calls.
    /// </summary>
    public const ModelTier DefaultTier = ModelTier.High;

    public static Dictionary<string, string> BuildTierModelOverrides(ModelTier tier)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in TrustedProviderIds)
        {
            var model = LlmProviderCatalog.GetTieredModel(id, tier);
            if (!string.IsNullOrWhiteSpace(model))
                overrides[id] = model!;
        }
        return overrides;
    }

    /// <summary>
    /// Intersects <paramref name="requested"/> with <see cref="TrustedProviderIds"/> (case-insensitive) —
    /// an untrusted id can never reach the panel this way, mirroring
    /// <c>MindAttic.Legion.Cli.AskCommand.IntersectWithTrustedSet</c>. Null/empty means "no narrowing
    /// requested", i.e. every trusted provider is eligible.
    /// </summary>
    public static HashSet<string> IntersectWithTrustedSet(IEnumerable<string>? requested)
    {
        var trusted = new HashSet<string>(TrustedProviderIds, StringComparer.OrdinalIgnoreCase);
        var list = requested?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (list is null || list.Count == 0) return trusted;
        trusted.IntersectWith(list);
        return trusted;
    }
}
