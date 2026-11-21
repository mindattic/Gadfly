using Gadfly.Socrates;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Configuration;

/// <summary>GadflyConfiguration.Build against a temp project dir (GAD-T5). Builds a config object only; no client, no provider call.</summary>
[TestFixture]
[NonParallelizable] // GADFLY_CLAUDE_MODEL is process-wide
public class GadflyConfigurationTests
{
    private string dir = "";
    private string? savedEnv;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "gadfly-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        savedEnv = Environment.GetEnvironmentVariable("GADFLY_CLAUDE_MODEL");
        Environment.SetEnvironmentVariable("GADFLY_CLAUDE_MODEL", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("GADFLY_CLAUDE_MODEL", savedEnv);
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteLegionJson(string json) => File.WriteAllText(Path.Combine(dir, "legion.json"), json);

    [Test]
    public void NoLegionJson_UsesSharedCredentials_JudgeClaude_AndTheTrustedPanel()
    {
        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.UseSharedCredentials, Is.True);
        Assert.That(config.JudgeProviderId, Is.EqualTo("claude"));
        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(LegionTierResolver.TrustedProviderIds));
    }

    [TestCase(ModelTier.Low)]
    [TestCase(ModelTier.High)]
    [TestCase(ModelTier.Highest)]
    public void NoLegionJson_AppliesTheTiersModelOverrides_ForEveryNonClaudeProvider(ModelTier tier)
    {
        var config = GadflyConfiguration.Build(tier, dir);

        foreach (var kv in LegionTierResolver.BuildTierModelOverrides(tier).Where(k => !k.Key.Equals("claude", StringComparison.OrdinalIgnoreCase)))
            Assert.That(config.ModelOverrides[kv.Key], Is.EqualTo(kv.Value), kv.Key);
    }

    [Test]
    public void DefaultTier_IsHigh()
    {
        var implicitTier = GadflyConfiguration.Build(projectDir: dir);
        var explicitHigh = GadflyConfiguration.Build(ModelTier.High, dir);

        Assert.That(implicitTier.ModelOverrides, Is.EquivalentTo(explicitHigh.ModelOverrides));
    }

    // ---- Claude is pinned to Sonnet 5 (GAD-T150) --------------------------------------------

    [Test]
    public void ClaudeIsPinnedToSonnet5_ByDefault()
    {
        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(GadflyConfiguration.DefaultClaudeModel, Is.EqualTo("claude-sonnet-5"));
        Assert.That(config.ModelOverrides["claude"], Is.EqualTo("claude-sonnet-5"));
    }

    [TestCase(ModelTier.Low)]
    [TestCase(ModelTier.Medium)]
    [TestCase(ModelTier.High)]
    [TestCase(ModelTier.Higher)]
    [TestCase(ModelTier.Highest)]
    public void ClaudeIsPinnedToSonnet5_AtEveryTier(ModelTier tier)
    {
        var config = GadflyConfiguration.Build(tier, dir);

        Assert.That(config.ModelOverrides["claude"], Is.EqualTo(GadflyConfiguration.DefaultClaudeModel));
    }

    [Test]
    public void ExplicitClaudeModel_WinsOverTheDefault_AndOverTheEnvironment()
    {
        Environment.SetEnvironmentVariable("GADFLY_CLAUDE_MODEL", "from-env");

        var config = GadflyConfiguration.Build(projectDir: dir, claudeModel: "from-argument");

        Assert.That(config.ModelOverrides["claude"], Is.EqualTo("from-argument"));
    }

    [Test]
    public void EnvironmentClaudeModel_IsUsed_WhenNoArgumentIsGiven()
    {
        Environment.SetEnvironmentVariable("GADFLY_CLAUDE_MODEL", "from-env");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.ModelOverrides["claude"], Is.EqualTo("from-env"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void BlankClaudeModelArgument_FallsBackToTheDefault(string blank)
    {
        var config = GadflyConfiguration.Build(projectDir: dir, claudeModel: blank);

        Assert.That(config.ModelOverrides["claude"], Is.EqualTo(GadflyConfiguration.DefaultClaudeModel));
    }

    [Test]
    public void LegionJsonModels_CannotUnpinClaude()
    {
        WriteLegionJson("""{ "models": { "claude": "some-expensive-model" } }""");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.ModelOverrides["claude"], Is.EqualTo(GadflyConfiguration.DefaultClaudeModel));
    }

    // ---- legion.json -------------------------------------------------------------------------

    [Test]
    public void LegionJsonVoterList_IsApplied()
    {
        WriteLegionJson("""{ "voters": ["openai", "gemini"] }""");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "openai", "gemini" }));
    }

    [Test]
    public void LegionJsonVoterList_CannotAddAnUntrustedProvider()
    {
        WriteLegionJson("""{ "voters": ["openai", "mistral"] }""");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "openai" }), "GAD-T220");
    }

    [Test]
    public void LegionJsonWithOnlyUntrustedVoters_FallsBackToTheTrustedPanel_NeverAnEmptyList()
    {
        WriteLegionJson("""{ "voters": ["mistral"], "judge": "mistral" }""");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(LegionTierResolver.TrustedProviderIds));
        Assert.That(config.JudgeProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void LegionJsonJudgeAndConcurrency_AreApplied()
    {
        WriteLegionJson("""{ "judge": "openai", "maxConcurrency": 2 }""");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.JudgeProviderId, Is.EqualTo("openai"));
        Assert.That(config.MaxConcurrency, Is.EqualTo(2));
    }

    [Test]
    public void TierOverrides_StillWin_OverLegionJsonModels()
    {
        WriteLegionJson("""{ "models": { "openai": "legion-json-model" } }""");

        var config = GadflyConfiguration.Build(ModelTier.High, dir);

        Assert.That(config.ModelOverrides["openai"], Is.EqualTo(LegionTierResolver.BuildTierModelOverrides(ModelTier.High)["openai"]));
    }

    [Test]
    public void MalformedLegionJson_IsIgnored_AndDefaultsApply()
    {
        WriteLegionJson("this is not json");

        var config = GadflyConfiguration.Build(projectDir: dir);

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(LegionTierResolver.TrustedProviderIds));
        Assert.That(config.JudgeProviderId, Is.EqualTo("claude"));
    }

    // ---- --voters / --single-voter override --------------------------------------------------

    [Test]
    public void ProviderOverride_NarrowsThePanel_WithNoLegionJson()
    {
        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "gemini" });

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "gemini" }));
    }

    [Test]
    public void ProviderOverride_Wins_OverABroaderLegionJsonVoterList()
    {
        WriteLegionJson("""{ "voters": ["claude", "openai", "gemini", "deepseek"] }""");

        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "openai" });

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "openai" }));
    }

    [Test]
    public void ProviderOverride_Wins_OverADifferentLegionJsonVoterList()
    {
        WriteLegionJson("""{ "voters": ["claude"] }""");

        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "deepseek" });

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "deepseek" }));
    }

    [Test]
    public void ProviderOverride_FiltersOutUntrustedProviderIds()
    {
        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "openai", "mistral", "xai" });

        Assert.That(config.AllowedProviderIds, Is.EquivalentTo(new[] { "openai" }));
    }

    [Test]
    public void ProviderOverride_IsCaseInsensitive()
    {
        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "OpenAI" });

        Assert.That(config.AllowedProviderIds, Has.Count.EqualTo(1));
        Assert.That(config.AllowedProviderIds.Single(), Is.EqualTo("openai").IgnoreCase);
    }

    [Test]
    public void ProviderOverride_NeverWidensThePanel_BeyondTheTrustedSet()
    {
        var config = GadflyConfiguration.Build(projectDir: dir, providerOverride: new[] { "claude", "openai", "gemini", "deepseek", "mistral", "groq" });

        Assert.That(config.AllowedProviderIds, Is.SubsetOf(LegionTierResolver.TrustedProviderIds));
    }

    [Test]
    public void ProviderOverride_DoesNotChangeTheModelOverrides()
    {
        var plain = GadflyConfiguration.Build(ModelTier.High, dir);
        var narrowed = GadflyConfiguration.Build(ModelTier.High, dir, new[] { "gemini" });

        Assert.That(narrowed.ModelOverrides, Is.EquivalentTo(plain.ModelOverrides));
    }
}
