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
/// Streaming (GAD-T495): <see cref="StreamAsync"/> yields the reply as text deltas. On Claude the reply really streams
/// (<see cref="ClaudeStreamingLiveTurnModel"/>, the Messages API directly); MindAttic.Legion has no streaming call, so other providers
/// yield the whole reply as one delta. With a voice waiting (<see cref="Gadfly.Engine.QuestionSpeech"/>) the generator splits the reply
/// into sentences and guards each part, with everything before it and the dialogue, before that part is spoken; nothing reaches the
/// student before its leak check passes.
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
    /// <summary>"false" keeps Claude's live turn on the one-shot Legion call instead of the streamed reply (GAD-T495).</summary>
    public const string StreamSetting = "Gadfly:LiveTurn:Stream";
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
        // One delta: Legion has no streaming call (Claude streams through ClaudeStreamingLiveTurnModel instead, GAD-T495).
        yield return await legion.CallOneAsync(ProviderId, model, systemPrompt, userMessage, maxTokens, temperature, ct);
    }
}

/// <summary>
/// The live-turn model on Claude with the reply streamed as it is written (GAD-T495): the Anthropic Messages API with
/// <c>stream: true</c>, read as server-sent events, each <c>text_delta</c> yielded at once, so the examiner can start saying the
/// first sentence of a question while the rest is being written. MindAttic.Legion has no streaming call, so this one call goes to
/// the API directly, with the same key Legion uses (MindAttic Vault's shared credentials), the same model rules (no
/// <c>temperature</c> on the Claude 5 line, thinking turned off on Sonnet/Haiku 5) and the same metering (kind <c>live-turn</c>, the
/// provider's own token counts) and timing. Any failure throws, so the generator falls back to the panel as before.
/// </summary>
public sealed class ClaudeStreamingLiveTurnModel(LegionVotingClient legion, string model, Func<string?>? apiKey = null,
    HttpMessageHandler? handler = null) : ILiveTurnModel
{
    public const string Endpoint = "https://api.anthropic.com/v1/messages";
    public const string ApiVersion = "2023-06-01";

    /// <summary>How long a streamed reply may take before it counts as failed (the panel's own provider timeout).</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(25);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly HttpClient _http = handler is null ? SharedHttp : new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly Func<string?> _apiKey = apiKey ?? (() => MindAttic.Legion.MindAtticCredentialStore.GetKey("claude"));

    public string ProviderId => "claude";
    public string Model { get; } = string.IsNullOrWhiteSpace(model) ? GadflyConfiguration.DefaultClaudeModel : model;

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        var reply = new StringBuilder();
        long inputTokens = 0, outputTokens = 0;
        var ok = false;
        HttpResponseMessage? response = null;
        StreamReader? reader = null;
        try
        {
            try
            {
                var key = _apiKey();
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No API key configured for provider 'claude'.");
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(Body(systemPrompt, userMessage, maxTokens), Encoding.UTF8, "application/json") };
                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", ApiVersion);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    throw new HttpRequestException($"Claude stream failed: {(int)response.StatusCode} {(body.Length > 300 ? body[..300] : body)}", null, response.StatusCode);
                }
                reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Failed(ex, ct, startedAt, watch);
                throw;
            }
            while (true)
            {
                SseEvent next;
                try
                {
                    next = await ReadEventAsync(reader, timeout.Token);
                }
                catch (Exception ex)
                {
                    Failed(ex, ct, startedAt, watch);
                    throw;
                }
                if (next.End) break;
                inputTokens = Math.Max(inputTokens, next.InputTokens);
                outputTokens = Math.Max(outputTokens, next.OutputTokens);
                if (next.Text is { Length: > 0 } text)
                {
                    reply.Append(text);
                    yield return text;
                }
            }
            ok = true;
            PanelTimings.Report(new ProviderCallTiming(ProviderId, startedAt, watch.ElapsedMilliseconds, true, null));
            legion.Health.RecordSuccess(ProviderId, DateTimeOffset.UtcNow);
        }
        finally
        {
            reader?.Dispose();
            response?.Dispose();
            // Metered whether or not it finished (a reply the generator stopped early is still billed for what was written).
            if (ok || reply.Length > 0)
                PanelUsage.Record(inputTokens > 0
                    ? new PanelCall(ProviderId, Model, inputTokens, outputTokens, "live-turn", Estimated: false)
                    : new PanelCall(ProviderId, Model, PanelUsage.Tokens(systemPrompt.Length + userMessage.Length), PanelUsage.Tokens(reply.Length), "live-turn"));
        }
    }

    private void Failed(Exception ex, CancellationToken ct, DateTimeOffset startedAt, System.Diagnostics.Stopwatch watch)
    {
        if (ex is OperationCanceledException && ct.IsCancellationRequested)
        {
            PanelTimings.Report(new ProviderCallTiming(ProviderId, startedAt, watch.ElapsedMilliseconds, false, "cancelled"));
            return;
        }
        PanelTimings.Report(new ProviderCallTiming(ProviderId, startedAt, watch.ElapsedMilliseconds, false, ex.GetType().Name + ": " + ex.Message));
        legion.Health.RecordFailure(ProviderId, ex.Message, DateTimeOffset.UtcNow);
    }

    /// <summary>The request body: one user turn, streamed; no temperature on the Claude 5 line, thinking off where it defaults on.</summary>
    internal string Body(string systemPrompt, string userMessage, int maxTokens)
    {
        var body = new System.Text.Json.Nodes.JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            ["messages"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["role"] = "user", ["content"] = userMessage }),
        };
        if (!string.IsNullOrWhiteSpace(systemPrompt)) body["system"] = systemPrompt;
        if (ThinkingOff(Model) is { } off) body["thinking"] = new System.Text.Json.Nodes.JsonObject { ["type"] = off };
        return body.ToJsonString();
    }

    /// <summary>
    /// How to turn thinking off for <paramref name="model"/>, as MindAttic.Legion does: Sonnet and Haiku 5 run adaptive thinking unless
    /// told "disabled" (the 5.5 line wants "between_tools" instead); other models need nothing (null). Temperature is never sent: the
    /// Claude 5 line rejects it, and the default suits a one-question reply and a one-word guard alike.
    /// </summary>
    internal static string? ThinkingOff(string model)
    {
        foreach (var prefix in new[] { "claude-sonnet-", "claude-haiku-" })
        {
            if (!model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = model[prefix.Length..];
            var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var major) || major < 5) return null;
            var after = rest[digits.Length..];
            // "claude-sonnet-5" (bare) and dated snapshots take "disabled"; a minor version ("-5-5") takes "between_tools".
            return after.Length > 1 && after[0] == '-' && char.IsDigit(after[1]) && after.Skip(1).TakeWhile(char.IsDigit).Count() <= 2
                ? "between_tools" : "disabled";
        }
        return null;
    }

    /// <summary>One server-sent event's worth: text, token counts, or the end of the message.</summary>
    internal readonly record struct SseEvent(string? Text, long InputTokens, long OutputTokens, bool End);

    /// <summary>Reads lines until an event with something in it (text, usage) or the end; an <c>error</c> event throws.</summary>
    internal static async Task<SseEvent> ReadEventAsync(TextReader reader, CancellationToken ct)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return new SseEvent(null, 0, 0, true);
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "content_block_delta"
                    when root.TryGetProperty("delta", out var delta) && delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta":
                    return new SseEvent(delta.GetProperty("text").GetString(), 0, 0, false);
                case "message_start" when root.TryGetProperty("message", out var m) && m.TryGetProperty("usage", out var u):
                    return new SseEvent(null, InputOf(u), Number(u, "output_tokens"), false);
                case "message_delta" when root.TryGetProperty("usage", out var u2):
                    return new SseEvent(null, InputOf(u2), Number(u2, "output_tokens"), false);
                case "message_stop":
                    return new SseEvent(null, 0, 0, true);
                case "error":
                    throw new HttpRequestException($"Claude stream error: {(root.TryGetProperty("error", out var err) ? err.ToString() : data)}");
            }
        }
    }

    private static long InputOf(System.Text.Json.JsonElement usage) =>
        Number(usage, "input_tokens") + Number(usage, "cache_read_input_tokens") + Number(usage, "cache_creation_input_tokens");

    private static long Number(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}

/// <summary>
/// The live-turn model in mock mode: the mock panel's own deterministic replies, so nothing is called or billed. The reply comes a word
/// at a time, as a streaming provider sends it, so mock mode runs the same spoken-as-written path as live (GAD-T495).
/// </summary>
public sealed class MockLiveTurnModel : ILiveTurnModel
{
    public string ProviderId => MockVotingClient.ProviderId;
    public string Model => MockVotingClient.ModelName;

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        var reply = MockVotingClient.Complete(userMessage);
        var start = 0;
        for (var i = 1; i <= reply.Length; i++)
        {
            if (i < reply.Length && reply[i] != ' ') continue;
            ct.ThrowIfCancellationRequested();
            yield return reply[start..i];
            start = i;
        }
    }
}

/// <summary>
/// The live-turn model the host injects: the mock's in mock mode, the configured provider's otherwise (decided once, like the
/// panel). It follows whatever <see cref="IPanelInfo"/> the host registered (the <see cref="SelectedPanel"/> in the server, a
/// fixed mock in tests), and the live model is built only when first used in live mode, so a mock host never constructs the
/// live LLM client (GAD-T517).
/// </summary>
public sealed class SelectedLiveTurnModel(IPanelInfo panel, Func<ILiveTurnModel> live, ILiveTurnModel mock) : ILiveTurnModel
{
    private readonly Lazy<ILiveTurnModel> _live = new(live);
    private ILiveTurnModel Current => panel.IsMock ? mock : _live.Value;
    public string ProviderId => Current.ProviderId;
    public string Model => Current.Model;

    public IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userMessage, int maxTokens = 300, double temperature = 0.4,
        CancellationToken ct = default) => Current.StreamAsync(systemPrompt, userMessage, maxTokens, temperature, ct);
}
