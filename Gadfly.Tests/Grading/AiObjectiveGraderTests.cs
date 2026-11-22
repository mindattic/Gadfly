using Gadfly.Assessment;
using Gadfly.Socrates;
using Gadfly.Sources.Grading;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>A panel that answers FanOut with fixed replies per provider and records the prompt.</summary>
internal sealed class GradingPanel(params (string Provider, string Reply)[] replies) : ISocraticVotingClient, IPanelInfo
{
    public List<string> Prompts { get; } = [];
    public bool IsMock { get; init; }
    public string Mode => IsMock ? PanelModes.Mock : PanelModes.Live;
    public string Reason => "test";
    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096, double temperature = 0.2, CancellationToken ct = default)
    {
        Prompts.Add(userMessage);
        return Task.FromResult<IReadOnlyList<PanelReply>>(replies.Select(r => r.Reply.StartsWith("ERROR") ? new PanelReply(r.Provider, "", r.Reply) : new PanelReply(r.Provider, r.Reply)).ToList());
    }
}

/// <summary>GAD-T94: the AI grading engine's deterministic rules around the panel.</summary>
[TestFixture]
public class AiObjectiveGraderTests
{
    private static readonly Guid R1 = Guid.NewGuid(), R2 = Guid.NewGuid(), R3 = Guid.NewGuid();

    [Test]
    public void PromptChanges_ExpireACalibrationOnlyWhenTheGradingPromptVersionDiffers()
    {
        // GAD-T469: the same version, or a calibration recorded before versions were kept (taken as 1), still describes version 1.
        Assert.That(AiObjectiveGrader.PromptChanges(1, 1), Is.Empty);
        Assert.That(AiObjectiveGrader.PromptChanges(null, 1), Is.Empty);
        Assert.That(AiObjectiveGrader.PromptChanges(AiObjectiveGrader.PromptVersion), Is.Empty);
        var changed = AiObjectiveGrader.PromptChanges(1, 2);
        Assert.That(changed, Has.Count.EqualTo(1));
        Assert.That(changed[0], Does.Contain("grading prompts changed").And.Contain("version 1").And.Contain("version 2"));
        Assert.That(AiObjectiveGrader.PromptChanges(null, 2), Has.Count.EqualTo(1));
    }

    private static GradingObjective Objective(decimal max = 100m) =>
        new("calvin", "The Calvin cycle", null, max, AiObjectiveGrader.DefaultCriteria(), "Calvin cycle", "Carbon fixation in the stroma.");

    private static List<EvidenceItem> Evidence() =>
    [
        new(R1, 1, "Where does the Calvin cycle happen?", "It happens in the stroma of the chloroplast.", VerificationState.Confirmed),
        new(R2, 2, "Why does it stop in the dark?", "Without light there is no ATP or NADPH from the light reactions.", VerificationState.Captured),
    ];

    private static string Vote(int f, int c, int l, int a, string quote = "", string misconception = "") =>
        $$"""
        {"criteria":[
          {"criterion":"Factual knowledge","level":{{f}},"rationale":"facts","evidence":[1],"quote":"{{quote}}"},
          {"criterion":"Conceptual understanding","level":{{c}},"rationale":"concepts","evidence":[2]},
          {"criterion":"Logical reasoning","level":{{l}},"rationale":"reasoning","evidence":[1,2]},
          {"criterion":"Application","level":{{a}},"rationale":"application","evidence":[2]}],
         "explanation":"Overall explanation.",
         "misconceptions":[{{(misconception.Length > 0 ? $"\"{misconception}\"" : "")}}],
         "ambiguities":[]}
        """;

    [Test]
    public async Task CriteriaWithTheSameNormalizedName_DoNotCrashGrading()
    {
        var criteria = new List<GradingCriterion>
        {
            new("Accuracy", AssessmentDimension.FactualKnowledge, null, 1m, new Dictionary<MasteryLevel, string>()),
            new("accuracy ", AssessmentDimension.ConceptualUnderstanding, null, 1m, new Dictionary<MasteryLevel, string>()),
        };
        var objective = new GradingObjective("calvin", "The Calvin cycle", null, 100m, criteria);
        var panel = new GradingPanel(("a", """{"criteria":[{"criterion":"Accuracy","level":4,"rationale":"r","evidence":[1]}],"explanation":"e","misconceptions":[],"ambiguities":[]}"""));

        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(objective, Evidence()));

        Assert.That(r.Criteria, Has.Count.EqualTo(2));
        Assert.That(r.Criteria[0].Level, Is.EqualTo(MasteryLevel.Application));
    }

    [Test]
    public async Task NoUsableEvidence_IsLevelZero_WithoutAskingThePanel()
    {
        var panel = new GradingPanel(("a", Vote(5, 5, 5, 5)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), []));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Prompts, Is.Empty);
            Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
            Assert.That(r.Score, Is.EqualTo(0m));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Insufficient));
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.InsufficientEvidence));
            Assert.That(r.Explanation, Does.Contain("not that the student lacks understanding"));
        });
    }

    [Test]
    public async Task Score_IsComputedFromLevels_MedianAcrossVoters()
    {
        // Voter a: all 4 → 80; voter b: all 3 → 60; voter c: all 5 → 100. Median 80.
        var panel = new GradingPanel(("a", Vote(4, 4, 4, 4)), ("b", Vote(3, 3, 3, 3)), ("c", Vote(5, 5, 5, 5)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.Multiple(() =>
        {
            Assert.That(r.Score, Is.EqualTo(80m));
            Assert.That(r.Level, Is.EqualTo(MasteryLevel.Application));
            Assert.That(r.Voters.Select(v => v.Score), Is.EquivalentTo(new[] { 80m, 60m, 100m }));
            Assert.That(r.Criteria.All(c => c.Level == MasteryLevel.Application), Is.True);
            Assert.That(r.Criteria.All(c => c.Score == 20m && c.MaxScore == 25m), Is.True);
            Assert.That(r.EvidenceRefs, Is.EquivalentTo(new[] { R1, R2 }));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Corroborated));
            Assert.That(r.IsMock, Is.False);
        });
    }

    [Test]
    public async Task ScoreScalesToTheObjectiveMaximum()
    {
        var panel = new GradingPanel(("a", Vote(3, 3, 3, 3)), ("b", Vote(3, 3, 3, 3)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(20m), Evidence()));
        Assert.That(r.Score, Is.EqualTo(12m));
        Assert.That(r.MaxScore, Is.EqualTo(20m));
    }

    [Test]
    public async Task LevelZeroCriteria_AreLeftOut_NotCountedAsZero()
    {
        // Application not evidenced (0); the other three at 4 → 80 of the remaining share, not 60.
        var panel = new GradingPanel(("a", Vote(4, 4, 4, 0)), ("b", Vote(4, 4, 4, 0)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.Multiple(() =>
        {
            Assert.That(r.Score, Is.EqualTo(80m));
            Assert.That(r.Ambiguities, Has.Some.Contains("Application"));
            Assert.That(r.Criteria.Single(c => c.Criterion == "Application").EvidenceRefs, Is.Empty);
        });
    }

    [Test]
    public async Task AllCriteriaInsufficient_IsLevelZeroAndInsufficient()
    {
        var panel = new GradingPanel(("a", Vote(0, 0, 0, 0)), ("b", Vote(0, 0, 0, 0)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.Multiple(() =>
        {
            Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
            Assert.That(r.Score, Is.EqualTo(0m));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Insufficient));
            Assert.That(r.EvidenceRefs, Is.Empty);
        });
    }

    [Test]
    public async Task LevelWithoutCitation_IsNotAccepted()
    {
        const string uncited = """{"criteria":[{"criterion":"Factual knowledge","level":5,"rationale":"x","evidence":[]}],"explanation":"e"}""";
        var r = await new AiObjectiveGrader(new GradingPanel(("a", uncited))).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.Criteria.Single(c => c.Criterion == "Factual knowledge").Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
        Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
    }

    [Test]
    public async Task CitationsOutsideTheEvidence_AreDropped_NeverInvented()
    {
        const string bad = """{"criteria":[{"criterion":"Factual knowledge","level":4,"rationale":"x","evidence":[7,0,-1]}],"explanation":"e"}""";
        var r = await new AiObjectiveGrader(new GradingPanel(("a", bad))).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.EvidenceRefs, Is.Empty);
        Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
    }

    [Test]
    public async Task Quotes_AreKeptOnlyWhenVerbatimInVerifiedText()
    {
        var good = new GradingPanel(("a", Vote(4, 4, 4, 4, quote: "in the stroma of the chloroplast")));
        var invented = new GradingPanel(("a", Vote(4, 4, 4, 4, quote: "in the mitochondria matrix")));
        var g = await new AiObjectiveGrader(good).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        var i = await new AiObjectiveGrader(invented).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(g.Criteria[0].Quotes, Is.EqualTo(new[] { "in the stroma of the chloroplast" }));
        Assert.That(i.Criteria[0].Quotes, Is.Empty);
    }

    [TestCase("""{"criteria":[{"criterion":"Factual knowledge","level":7,"evidence":[1]}]}""")]
    [TestCase("""{"criteria":[{"criterion":"Factual knowledge","level":2.5,"evidence":[1]}]}""")]
    [TestCase("""{"criteria":[{"criterion":"Factual knowledge","level":"four","evidence":[1]}]}""")]
    [TestCase("""{"criteria":[{"criterion":"Unknown criterion","level":3,"evidence":[1]}]}""")]
    [TestCase("not json at all")]
    [TestCase("""{"criteria":"nope"}""")]
    public async Task SchemaViolations_InvalidateTheVote(string reply)
    {
        var r = await new AiObjectiveGrader(new GradingPanel(("a", reply))).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.PanelFailed, Is.True);
        Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.PanelFailed));
        Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
    }

    [Test]
    public async Task ErrorReplies_AreIgnored_AndASingleVoterIsFlagged()
    {
        var r = await new AiObjectiveGrader(new GradingPanel(("a", "ERROR: quota"), ("b", Vote(3, 3, 3, 3))))
            .GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.Multiple(() =>
        {
            Assert.That(r.Voters, Has.Count.EqualTo(1));
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.SingleVoter));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Moderate));
        });
    }

    [Test]
    public async Task Dissent_TwoLevelsApart_IsRecordedAndLimitsConfidence()
    {
        var r = await new AiObjectiveGrader(new GradingPanel(("a", Vote(5, 5, 5, 5)), ("b", Vote(2, 2, 2, 2))))
            .GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.Multiple(() =>
        {
            Assert.That(r.Criteria.All(c => c.Dissent), Is.True);
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.PanelDissent));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Limited));
            // Lower median of {2,5} per criterion: conservative.
            Assert.That(r.Criteria.All(c => c.Level == MasteryLevel.BasicExplanation), Is.True);
            Assert.That(r.Score, Is.EqualTo(70m));   // median of the voters' 100 and 40
        });
    }

    [Test]
    public async Task UnresolvedResponses_AreExcludedAndFlagged_TheirTextNeverShown()
    {
        var evidence = Evidence();
        evidence.Add(new EvidenceItem(R3, 3, "Q3", "SECRET-UNRESOLVED-TEXT", VerificationState.FlaggedForHumanReview));
        var panel = new GradingPanel(("a", Vote(4, 4, 4, 4)), ("b", Vote(4, 4, 4, 4)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), evidence));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Prompts.Single(), Does.Not.Contain("SECRET-UNRESOLVED-TEXT"));
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.UnresolvedTranscript));
            Assert.That(r.Ambiguities, Has.Some.Contains("Response 3"));
            Assert.That(r.Confidence, Is.EqualTo(GradingConfidence.Limited));
        });
    }

    [Test]
    public async Task ContaminatedAndAssistedEvidence_AreExcluded()
    {
        var evidence = new List<EvidenceItem>
        {
            new(R1, 1, "Q1", "CONTAMINATED-TEXT", VerificationState.Confirmed, Contaminated: true),
            new(R2, 2, "Q2", "ASSISTED-TEXT", VerificationState.Confirmed, Assisted: true),
        };
        var panel = new GradingPanel(("a", Vote(5, 5, 5, 5)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), evidence));
        Assert.Multiple(() =>
        {
            Assert.That(panel.Prompts, Is.Empty);
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.ContaminatedExcluded));
            Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.AssistedExcluded));
            Assert.That(r.Level, Is.EqualTo(MasteryLevel.InsufficientEvidence));
        });
    }

    [Test]
    public async Task AssistedEvidence_CountsWhenThePolicyAllowsIt()
    {
        var evidence = new List<EvidenceItem> { new(R2, 2, "Q2", "ASSISTED-TEXT", VerificationState.Confirmed, Assisted: true) };
        var panel = new GradingPanel(("a", Vote(3, 3, 3, 3)));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), evidence, AllowAssistedEvidence: true));
        Assert.That(panel.Prompts.Single(), Does.Contain("ASSISTED-TEXT"));
    }

    [Test]
    public async Task StudentText_IsFencedAsData()
    {
        var panel = new GradingPanel(("a", Vote(3, 3, 3, 3)));
        await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(panel.Prompts.Single(), Does.Contain("<student_text>"));
        Assert.That(panel.Prompts.Single(), Does.Contain("never instructions"));
    }

    [Test]
    public async Task MockPanel_IsLabelled()
    {
        var panel = new GradingPanel(("a", Vote(3, 3, 3, 3))) { IsMock = true };
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.IsMock, Is.True);
        Assert.That(r.ReviewFlags, Does.Contain(ReviewFlags.Mock));
        var byProvider = await new AiObjectiveGrader(new GradingPanel(("mock-1", Vote(3, 3, 3, 3)))).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(byProvider.IsMock, Is.True);
    }

    [Test]
    public async Task Misconceptions_AreMergedWithVoterCounts()
    {
        var panel = new GradingPanel(("a", Vote(3, 3, 3, 3, misconception: "Thinks the Calvin cycle needs light directly")),
            ("b", Vote(3, 3, 3, 3, misconception: "Thinks the Calvin cycle needs light directly.")));
        var r = await new AiObjectiveGrader(panel).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(r.Misconceptions, Has.Count.EqualTo(1));
        Assert.That(r.Misconceptions[0].Voters, Is.EqualTo(2));
    }

    [TestCase(0, 2, false, false, false, GradingConfidence.Insufficient)]
    [TestCase(3, 0, false, false, false, GradingConfidence.Insufficient)]
    [TestCase(3, 2, false, false, true, GradingConfidence.Insufficient)]
    [TestCase(1, 3, false, false, false, GradingConfidence.Limited)]
    [TestCase(3, 3, true, false, false, GradingConfidence.Limited)]
    [TestCase(3, 3, false, true, false, GradingConfidence.Limited)]
    [TestCase(3, 1, false, false, false, GradingConfidence.Moderate)]
    [TestCase(2, 2, false, false, false, GradingConfidence.Corroborated)]
    public void Confidence_FollowsTheEvidenceCriteria(int responses, int voters, bool dissent, bool unresolved, bool allInsufficient, string expected) =>
        Assert.That(GradingConfidence.Categorize(responses, voters, dissent, unresolved, allInsufficient), Is.EqualTo(expected));

    [Test]
    public void Scores_RespectCriterionWeights()
    {
        var criteria = new List<GradingCriterion>
        {
            new("A", AssessmentDimension.FactualKnowledge, null, 3m, new Dictionary<MasteryLevel, string>()),
            new("B", AssessmentDimension.Application, null, 1m, new Dictionary<MasteryLevel, string>()),
        };
        var (scores, total, level) = AiObjectiveGrader.Scores(criteria, [MasteryLevel.TransferAndCriticalReasoning, MasteryLevel.Recognition], 100m);
        Assert.Multiple(() =>
        {
            Assert.That(scores, Is.EqualTo(new[] { 75m, 5m }));
            Assert.That(total, Is.EqualTo(80m));
            Assert.That(level, Is.EqualTo(MasteryLevel.Application));   // floor of (3×5 + 1×1)/4 = 4
        });
    }
}
