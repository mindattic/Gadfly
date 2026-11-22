using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindAttic.Legion;

namespace Gadfly.Socrates.Mock;

/// <summary>
/// The panel the host injects as <see cref="ISocraticVotingClient"/> (GAD-T100): live Legion, the mock, or — in
/// auto mode — whichever a one-time startup probe allows (live when at least one provider answers, otherwise
/// mock). The choice is made once per process and never flips mid-run, so a session is never half live, half mock.
/// </summary>
public sealed class SelectedPanel : ISocraticVotingClient, IPanelInfo, IPanelModels
{
    private readonly LegionVotingClient _live;
    private readonly MockVotingClient _mock;
    private readonly Task<(bool Mock, string Reason)> _decision;

    public SelectedPanel(string configuredMode, LegionVotingClient live, MockVotingClient mock, ILogger? log = null)
    {
        _live = live;
        _mock = mock;
        ConfiguredMode = configuredMode;
        _decision = configuredMode switch
        {
            PanelModes.Mock => Task.FromResult((true, $"{PanelModes.Setting} is \"mock\".")),
            PanelModes.Live => Task.FromResult((false, $"{PanelModes.Setting} is \"live\".")),
            _ => Task.Run(() => ProbeAsync(live)),
        };
        _decision.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                if (t.Result.Mock)
                    log?.LogWarning("Gadfly AI panel: MOCK mode. {Reason} Every AI output is simulated and labelled; none is a genuine AI evaluation.", t.Result.Reason);
                else
                    log?.LogInformation("Gadfly AI panel: live providers. {Reason}", t.Result.Reason);
            }
        }, TaskScheduler.Default);
    }

    /// <summary>The configured <c>Gadfly:AiMode</c> (live, mock or auto).</summary>
    public string ConfiguredMode { get; }

    /// <summary>How long the auto-mode probe waits for any provider.</summary>
    public static TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Blocks only until the startup probe finishes (auto mode); constant afterwards.
    private (bool Mock, string Reason) Decision => _decision.GetAwaiter().GetResult();

    public bool IsMock => Decision.Mock;
    public string Mode => IsMock ? PanelModes.Mock : PanelModes.Live;
    public string Reason => Decision.Reason;

    /// <summary>The models of the panel actually in use: the mock's in mock mode, the live members' otherwise (GAD-T429).</summary>
    public IReadOnlyDictionary<string, string> ConfiguredModels => IsMock ? _mock.ConfiguredModels : _live.ConfiguredModels;

    private async Task<ISocraticVotingClient> PanelAsync() => (await _decision.ConfigureAwait(false)).Mock ? _mock : _live;

    public async Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
        await (await PanelAsync()).AskAsync(question, context, quorum, ct);

    public async Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) =>
        await (await PanelAsync()).AskWithProfilesAsync(question, context, voters, ct);

    public async Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) =>
        await (await PanelAsync()).DecideAsync(question, options, context, ct);

    public async Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default) =>
        await (await PanelAsync()).FanOutAsync(systemPrompt, userMessage, maxTokens, temperature, ct);

    /// <summary>One tiny call to every configured provider; any usable reply means live.</summary>
    private static async Task<(bool Mock, string Reason)> ProbeAsync(LegionVotingClient live)
    {
        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            var replies = await live.FanOutAsync("Reply with the single word OK.", "OK?", maxTokens: 5, temperature: 0, ct: cts.Token);
            var working = replies.Where(r => !r.IsError && !string.IsNullOrWhiteSpace(r.Text)).Select(r => r.ProviderId).ToList();
            if (working.Count > 0)
                return (false, $"{PanelModes.Setting} is \"auto\" and {string.Join(", ", working)} answered the startup probe.");
            return replies.Count == 0
                ? (true, $"{PanelModes.Setting} is \"auto\" and no LLM provider key is configured.")
                : (true, $"{PanelModes.Setting} is \"auto\" and no provider answered the startup probe ({string.Join("; ", replies.Select(r => $"{r.ProviderId}: {r.Error ?? "empty reply"}"))}).");
        }
        catch (Exception ex)
        {
            return (true, $"{PanelModes.Setting} is \"auto\" and the provider probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public static class PanelServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI panel (GAD-T100): <see cref="LegionVotingClient"/> (always, for provider health), the
    /// <see cref="MockVotingClient"/>, and <see cref="SelectedPanel"/> as both <see cref="ISocraticVotingClient"/> and
    /// <see cref="IPanelInfo"/>, chosen by <c>Gadfly:AiMode</c> ("live" | "mock" | "auto"). Unset means "mock" in
    /// development (no provider credit is spent unless someone opts in: owner, 2026-11-21) and "live" elsewhere;
    /// production sets it explicitly. Claude, when live, runs on <c>Gadfly:ClaudeModel</c> (default Sonnet 5).
    /// </summary>
    public static IServiceCollection AddGadflyPanel(this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        var mode = PanelModes.Parse(configuration[PanelModes.Setting], isDevelopment ? PanelModes.Mock : PanelModes.Live);
        var claudeModel = configuration["Gadfly:ClaudeModel"];
        services.AddSingleton<LegionVotingClient>(sp =>
        {
            var client = new LegionVotingClient(GadflyConfiguration.Build(claudeModel: claudeModel));
            var log = sp.GetService<ILoggerFactory>()?.CreateLogger("Gadfly.Panel");
            client.Health.BecameFailing += s => log?.LogError(
                "LLM provider {Provider} has failed {Count} calls in a row; the panel is running without it. Last error: {Error}",
                s.ProviderId, s.ConsecutiveFailures, s.LastError);
            return client;
        });
        services.AddSingleton<MockVotingClient>();
        services.AddSingleton<SelectedPanel>(sp => new SelectedPanel(mode, sp.GetRequiredService<LegionVotingClient>(),
            sp.GetRequiredService<MockVotingClient>(), sp.GetService<ILoggerFactory>()?.CreateLogger("Gadfly.Panel")));
        services.AddSingleton<ISocraticVotingClient>(sp => sp.GetRequiredService<SelectedPanel>());
        services.AddSingleton<IPanelInfo>(sp => sp.GetRequiredService<SelectedPanel>());
        // The live turn's one fast model (GAD-T494): the next question and its leak guard, off the multi-model panel.
        var liveProvider = LiveTurnSettings.Provider(configuration[LiveTurnSettings.ProviderSetting]);
        var liveModel = LiveTurnSettings.Model(liveProvider, configuration[LiveTurnSettings.ModelSetting], claudeModel);
        // On Claude the reply streams (GAD-T495), so the examiner can start speaking the question while it is written;
        // Gadfly:LiveTurn:Stream=false keeps the one-shot Legion call.
        var stream = liveProvider == LiveTurnSettings.DefaultProvider && !string.Equals(configuration[LiveTurnSettings.StreamSetting], "false", StringComparison.OrdinalIgnoreCase);
        services.AddSingleton<ILiveTurnModel>(sp => new SelectedLiveTurnModel(sp.GetRequiredService<SelectedPanel>(),
            stream ? new ClaudeStreamingLiveTurnModel(sp.GetRequiredService<LegionVotingClient>(), liveModel)
                : new LegionLiveTurnModel(sp.GetRequiredService<LegionVotingClient>(), liveProvider, liveModel), new MockLiveTurnModel()));
        return services;
    }
}
