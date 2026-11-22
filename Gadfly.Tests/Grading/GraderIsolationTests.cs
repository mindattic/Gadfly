using Gadfly.Assessment;
using Gadfly.Sources.Grading;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>
/// GAD-T430 (ElenchusAtScale Sections 3.8 and 5.4.2, M1, M12): the offline grader is kept apart from the examiner's
/// exam-time judgments, and sees the evidence in a shuffled order whose labels stay stable so citations still resolve.
/// </summary>
[TestFixture]
public class GraderIsolationTests
{
    private static readonly Guid R1 = Guid.NewGuid(), R2 = Guid.NewGuid(), R3 = Guid.NewGuid(), R4 = Guid.NewGuid();

    private static GradingObjective Objective() =>
        new("calvin", "The Calvin cycle", null, 100m, AiObjectiveGrader.DefaultCriteria());

    private static List<EvidenceItem> Evidence() =>
    [
        new(R1, 1, "Question one?", "Answer about the stroma of the chloroplast.", VerificationState.Confirmed,
            Note: "EXAMINER-NOTE-ONE", Stance: EvidenceStanceHint.Supports),
        new(R2, 2, "Question two?", "Answer about ATP and NADPH.", VerificationState.Confirmed,
            Note: "EXAMINER-NOTE-TWO", Stance: EvidenceStanceHint.Contradicts),
        new(R3, 3, "Question three?", "Answer about rubisco fixing carbon.", VerificationState.Confirmed, Stance: EvidenceStanceHint.Misconception),
        new(R4, 4, "Question four?", "Answer about glucose being made later.", VerificationState.Confirmed, Stance: EvidenceStanceHint.Uncertain),
    ];

    private const string CitesFirst =
        """{"criteria":[{"criterion":"Factual knowledge","evidence":[1],"quote":"the stroma of the chloroplast","rationale":"r","level":4}],"explanation":"e","misconceptions":[],"ambiguities":[]}""";

    [Test]
    public async Task ExamTimeStanceAndNotes_NeverReachTheGradingPrompt()
    {
        var panel = new GradingPanel(("a", CitesFirst));
        await new AiObjectiveGrader(panel, new Random(1)).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        var prompt = panel.Prompts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Not.Contain("Reviewer note"));
            Assert.That(prompt, Does.Not.Contain("EXAMINER-NOTE"));
            foreach (var stance in Enum.GetNames<EvidenceStanceHint>()) Assert.That(prompt, Does.Not.Contain($": {stance}"));
        });
    }

    [Test]
    public async Task ContradictingStance_StillFlagsTheHumanReviewer()
    {
        var r = await new AiObjectiveGrader(new GradingPanel(("a", CitesFirst)), new Random(1)).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.ContradictingEvidence));
    }

    [Test]
    public async Task EvidenceIsShuffled_ButEachLabelStaysWithItsResponse()
    {
        var orders = new HashSet<string>();
        for (var seed = 0; seed < 40; seed++)
        {
            var panel = new GradingPanel(("a", CitesFirst));
            await new AiObjectiveGrader(panel, new Random(seed)).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
            var prompt = panel.Prompts.Single();
            for (var n = 1; n <= 4; n++)
                Assert.That(prompt, Does.Contain($"[{n}] Question: Question {Word(n)}?"), $"seed {seed}: label {n} moved to another response");
            var positions = Enumerable.Range(1, 4).OrderBy(n => prompt.IndexOf($"[{n}] Question", StringComparison.Ordinal));
            orders.Add(string.Join(",", positions));
        }
        Assert.That(orders, Has.Count.GreaterThan(1), "the evidence was always listed in exam order");
    }

    [Test]
    public async Task CitationsResolveToTheLabelledResponse_WhateverTheListedOrder()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var r = await new AiObjectiveGrader(new GradingPanel(("a", CitesFirst)), new Random(seed))
                .GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
            var factual = r.Criteria.Single(c => c.Criterion == "Factual knowledge");
            Assert.That(factual.EvidenceRefs, Is.EqualTo(new[] { R1 }), $"seed {seed}");
            Assert.That(factual.Quotes, Is.EqualTo(new[] { "the stroma of the chloroplast" }), $"seed {seed}");
        }
    }

    [Test]
    public void WithoutAShuffle_EvidenceIsListedInExamOrder()
    {
        var evidence = Evidence();
        var prompt = AiObjectiveGrader.BuildPrompt(Objective(), AiObjectiveGrader.DefaultCriteria(), evidence, []);
        var positions = Enumerable.Range(1, 4).Select(n => prompt.IndexOf($"[{n}] Question", StringComparison.Ordinal)).ToList();
        Assert.That(positions, Is.Ordered);
        Assert.That(prompt, Does.Not.Contain("EXAMINER-NOTE"));
    }

    private static string Word(int n) => n switch { 1 => "one", 2 => "two", 3 => "three", _ => "four" };
}
