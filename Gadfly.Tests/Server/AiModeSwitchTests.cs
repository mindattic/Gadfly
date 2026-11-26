using Gadfly.Socrates;
using Gadfly.Socrates.Mock;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The runtime switch between mock and live (GAD-T802): the server-wide mode flips without a restart, and a session pinned to the
/// mode it started in keeps it. No provider is ever called: the live client is built but only mock or recording panels answer.
/// </summary>
[TestFixture]
public class AiModeSwitchTests
{
    /// <summary>Records the pin and the selected panel's mode as seen during a call, and answers nothing.</summary>
    private sealed class ProbePanel(IPanelInfo? watched = null) : ISocraticVotingClient
    {
        public bool? PinSeen { get; private set; }
        public bool? WatchedMockSeen { get; private set; }

        private T Seen<T>(T result)
        {
            PinSeen = PanelPin.Mock;
            WatchedMockSeen = watched?.IsMock;
            return result;
        }

        public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
            Task.FromResult(Seen(new VotingResult()));
        public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) =>
            Task.FromResult(Seen(new VotingResult()));
        public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) =>
            Task.FromResult(Seen(new DecisionResult()));
        public async Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096, double temperature = 0.2,
            CancellationToken ct = default)
        {
            await Task.Yield();
            return Seen<IReadOnlyList<PanelReply>>([]);
        }
    }

    private static SelectedPanel Mocked() =>
        new(PanelModes.Mock, new LegionVotingClient(new VotingConfiguration()), new MockVotingClient());

    [Test]
    public void Switch_changes_the_server_mode_without_a_restart()
    {
        var panel = Mocked();
        Assert.That(panel.IsMock, Is.True);

        Assert.That(panel.Switch(false, "switched by the admin"), Is.True);
        Assert.That(panel.IsMock, Is.False);
        Assert.That(panel.ServerIsMock, Is.False);
        Assert.That(panel.Mode, Is.EqualTo(PanelModes.Live));
        Assert.That(panel.Reason, Is.EqualTo("switched by the admin"));

        Assert.That(panel.Switch(false, "again"), Is.False, "switching to the mode in force changes nothing");
        Assert.That(panel.Switch(true, "back"), Is.True);
        Assert.That(panel.IsMock, Is.True);
    }

    [TestCase(PanelModes.Mock, true)]
    [TestCase(PanelModes.Live, false)]
    public async Task A_pinned_panel_holds_each_call_to_the_session_mode_and_restores_it(string mode, bool mock)
    {
        var probe = new ProbePanel();
        var pinned = PanelPin.For(probe, mode);

        await pinned.FanOutAsync("s", "u");
        await pinned.AskAsync("q", "c", Quorum.Plurality);

        Assert.That(probe.PinSeen, Is.EqualTo(mock));
        Assert.That(PanelPin.Mock, Is.Null, "the pin ends with the call");
        Assert.That(pinned, Is.InstanceOf<IPanelInfo>());
        Assert.That(((IPanelInfo)pinned).IsMock, Is.EqualTo(mock));
    }

    [Test]
    public void A_session_without_a_recorded_mode_follows_the_server()
    {
        var probe = new ProbePanel();
        Assert.That(PanelPin.For(probe, null), Is.SameAs(probe));
        Assert.That(PanelPin.For((ILiveTurnModel?)null, PanelModes.Mock), Is.Null);
    }

    [Test]
    public async Task A_mock_session_stays_mock_after_the_server_is_switched_live()
    {
        var server = Mocked();
        server.Switch(false, "the admin went live");
        var probe = new ProbePanel(watched: server);

        await PanelPin.For(probe, PanelModes.Mock).FanOutAsync("s", "u");

        Assert.That(probe.WatchedMockSeen, Is.True, "inside the session's call the selected panel is still mock");
        Assert.That(server.IsMock, Is.False, "outside it the server is live");
    }

    [Test]
    public async Task A_live_session_stays_live_after_the_server_is_switched_to_mock()
    {
        var server = Mocked();
        var probe = new ProbePanel(watched: server);

        await PanelPin.For(probe, PanelModes.Live).DecideAsync("q", ["a", "b"], "c");

        Assert.That(probe.WatchedMockSeen, Is.False);
        Assert.That(server.IsMock, Is.True);
    }

    [Test]
    public async Task A_mock_session_keeps_the_mock_live_turn_model_after_the_server_is_switched_live()
    {
        var server = Mocked();
        server.Switch(false, "the admin went live");
        var model = new SelectedLiveTurnModel(server, () => throw new InvalidOperationException("the live model must not be built"), new MockLiveTurnModel());

        var text = await PanelPin.For(model, PanelModes.Mock)!.CompleteAsync("system", "Ask the next question.");

        Assert.That(text, Is.Not.Empty);
        Assert.That(PanelPin.Mock, Is.Null);
    }

    [TestCase("live", false)]
    [TestCase("mock", true)]
    [TestCase(null, null)]
    [TestCase("auto", null)]
    public void Recorded_modes_parse(string? mode, bool? mock) =>
        Assert.That(PanelPin.IsMockMode(mode), Is.EqualTo(mock));
}
