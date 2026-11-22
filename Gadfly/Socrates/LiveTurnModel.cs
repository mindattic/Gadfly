using System.Runtime.CompilerServices;
using System.Text;
using Gadfly.Socrates.Mock;

namespace Gadfly.Socrates;

/// <summary>
/// The one fast model that words the examiner's next question on a live exam turn, and runs the leak guard on it (GAD-T494).
/// A spoken exam has to feel like a conversation: the multi-model panel vote (draft, judge synthesis, ballot, then a second
/// panel vote for the leak guard) cost about 7.5 s plus 4.1 s on production before the examiner could speak, so the live turn
/// asks a single configured model instead (<c>Gadfly:LiveTurn:Provider</c> / <c>Gadfly:LiveTurn:Model</c>, default Claude Sonnet 5,
/// never Opus). The panel still grades every answer; this departs from ElenchusAtScale's panel-worded questions for latency only
/// (docs/research/blueprint-gap-analysis.md).
/// </summary>
/// <remarks>
/// Streaming seam (for GAD-T495): <see cref="StreamAsync"/> yields the reply as text deltas. MindAttic.Legion has no streaming
/// call yet, so the Legion-backed model yields the whole reply as one delta; a streaming provider call slots in behind this
/// interface without touching the generator. The generator still guards the whole question before it is returned, so nothing
/// reaches the student before its leak check passes.
/// </remarks>
public interface ILiveTurnModel
{
    /// <summary>The provider family (e.g. "claude"), recorded as the question's author.</summary>
    string ProviderId { get; }

    /// <summary>The model id (e.g. "claude-sonnet-5").</summary>
    string Model { get; }

    /// <summary>The reply to <paramref name="userMessage"/>, as text deltas in order. Throws when the provider fails.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        CancellationToken ct = default);
}

public static class LiveTurnModelExtensions
{
    /// <summary>The whole reply: every delta of <see cref="ILiveTurnModel.StreamAsync"/>, joined.</summary>
    public static async Task<string> CompleteAsync(this ILiveTurnModel model, string systemPrompt, string userMessage, int maxTokens = 300,
        double temperature = 0.4, CancellationToken ct = default)
    {
        var text = new StringBuilder();
        await foreach (var delta in model.StreamAsync(systemPrompt, userMessage, maxTokens, temperature, ct).WithCancellation(ct))
            text.Append(delta);
        return text.ToString();
    }
}

/// <summary>Where the live-turn model is configured (GAD-T494).</summary>
public static class LiveTurnSettings
{
    public const string ProviderSetting = "Gadfly:LiveTurn:Provider";
    public const string ModelSetting = "Gadfly:LiveTurn:Model";
    public const string DefaultProvider = "claude";

    /// <summary>The configured provider, narrowed to the trusted panel members (an unknown one falls back to Claude).</summary>
    public static string Provider(string? configured) =>
        LegionTierResolver.TrustedProviderIds.FirstOrDefault(p => string.Equals(p, configured?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? DefaultProvider;

    /// <summary>
    /// The configured model; for Claude, Sonnet 5 unless configured, and never an Opus model (owner, 2026-11-22: the live turn
    /// must be fast and spend sparingly). Other providers default to the panel's model for them ("" lets Legion decide).
    /// </summary>
    public static string Model(string provider, string? configured, string? claudeModel = null)
    {
        var model = string.IsNullOrWhiteSpace(configured) ? "" : configured.Trim();
        if (provider != DefaultProvider) return model;
        if (model.Length == 0) model = string.IsNullOrWhiteSpace(claudeModel) ? GadflyConfiguration.DefaultClaudeModel : claudeModel.Trim();
        return model.Contains("opus", StringComparison.OrdinalIgnoreCase) ? GadflyConfiguration.DefaultClaudeModel : model;
    }
}

/// <summary>The live-turn model on a real provider through MindAttic.Legion (one provider, one call, metered and timed).</summary>
public sealed class LegionLiveTurnModel(LegionVotingClient legion, string providerId, string model) : ILiveTurnModel
{
    public string ProviderId { get; } = providerId;
    public string Model => model.Length > 0 ? model : legion.ConfiguredModels.GetValueOrDefault(ProviderId, "");

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // One delta until Legion can stream (GAD-T495 replaces this with the provider's own stream).
        yield return await legion.CallOneAsync(ProviderId, model, systemPrompt, userMessage, maxTokens, temperature, ct);
    }
}

/// <summary>The live-turn model in mock mode: the mock panel's own deterministic replies, so nothing is called or billed.</summary>
public sealed class MockLiveTurnModel : ILiveTurnModel
{
    public string ProviderId => MockVotingClient.ProviderId;
    public string Model => MockVotingClient.ModelName;

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return MockVotingClient.Complete(userMessage);
    }
}

/// <summary>The live-turn model the host injects: the mock's in mock mode, the configured provider's otherwise (decided once, like the panel).</summary>
public sealed class SelectedLiveTurnModel(SelectedPanel panel, ILiveTurnModel live, ILiveTurnModel mock) : ILiveTurnModel
{
    private ILiveTurnModel Current => panel.IsMock ? mock : live;
    public string ProviderId => Current.ProviderId;
    public string Model => Current.Model;

    public IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        CancellationToken ct = default) => Current.StreamAsync(systemPrompt, userMessage, maxTokens, temperature, ct);
}
