using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly;

/// <summary>
/// Builds the <see cref="VotingConfiguration"/> Gadfly hands to
/// <see cref="LlmVotingService"/>: shared-credential resolution (no Vault bucket
/// of Gadfly's own — Legion already reads the shared
/// <c>%APPDATA%\MindAttic\LLM\providers.json</c> bucket), the repo's
/// <c>legion.json</c> (voters/judge/maxConcurrency only) applied on top, then
/// the requested <see cref="ModelTier"/> resolved into model overrides.
/// </summary>
public static class GadflyConfiguration
{
    public static VotingConfiguration Build(
        ModelTier tier = LegionTierResolver.DefaultTier,
        string? projectDir = null,
        IEnumerable<string>? providerOverride = null)
    {
        var config = new VotingConfiguration
        {
            UseSharedCredentials = true,
            AllowedProviderIds = LegionTierResolver.IntersectWithTrustedSet(providerOverride),
            JudgeProviderId = "claude",
            ModelOverrides = LegionTierResolver.BuildTierModelOverrides(tier),
        };

        var legionConfig = LegionConfig.LoadFromDirectory(projectDir ?? Environment.CurrentDirectory);
        legionConfig?.ApplyTo(config);

        // Re-apply tier and an explicit --voters/--single-voter override last so they always
        // win over legion.json: tier because LegionConfig never sets ModelOverrides for it (the
        // "tier" json key is not a real field), and the provider override because a CLI flag
        // should narrow the panel even if legion.json's own voter list is broader.
        foreach (var kv in LegionTierResolver.BuildTierModelOverrides(tier))
            config.ModelOverrides[kv.Key] = kv.Value;
        if (providerOverride is not null)
            config.AllowedProviderIds = LegionTierResolver.IntersectWithTrustedSet(providerOverride);

        return config;
    }
}
