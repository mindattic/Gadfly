using Gadfly.Socrates;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

[TestFixture]
public class LegionTierResolverTests
{
    [Test]
    public void BuildTierModelOverrides_CoversEveryTrustedProvider()
    {
        var overrides = LegionTierResolver.BuildTierModelOverrides(ModelTier.High);

        foreach (var id in LegionTierResolver.TrustedProviderIds)
            Assert.That(overrides.ContainsKey(id), Is.True, $"expected a High-tier model override for '{id}'");
    }

    [Test]
    public void IntersectWithTrustedSet_DropsUntrustedProvider()
    {
        var result = LegionTierResolver.IntersectWithTrustedSet(new[] { "claude", "some-untrusted-provider" });

        Assert.That(result, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void IntersectWithTrustedSet_ReturnsFullTrustedSet_WhenRequestedIsNullOrEmpty()
    {
        Assert.That(LegionTierResolver.IntersectWithTrustedSet(null), Is.EquivalentTo(LegionTierResolver.TrustedProviderIds));
        Assert.That(LegionTierResolver.IntersectWithTrustedSet(Array.Empty<string>()), Is.EquivalentTo(LegionTierResolver.TrustedProviderIds));
    }
}
