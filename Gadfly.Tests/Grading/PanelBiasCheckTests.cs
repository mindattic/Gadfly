using Gadfly.Assessment;
using Gadfly.Socrates;
using Gadfly.Sources.Grading;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Grading;

/// <summary>A panel that answers each FanOut call with the next scripted reply and records the prompts.</summary>
internal sealed class ScriptedGradingPanel(params string[] replies) : ISocraticVotingClient
{
    private int next;
    public List<string> Prompts { get; } = [];
    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096, double temperature = 0.2, CancellationToken ct = default)
    {
        Prompts.Add(userMessage);
        var reply = replies[Math.Min(next++, replies.Length - 1)];
        return Task.FromResult<IReadOnlyList<PanelReply>>([new PanelReply("a", reply)]);
    }
}

/// <summary>GAD-T269: grading-panel bias controls (prompt rules, order shuffling, padded and fluency probes).</summary>
[TestFixture]
public class PanelBiasCheckTests
{
    private static GradingObjective Objective() =>
        new("calvin", "The Calvin cycle", null, 100m, AiObjectiveGrader.DefaultCriteria(), "Calvin cycle", "Carbon fixation in the stroma.");

    private static List<EvidenceItem> Evidence() =>
    [
        new(Guid.NewGuid(), 1, "Where does the Calvin cycle happen?", "It happens in the stroma of the chloroplast.", VerificationState.Confirmed),
    ];

    private static string Vote(int level) =>
        $$"""
        {"criteria":[
          {"criterion":"Factual knowledge","evidence":[1],"quote":"","rationale":"r","level":{{level}}},
          {"criterion":"Conceptual understanding","evidence":[1],"quote":"","rationale":"r","level":{{level}}},
          {"criterion":"Logical reasoning","evidence":[1],"quote":"","rationale":"r","level":{{level}}},
          {"criterion":"Application","evidence":[1],"quote":"","rationale":"r","level":{{level}}}],
         "explanation":"e","misconceptions":[],"ambiguities":[]}
        """;

    [Test]
    public async Task Prompt_SaysLengthEarnsNothing_FluencyIsNotPenalized_AndEvidenceComesBeforeTheLevel()
    {
        var panel = new ScriptedGradingPanel(Vote(3));
        await new AiObjectiveGrader(panel, new Random(7)).GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        var prompt = panel.Prompts.Single();
        Assert.That(prompt, Does.Contain("Length, repetition and restatement earn nothing"));
        Assert.That(prompt, Does.Contain("Never penalize grammar, accent, fluency"));
        Assert.That(prompt, Does.Contain("random order"));
        Assert.That(prompt.IndexOf("\"quote\":", StringComparison.Ordinal), Is.LessThan(prompt.IndexOf("\"level\":", StringComparison.Ordinal)));
    }

    [Test]
    public async Task CriteriaAndLevelOrder_ChangesBetweenCalls_ButEveryCriterionIsStillGraded()
    {
        var panel = new ScriptedGradingPanel(Vote(3));
        var grader = new AiObjectiveGrader(panel, new Random(11));
        for (var i = 0; i < 12; i++)
        {
            var r = await grader.GradeAsync(new ObjectiveGradingInput(Objective(), Evidence()));
            Assert.That(r.Criteria, Has.Count.EqualTo(4));
            Assert.That(r.Criteria.Select(c => (int)c.Level), Is.All.EqualTo(3));
        }
        string FirstCriterion(string p) => p.Split('\n').First(l => l.TrimStart().StartsWith("- \"", StringComparison.Ordinal)).Trim();
        string FirstLevel(string p) => p.Split('\n').SkipWhile(l => !l.TrimStart().StartsWith("- \"", StringComparison.Ordinal)).Skip(1).First().Trim()[..1];
        Assert.That(panel.Prompts.Select(FirstCriterion).Distinct().Count(), Is.GreaterThan(1));
        Assert.That(panel.Prompts.Select(FirstLevel).Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public async Task APanelThatRewardsPaddingAndPunishesDisfluency_IsReportedBiased()
    {
        // Baseline, padded, less fluent: in that order.
        var panel = new ScriptedGradingPanel(Vote(3), Vote(5), Vote(1));
        var report = await new PanelBiasCheck(panel, new Random(3)).RunAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(report.AnyBias, Is.True);
        Assert.That(report.Probes.Single(p => p.Probe == PanelBiasCheck.Padded).Biased, Is.True);
        Assert.That(report.Probes.Single(p => p.Probe == PanelBiasCheck.Disfluent).Biased, Is.True);
        Assert.That(panel.Prompts, Has.Count.EqualTo(3));
        Assert.That(panel.Prompts[1], Does.Contain("restate"));
        Assert.That(panel.Prompts[2], Does.Contain("um,"));
    }

    [Test]
    public async Task APanelThatScoresContentOnly_IsNotReportedBiased()
    {
        var panel = new ScriptedGradingPanel(Vote(4));
        var report = await new PanelBiasCheck(panel).RunAsync(new ObjectiveGradingInput(Objective(), Evidence()));
        Assert.That(report.AnyBias, Is.False);
        Assert.That(report.Probes.Select(p => p.Shift), Is.All.EqualTo(0m));
    }

    [Test]
    public void Rewrites_KeepTheContentWords()
    {
        const string answer = "Without light there is no ATP or NADPH from the light reactions.";
        Assert.That(PanelBiasCheck.Pad(answer), Does.StartWith(answer));
        var disfluent = PanelBiasCheck.Disfluency(answer);
        foreach (var word in new[] { "without", "light", "atp", "nadph", "reactions" })
            Assert.That(disfluent, Does.Contain(word));
        Assert.That(disfluent, Does.Not.Contain(" the "));
        Assert.That(disfluent, Does.Not.EndWith("."));
    }
}
