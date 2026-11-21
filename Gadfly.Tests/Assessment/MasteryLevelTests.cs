using Gadfly.Assessment;
using NUnit.Framework;

namespace Gadfly.Tests.Assessment;

/// <summary>Part 5.3 understanding levels: labels, default descriptors and conclusiveness for every level.</summary>
[TestFixture]
public class MasteryLevelTests
{
    [TestCase(MasteryLevel.InsufficientEvidence, 0, "Insufficient Evidence")]
    [TestCase(MasteryLevel.Recognition, 1, "Recognition")]
    [TestCase(MasteryLevel.BasicExplanation, 2, "Basic Explanation")]
    [TestCase(MasteryLevel.ConceptualUnderstanding, 3, "Conceptual Understanding")]
    [TestCase(MasteryLevel.Application, 4, "Application")]
    [TestCase(MasteryLevel.TransferAndCriticalReasoning, 5, "Transfer and Critical Reasoning")]
    public void Level_HasPlanNumberAndLabel(MasteryLevel level, int number, string label)
    {
        Assert.That((int)level, Is.EqualTo(number));
        Assert.That(MasteryLevels.Label(level), Is.EqualTo(label));
    }

    [Test]
    public void ThereAreExactlySixLevels_ZeroToFive()
    {
        var values = Enum.GetValues<MasteryLevel>().Select(v => (int)v).ToArray();
        Assert.That(values, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
    }

    [TestCase(MasteryLevel.InsufficientEvidence, "not enough reliable evidence")]
    [TestCase(MasteryLevel.Recognition, "recognizes relevant concepts")]
    [TestCase(MasteryLevel.BasicExplanation, "partial accuracy")]
    [TestCase(MasteryLevel.ConceptualUnderstanding, "underlying relationships")]
    [TestCase(MasteryLevel.Application, "applies concepts to relevant problems")]
    [TestCase(MasteryLevel.TransferAndCriticalReasoning, "unfamiliar situations")]
    public void DefaultDescriptor_DescribesTheLevel(MasteryLevel level, string phrase)
    {
        var d = MasteryLevels.DefaultDescriptor(level);
        Assert.That(d, Does.Contain(phrase));
        Assert.That(d, Does.EndWith("."));
    }

    [Test]
    public void EveryLevel_HasADistinctLabelAndDescriptor()
    {
        var levels = Enum.GetValues<MasteryLevel>();
        Assert.That(levels.Select(MasteryLevels.Label).Distinct().Count(), Is.EqualTo(levels.Length));
        Assert.That(levels.Select(MasteryLevels.DefaultDescriptor).Distinct().Count(), Is.EqualTo(levels.Length));
    }

    [Test]
    public void LevelZero_MeansInsufficientEvidence_NeverNoUnderstanding()
    {
        Assert.That(MasteryLevels.IsConclusive(MasteryLevel.InsufficientEvidence), Is.False);
        Assert.That(MasteryLevels.Label(MasteryLevel.InsufficientEvidence), Does.Not.Contain("No Understanding"));
        Assert.That(MasteryLevels.DefaultDescriptor(MasteryLevel.InsufficientEvidence), Does.Contain("not provide enough reliable evidence"));
    }

    [TestCase(MasteryLevel.Recognition)]
    [TestCase(MasteryLevel.BasicExplanation)]
    [TestCase(MasteryLevel.ConceptualUnderstanding)]
    [TestCase(MasteryLevel.Application)]
    [TestCase(MasteryLevel.TransferAndCriticalReasoning)]
    public void EveryRealLevel_IsConclusive(MasteryLevel level) => Assert.That(MasteryLevels.IsConclusive(level), Is.True);

    [Test]
    public void UndefinedLevel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MasteryLevels.Label((MasteryLevel)6));
        Assert.Throws<ArgumentOutOfRangeException>(() => MasteryLevels.DefaultDescriptor((MasteryLevel)(-1)));
    }

    [Test]
    public void Dimensions_CoverPart51()
    {
        var names = Enum.GetNames<AssessmentDimension>();
        Assert.That(names, Is.SupersetOf(new[]
        {
            "FactualKnowledge", "ConceptualUnderstanding", "LogicalReasoning", "Application", "KnowledgeTransfer",
            "MisconceptionRecognition", "Consistency", "Metacognition", "Custom",
        }));
    }

    [Test]
    public void Modes_AreExaminationAndLearning() =>
        Assert.That(Enum.GetNames<AssessmentMode>(), Is.EquivalentTo(new[] { "Examination", "Learning" }));

    [Test]
    public void CorrectionKinds_DistinguishThePart47Cases() =>
        Assert.That(Enum.GetNames<CorrectionKind>(), Is.EquivalentTo(new[] { "Transcription", "MeaningClarification", "SubstantiveRevision" }));
}
