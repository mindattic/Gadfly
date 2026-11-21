using Gadfly.Prompts;
using Gadfly.Socrates;
using Gadfly.Tests.Fakes;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Socrates;

/// <summary>MasterExaminationRunner (Prompt 3) and ReflectionRunner (Prompt 2) driven by a recording fake panel (GAD-T4).</summary>
[TestFixture]
public class OneShotRunnerTests
{
    /// <summary>Records what the runners send and returns canned results; never touches a provider.</summary>
    private sealed class RecordingPanel : ISocraticVotingClient
    {
        public VotingResult ProfilesResult { get; set; } = new() { QuorumReached = true };
        public VotingResult AskResult { get; set; } = new() { QuorumReached = true };

        public List<(string Question, string Context, IReadOnlyList<VoterProfile> Voters)> ProfileCalls { get; } = new();
        public List<(string Question, string Context, Quorum Quorum)> AskCalls { get; } = new();

        public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
        {
            AskCalls.Add((question, context, quorum));
            return Task.FromResult(AskResult);
        }

        public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
        {
            ProfileCalls.Add((question, context, voters));
            return Task.FromResult(ProfilesResult);
        }

        public Task<DecisionResult> DecideAsync(string q, IReadOnlyList<string> o, string c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PanelReply>> FanOutAsync(string s, string u, int m = 4096, double t = 0.2, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static InterrogationTurn Turn(string speaker, string text) => new() { Speaker = speaker, Text = text };

    // ---- MasterExaminationRunner -------------------------------------------------------------

    [Test]
    public async Task Master_DefaultsToClaudeOpenAiGemini_ForSocratesConfuciusBuddha()
    {
        var panel = new RecordingPanel();

        await new MasterExaminationRunner(panel).RunAsync("digest");

        var voters = panel.ProfileCalls.Single().Voters;
        Assert.That(voters.Select(v => v.Name), Is.EqualTo(new[] { "Socrates", "Confucius", "Buddha" }));
        Assert.That(voters.Select(v => v.ProviderId), Is.EqualTo(new[] { "claude", "openai", "gemini" }));
    }

    [Test]
    public async Task Master_GivesEachVoterItsOwnPersona()
    {
        var panel = new RecordingPanel();

        await new MasterExaminationRunner(panel).RunAsync("digest");

        var voters = panel.ProfileCalls.Single().Voters;
        Assert.That(voters[0].PersonalityMarkdown, Is.EqualTo(Personas.Socrates));
        Assert.That(voters[1].PersonalityMarkdown, Is.EqualTo(Personas.Confucius));
        Assert.That(voters[2].PersonalityMarkdown, Is.EqualTo(Personas.Buddha));
    }

    [Test]
    public async Task Master_UsesAThreeItemOverride_InOrder()
    {
        var panel = new RecordingPanel();

        await new MasterExaminationRunner(panel).RunAsync("digest", new[] { "deepseek", "gemini", "openai" });

        Assert.That(panel.ProfileCalls.Single().Voters.Select(v => v.ProviderId), Is.EqualTo(new[] { "deepseek", "gemini", "openai" }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    public async Task Master_IgnoresAnOverrideThatIsNotExactlyThreeItems(int count)
    {
        var panel = new RecordingPanel();
        var wrongSize = new[] { "deepseek", "gemini", "openai", "claude" }.Take(count).ToArray();

        await new MasterExaminationRunner(panel).RunAsync("digest", wrongSize);

        Assert.That(panel.ProfileCalls.Single().Voters.Select(v => v.ProviderId), Is.EqualTo(new[] { "claude", "openai", "gemini" }));
    }

    [Test]
    public async Task Master_SendsTheMasterPrompt_WithTheDigestAsContext()
    {
        var panel = new RecordingPanel();

        await new MasterExaminationRunner(panel).RunAsync("the corpus digest");

        var call = panel.ProfileCalls.Single();
        Assert.That(call.Question, Is.EqualTo(CanonicalPrompts.MasterPrompt));
        Assert.That(call.Context, Is.EqualTo("the corpus digest"));
    }

    [Test]
    public async Task Master_ReturnsOnePerspectivePerVote_WithVoterNameAndProvider()
    {
        var panel = new RecordingPanel
        {
            ProfilesResult = new VotingResult
            {
                QuorumReached = true,
                IndividualVotes =
                [
                    new VoteResult { VoterName = "Socrates", ProviderId = "claude", Decision = "What do you know?" },
                    new VoteResult { VoterName = "Confucius", ProviderId = "openai", Decision = "Study the rites." },
                ],
            },
        };

        var result = await new MasterExaminationRunner(panel).RunAsync("digest");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Perspective, Is.EqualTo("Socrates"));
        Assert.That(result[0].ProviderId, Is.EqualTo("claude"));
        Assert.That(result[0].Text, Is.EqualTo("What do you know?"));
        Assert.That(result[1].Perspective, Is.EqualTo("Confucius"));
        Assert.That(result[1].Text, Is.EqualTo("Study the rites."));
    }

    [Test]
    public async Task Master_FallsBackFromDecisionToReasoning_WhenDecisionIsBlank()
    {
        var panel = new RecordingPanel
        {
            ProfilesResult = new VotingResult
            {
                QuorumReached = true,
                IndividualVotes =
                [
                    new VoteResult { VoterName = "Socrates", Decision = "", Reasoning = "from reasoning" },
                    new VoteResult { VoterName = "Confucius", Decision = "   ", Reasoning = "whitespace decision" },
                    new VoteResult { VoterName = "Buddha", Decision = "from decision", Reasoning = "unused" },
                ],
            },
        };

        var result = await new MasterExaminationRunner(panel).RunAsync("digest");

        Assert.That(result.Select(r => r.Text), Is.EqualTo(new[] { "from reasoning", "whitespace decision", "from decision" }));
    }

    [Test]
    public async Task Master_ReturnsNothing_WhenThePanelReturnsNoVotes()
    {
        var result = await new MasterExaminationRunner(new RecordingPanel()).RunAsync("digest");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task Master_WorksWithTheSharedFakeClient_ConsumingOneResponsePerVoter()
    {
        var fake = new FakeSocraticVotingClient(["one", "two", "three"]);

        var result = await new MasterExaminationRunner(fake).RunAsync("digest");

        Assert.That(result.Select(r => r.Text), Is.EqualTo(new[] { "one", "two", "three" }));
        Assert.That(result.Select(r => r.Perspective), Is.EqualTo(new[] { "Socrates", "Confucius", "Buddha" }));
    }

    // ---- ReflectionRunner --------------------------------------------------------------------

    [Test]
    public async Task Reflection_SendsTheFinalReflectionPrompt_WithPluralityQuorum()
    {
        var panel = new RecordingPanel { AskResult = new VotingResult { QuorumReached = true, Consensus = "reflection" } };

        await new ReflectionRunner(panel).RunAsync("digest");

        var call = panel.AskCalls.Single();
        Assert.That(call.Question, Is.EqualTo(CanonicalPrompts.FinalReflection));
        Assert.That(call.Quorum, Is.EqualTo(Quorum.Plurality));
    }

    [Test]
    public async Task Reflection_AppendsNoTranscript_WhenThereAreNoPriorTurns()
    {
        var panel = new RecordingPanel { AskResult = new VotingResult { QuorumReached = true, Consensus = "x" } };

        await new ReflectionRunner(panel).RunAsync("digest");
        await new ReflectionRunner(panel).RunAsync("digest", null);
        await new ReflectionRunner(panel).RunAsync("digest", Array.Empty<InterrogationTurn>());

        Assert.That(panel.AskCalls.Select(c => c.Context), Is.All.EqualTo("digest"));
    }

    [Test]
    public async Task Reflection_AppendsTheInterrogationTranscript_WhenThereArePriorTurns()
    {
        var panel = new RecordingPanel { AskResult = new VotingResult { QuorumReached = true, Consensus = "x" } };

        await new ReflectionRunner(panel).RunAsync("digest",
            [Turn("Socrates", "What is courage?"), Turn("You", "Facing fear."), Turn("Socrates", "Is it always good?")]);

        Assert.That(panel.AskCalls.Single().Context, Is.EqualTo(
            "digest\n\nINTERROGATION TRANSCRIPT:\nSocrates: What is courage?\nYou: Facing fear.\nSocrates: Is it always good?"));
    }

    [Test]
    public async Task Reflection_ReturnsTheConsensus_WhenQuorumIsReached()
    {
        var panel = new RecordingPanel
        {
            AskResult = new VotingResult { QuorumReached = true, Consensus = "the consensus", NarrativeSummary = "the narrative" },
        };

        var text = await new ReflectionRunner(panel).RunAsync("digest");

        Assert.That(text, Is.EqualTo("the consensus"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Reflection_FallsBackFromConsensusToNarrativeSummary_WhenConsensusIsBlank(string consensus)
    {
        var panel = new RecordingPanel
        {
            AskResult = new VotingResult { QuorumReached = true, Consensus = consensus, NarrativeSummary = "the narrative" },
        };

        var text = await new ReflectionRunner(panel).RunAsync("digest");

        Assert.That(text, Is.EqualTo("the narrative"));
    }

    [Test]
    public async Task Reflection_FallsBackToNarrativeSummary_WhenQuorumIsNotReached()
    {
        var panel = new RecordingPanel
        {
            AskResult = new VotingResult { QuorumReached = false, Consensus = "All voters failed.", NarrativeSummary = "the narrative" },
        };

        var text = await new ReflectionRunner(panel).RunAsync("digest");

        Assert.That(text, Is.EqualTo("the narrative"));
    }

    [Test]
    public async Task Reflection_NeverReturnsPanelFailureWording_AsTheReflection()
    {
        var panel = new RecordingPanel { AskResult = new VotingResult { QuorumReached = false, Consensus = "All voters failed." } };

        var text = await new ReflectionRunner(panel).RunAsync("digest");

        Assert.That(text, Does.Not.Contain("All voters failed"));
        Assert.That(text, Does.Contain("no usable reflection"));
    }

    [Test]
    public async Task Reflection_WorksWithTheSharedFakeClient()
    {
        var fake = new FakeSocraticVotingClient(["a reflection"]);

        var text = await new ReflectionRunner(fake).RunAsync("digest");

        Assert.That(text, Is.EqualTo("a reflection"));
        Assert.That(fake.Calls.Single().Question, Is.EqualTo(CanonicalPrompts.FinalReflection));
    }
}
