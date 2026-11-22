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
    /// <summary>The Claude model Gadfly uses unless configured otherwise.</summary>
    public const string DefaultClaudeModel = "claude-sonnet-5";

    public static VotingConfiguration Build(
        ModelTier tier = LegionTierResolver.DefaultTier,
        string? projectDir = null,
        IEnumerable<string>? providerOverride = null,
        string? claudeModel = null)
    {
        var config = new VotingConfiguration
        {
            UseSharedCredentials = true,
            AllowedProviderIds = LegionTierResolver.IntersectWithTrustedSet(providerOverride),
            JudgeProviderId = "claude",
            ModelOverrides = LegionTierResolver.BuildTierModelOverrides(tier),
            // Legion's own default (2 minutes) lets one stalled provider hold up a whole panel call on a live exam, where
            // every question waits on it (GAD-T404): the panel is already built to tolerate a timed-out voter (a failed
            // provider in AllowedProviderIds is refilled from the surviving ones, so quorum doesn't shrink), so cutting
            // this well below that default makes a stuck call fall through to the others sooner without weakening the vote.
            ProviderTimeout = TimeSpan.FromSeconds(25),
        };

        var legionConfig = LegionConfig.LoadFromDirectory(projectDir ?? Environment.CurrentDirectory);
        legionConfig?.ApplyTo(config);

        // Re-apply tier and an explicit --voters/--single-voter override last so they always
        // win over legion.json: tier because LegionConfig never sets ModelOverrides for it (the
        // "tier" json key is not a real field), and the provider override because a CLI flag
        // should narrow the panel even if legion.json's own voter list is broader.
        foreach (var kv in LegionTierResolver.BuildTierModelOverrides(tier))
            config.ModelOverrides[kv.Key] = kv.Value;
        // Claude is pinned to Sonnet 5 at every tier unless told otherwise (owner, 2026-11-21: spend credits sparingly).
        config.ModelOverrides["claude"] = !string.IsNullOrWhiteSpace(claudeModel) ? claudeModel
            : Environment.GetEnvironmentVariable("GADFLY_CLAUDE_MODEL") is { Length: > 0 } env ? env
            : DefaultClaudeModel;
        if (providerOverride is not null)
            config.AllowedProviderIds = LegionTierResolver.IntersectWithTrustedSet(providerOverride);
        // legion.json can't widen the panel past the trusted providers either (GAD-T220): its voter list is narrowed
        // to them, and a list with none of them falls back to all of them, never to an empty (unrestricted) list.
        var trusted = LegionTierResolver.IntersectWithTrustedSet(config.AllowedProviderIds);
        config.AllowedProviderIds = trusted.Count > 0 ? trusted : LegionTierResolver.IntersectWithTrustedSet(null);
        if (!LegionTierResolver.TrustedProviderIds.Contains(config.JudgeProviderId ?? "", StringComparer.OrdinalIgnoreCase))
            config.JudgeProviderId = "claude";

        return config;
    }
}
