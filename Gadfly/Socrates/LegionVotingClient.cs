using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;

namespace Gadfly.Socrates;

/// <summary>Real <see cref="ISocraticVotingClient"/> backed by a live <see cref="LlmVotingService"/>.</summary>
public class LegionVotingClient : ISocraticVotingClient, IPanelModels, IDisposable
{
    private readonly HttpClient http;
    private readonly VotingConfiguration config;
    private readonly LlmVotingService service;
    private readonly LlmVotingProvider provider;

    /// <summary>Per-provider failure tracking for the raw fan-out calls (GAD-T62).</summary>
    public ProviderHealth Health { get; } = new();

    public LegionVotingClient(VotingConfiguration config)
    {
        this.config = config;
        http = new HttpClient { Timeout = config.ProviderTimeout };
        provider = new LlmVotingProvider(http, config);
        service = new LlmVotingService(provider, config, NullLogger<LlmVotingService>.Instance);
    }

    public async Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default)
    {
        using var real = PanelUsage.CaptureReal("vote");
        var result = await service.VoteAsync(new VoteRequest { Question = question, Context = context, SynthesizeNarrative = true }, quorum, ct);
        if (real.Count == 0) RecordVote(result, question.Length + context.Length, "vote");
        return result;
    }

    public async Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default)
    {
        using var real = PanelUsage.CaptureReal("vote");
        var result = await service.VoteWithProfilesAsync(
            new VoteRequest { Question = question, Context = context, SynthesizeNarrative = false },
            Quorum.Plurality,
            voters,
            ct);
        if (real.Count == 0) RecordVote(result, question.Length + context.Length, "vote");
        return result;
    }

    public async Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default)
    {
        using var real = PanelUsage.CaptureReal("decide");
        var result = await service.DecideAsync(question, options, context, Quorum.Plurality, ct: ct);
        if (real.Count == 0)
        foreach (var v in result.IndividualVotes.Where(v => !v.IsError))
            PanelUsage.Record(new PanelCall(v.ProviderId, ModelOf(v.ProviderId),
                PanelUsage.Tokens(question.Length + context.Length + options.Sum(o => o.Length)), PanelUsage.Tokens(v.Decision.Length + v.Reasoning.Length), "decide"));
        return result;
    }

    /// <summary>Meters a Legion vote: one call per voter that answered, plus the judge's synthesis (estimated sizes).</summary>
    private void RecordVote(VotingResult result, int promptChars, string kind)
    {
        foreach (var v in result.IndividualVotes.Where(v => !v.IsError))
            PanelUsage.Record(new PanelCall(v.ProviderId, ModelOf(v.ProviderId),
                PanelUsage.Tokens(promptChars), PanelUsage.Tokens(v.Decision.Length + v.Reasoning.Length), kind));
        if (!string.IsNullOrEmpty(result.NarrativeSummary) && config.JudgeProviderId is { Length: > 0 } judge)
            PanelUsage.Record(new PanelCall(judge, ModelOf(judge),
                PanelUsage.Tokens(promptChars + result.IndividualVotes.Sum(v => v.Decision.Length)), PanelUsage.Tokens(result.NarrativeSummary.Length), kind + "-judge"));
    }

    private string ModelOf(string id) =>
        config.ModelOverrides.GetValueOrDefault(id) ?? LlmProviderRuntimeConfigurationResolver.GetModel(id) ?? LegionClient.DefaultModels.GetValueOrDefault(id, "");

    /// <summary>
    /// The model each member would run (GAD-T429): the providers with a key, or every allowed provider when none has one yet.
    /// Read from configuration only; no provider is called.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfiguredModels
    {
        get
        {
            var ids = config.ActiveProviderIds;
            if (ids.Count == 0) ids = [.. config.AllowedProviderIds];
            return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(id => id, ModelOf, StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096,
        double temperature = 0.2, CancellationToken ct = default)
    {
        var ids = config.ActiveProviderIds.ToList();
        if (ids.Count == 0) return [];
        var calls = ids.Select(async id =>
        {
            var model = ModelOf(id);
            var startedAt = DateTimeOffset.UtcNow;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var real = PanelUsage.CaptureReal("fan-out");
                var text = await provider.CallAsync(id, systemPrompt, userMessage, maxTokens, temperature, null, ct);
                PanelTimings.Report(new ProviderCallTiming(id, startedAt, watch.ElapsedMilliseconds, true, null));   // GAD-T413
                Health.RecordSuccess(id, DateTimeOffset.UtcNow);
                if (real.Count == 0)
                    PanelUsage.Record(new PanelCall(id, model, PanelUsage.Tokens(systemPrompt.Length + userMessage.Length), PanelUsage.Tokens((text ?? "").Length), "fan-out"));
                return new PanelReply(id, text ?? "", Model: model);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                PanelTimings.Report(new ProviderCallTiming(id, startedAt, watch.ElapsedMilliseconds, false, "cancelled"));
                throw;
            }
            catch (Exception ex)
            {
                PanelTimings.Report(new ProviderCallTiming(id, startedAt, watch.ElapsedMilliseconds, false, ex.GetType().Name + ": " + ex.Message));
                Health.RecordFailure(id, ex.Message, DateTimeOffset.UtcNow);
                return new PanelReply(id, "", ex.Message, model);
            }
        });
        return await Task.WhenAll(calls);
    }

    /// <summary>
    /// One call to one provider (GAD-T494, the live turn's single fast model): <paramref name="model"/> when given, else the panel's
    /// model for that provider. Metered (kind "live-turn") and timed like a fan-out member; failures are recorded in
    /// <see cref="Health"/> and rethrown so the caller can fall back to the panel.
    /// </summary>
    public async Task<string> CallOneAsync(string providerId, string model, string systemPrompt, string userMessage, int maxTokens,
        double temperature, CancellationToken ct = default)
    {
        var used = string.IsNullOrWhiteSpace(model) ? ModelOf(providerId) : model;
        var startedAt = DateTimeOffset.UtcNow;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var real = PanelUsage.CaptureReal("live-turn");
            var text = await provider.CallAsync(providerId, systemPrompt, userMessage, maxTokens, temperature,
                string.IsNullOrWhiteSpace(model) ? null : new VoterProfile { ProviderId = providerId, ModelOverride = model }, ct);
            PanelTimings.Report(new ProviderCallTiming(providerId, startedAt, watch.ElapsedMilliseconds, true, null));
            Health.RecordSuccess(providerId, DateTimeOffset.UtcNow);
            if (real.Count == 0)
                PanelUsage.Record(new PanelCall(providerId, used, PanelUsage.Tokens(systemPrompt.Length + userMessage.Length), PanelUsage.Tokens((text ?? "").Length), "live-turn"));
            return text ?? "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            PanelTimings.Report(new ProviderCallTiming(providerId, startedAt, watch.ElapsedMilliseconds, false, "cancelled"));
            throw;
        }
        catch (Exception ex)
        {
            PanelTimings.Report(new ProviderCallTiming(providerId, startedAt, watch.ElapsedMilliseconds, false, ex.GetType().Name + ": " + ex.Message));
            Health.RecordFailure(providerId, ex.Message, DateTimeOffset.UtcNow);
            throw;
        }
    }

    public void Dispose() => http.Dispose();
}
