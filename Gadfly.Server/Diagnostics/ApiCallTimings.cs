using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;

namespace Gadfly.Server.Diagnostics;

/// <summary>Which live session (and turn) an outside call belongs to, carried ambiently through a session step (GAD-T413).</summary>
public sealed class ApiCallContext(Guid? organizationId, Guid? sessionId)
{
    public Guid? OrganizationId { get; } = organizationId;
    public Guid? SessionId { get; } = sessionId;
    /// <summary>The turn open when the step began; set once the session's engine is loaded.</summary>
    public int? TurnIndex { get; set; }
}

/// <summary>
/// The ambient call context (GAD-T413). <c>using var timing = ApiCallScope.Begin(org, session);</c> at the top of a session step
/// tags every outside call made beneath it; the voice hub filter does the same for the examiner's voice and the recognizer.
/// </summary>
public static class ApiCallScope
{
    private static readonly AsyncLocal<ApiCallContext?> CurrentContext = new();
    private static readonly AsyncLocal<string?> CurrentName = new();

    public static ApiCallContext? Current => CurrentContext.Value;

    /// <summary>The panel call in progress (e.g. panel.judge), so a provider's own timing can be labelled with it.</summary>
    public static string? CallName => CurrentName.Value;

    public static Scope Begin(Guid? organizationId, Guid? sessionId, int? turnIndex = null)
    {
        var previous = CurrentContext.Value;
        var context = new ApiCallContext(organizationId, sessionId) { TurnIndex = turnIndex };
        CurrentContext.Value = context;
        return new Scope(context, previous);
    }

    internal static void SetName(string? name) => CurrentName.Value = name;

    public sealed class Scope(ApiCallContext context, ApiCallContext? previous) : IDisposable
    {
        public ApiCallContext Context { get; } = context;
        public int? TurnIndex { get => Context.TurnIndex; set => Context.TurnIndex = value; }
        public void Dispose() => CurrentContext.Value = previous;
    }
}

/// <summary>
/// Records how long each outside call took (GAD-T413): an Information log line (which flows once the file log is healthy)
/// and a durable <see cref="ApiCallTiming"/> row, queued here and written by <see cref="ApiCallTimingWriter"/> off the
/// caller's path, so timing never adds to the student's wait. The queue is bounded; under a flood the oldest rows are dropped.
/// </summary>
public sealed class ApiCallRecorder(ILogger<ApiCallRecorder> log)
{
    private readonly Channel<ApiCallTiming> _queue = Channel.CreateBounded<ApiCallTiming>(
        new BoundedChannelOptions(20_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    internal ChannelReader<ApiCallTiming> Reader => _queue.Reader;

    public void Record(string callName, DateTimeOffset startedAt, long durationMs, bool succeeded, string? error = null, ApiCallContext? context = null)
    {
        try
        {
            context ??= ApiCallScope.Current;
            if (callName.Length > 80) callName = callName[..80];
            if (error is { Length: > 300 }) error = error[..300];
            log.LogInformation("API call {CallName} took {DurationMs} ms ({Outcome}) session {SessionId} turn {TurnIndex}{Error}",
                callName, durationMs, succeeded ? "ok" : "failed", context?.SessionId, context?.TurnIndex, error is null ? "" : ": " + error);
            _queue.Writer.TryWrite(new ApiCallTiming
            {
                Id = Guid.NewGuid(),
                OrganizationId = context?.OrganizationId,
                SessionId = context?.SessionId,
                TurnIndex = context?.TurnIndex,
                CallName = callName,
                StartedAtUtc = startedAt,
                DurationMs = (int)Math.Clamp(durationMs, 0, int.MaxValue),
                Succeeded = succeeded,
                ErrorSummary = error,
            });
        }
        catch
        {
            // Timing must never fail the call it measures.
        }
    }

    /// <summary>Times <paramref name="call"/> and records it; the call's own result or exception passes through unchanged.</summary>
    public async Task<T> TimeAsync<T>(string callName, Func<Task<T>> call, Func<T, (bool Ok, string? Error)>? outcome = null)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await call();
            var (ok, error) = outcome?.Invoke(result) ?? (true, null);
            Record(callName, startedAt, watch.ElapsedMilliseconds, ok, error);
            return result;
        }
        catch (Exception ex)
        {
            Record(callName, startedAt, watch.ElapsedMilliseconds, false, Summary(ex));
            throw;
        }
    }

    public async Task TimeAsync(string callName, Func<Task> call)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        try
        {
            await call();
            Record(callName, startedAt, watch.ElapsedMilliseconds, true);
        }
        catch (Exception ex)
        {
            Record(callName, startedAt, watch.ElapsedMilliseconds, false, Summary(ex));
            throw;
        }
    }

    public static string Summary(Exception ex) => ex is OperationCanceledException ? "cancelled" : $"{ex.GetType().Name}: {ex.Message}";
}

/// <summary>Writes queued <see cref="ApiCallTiming"/> rows to SQL in small batches (GAD-T413). A failed write is logged and dropped.</summary>
public sealed class ApiCallTimingWriter(ApiCallRecorder recorder, IServiceScopeFactory scopes, ILogger<ApiCallTimingWriter> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = recorder.Reader;
        var batch = new List<ApiCallTiming>(200);
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                while (batch.Count < 200 && reader.TryRead(out var row)) batch.Add(row);
                if (batch.Count == 0) continue;
                try
                {
                    using var scope = scopes.CreateScopeFor(TenantSnapshot.System);
                    var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                    db.ApiCallTimings.AddRange(batch);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Could not write {Count} API call timing row(s); they are dropped.", batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}

/// <summary>
/// Times every panel call (GAD-T413), named by who made it: panel.judge, panel.analysis, panel.claimExtraction,
/// panel.questionGeneration.*, panel.aporia, panel.verify.*, and so on. Wraps the registered panel and keeps its
/// <see cref="IPanelInfo"/>, so mock output is still flagged as mock.
/// </summary>
public sealed class TimedVotingClient(ISocraticVotingClient inner, ApiCallRecorder recorder) : ISocraticVotingClient, IPanelInfo
{
    public ISocraticVotingClient Inner => inner;
    public bool IsMock => inner is IPanelInfo { IsMock: true };
    public string Mode => inner is IPanelInfo info ? info.Mode : PanelModes.Live;
    public string Reason => inner is IPanelInfo info ? info.Reason : "";

    public Task<VotingResult> AskAsync(string question, string context, Quorum quorum, CancellationToken ct = default) =>
        Timed(CallNames.Panel("ask"), () => inner.AskAsync(question, context, quorum, ct));

    public Task<VotingResult> AskWithProfilesAsync(string question, string context, IReadOnlyList<VoterProfile> voters, CancellationToken ct = default) =>
        Timed(CallNames.Panel("askWithProfiles"), () => inner.AskWithProfilesAsync(question, context, voters, ct));

    public Task<DecisionResult> DecideAsync(string question, IReadOnlyList<string> options, string context, CancellationToken ct = default) =>
        Timed(CallNames.Panel("decide"), () => inner.DecideAsync(question, options, context, ct));

    public Task<IReadOnlyList<PanelReply>> FanOutAsync(string systemPrompt, string userMessage, int maxTokens = 4096, double temperature = 0.2,
        CancellationToken ct = default) =>
        Timed(CallNames.Panel("fanOut"), () => inner.FanOutAsync(systemPrompt, userMessage, maxTokens, temperature, ct), replies =>
        {
            var failed = replies.Count(r => r.IsError);
            return (replies.Count > failed, failed == 0 ? null : $"{failed} of {replies.Count} provider(s) failed: " +
                string.Join("; ", replies.Where(r => r.IsError).Select(r => $"{r.ProviderId}: {r.Error}")));
        });

    private async Task<T> Timed<T>(string name, Func<Task<T>> call, Func<T, (bool, string?)>? outcome = null)
    {
        // Set here (an async method), so the name is visible to the providers' own timings and restored on return.
        ApiCallScope.SetName(name);
        return await recorder.TimeAsync(name, call, outcome);
    }
}

/// <summary>Names a panel call after the code that made it (GAD-T413), read from the stack so no call site needs changing.</summary>
internal static class CallNames
{
    public static string Panel(string method)
    {
        try
        {
            foreach (var frame in new StackTrace(2, false).GetFrames())
            {
                var m = frame.GetMethod();
                var type = m?.DeclaringType;
                if (type is null) continue;
                var name = m!.Name;
                // Async state machines and lambdas are nested types named <Method>d__N / <>c__DisplayClass: report their owner.
                while (type.Name.StartsWith('<') && type.DeclaringType is not null)
                {
                    if (name == "MoveNext") name = Between(type.Name) ?? name;
                    type = type.DeclaringType;
                }
                if (name.StartsWith('<')) name = Between(name) ?? name;
                var ns = type.Namespace ?? "";
                if (type == typeof(TimedVotingClient) || type == typeof(ApiCallRecorder) || ns.StartsWith("System", StringComparison.Ordinal)
                    || ns.StartsWith("Microsoft", StringComparison.Ordinal) || ns.StartsWith("Gadfly.Socrates", StringComparison.Ordinal))
                    continue;
                return Map(type.Name, name);
            }
        }
        catch
        {
            // Fall through to the generic name.
        }
        return "panel." + method;
    }

    private static string Map(string type, string method) => type switch
    {
        "PanelAnswerJudge" => "panel.judge",
        "ResponseAnalyzer" => "panel.analysis",
        "ClaimAnalyzer" => "panel.claimExtraction",
        "TextStyleAssessor" => "panel.textStyle",
        "QuestionGenerator" => "panel.questionGeneration." + Trim(method),
        "QuestionPlanner" => "panel.questionPlanning." + Trim(method),
        "ResponseVerifier" => "panel.verify." + Trim(method),
        "SourceExamEngine" when method == "IsAporiaAsync" => "panel.aporia",
        "SourceExamEngine" => "panel.engine." + Trim(method),
        "AiObjectiveGrader" => "panel.aiGrade",
        _ => $"panel.{type}.{Trim(method)}",
    };

    private static string Trim(string method) => method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] : method;

    private static string? Between(string s)
    {
        var open = s.IndexOf('<');
        var close = s.IndexOf('>', open + 1);
        return open >= 0 && close > open + 1 ? s[(open + 1)..close] : null;
    }
}

/// <summary>
/// Times the examiner's ElevenLabs voice (GAD-T413): <c>tts.elevenlabs.firstByte</c> when the response headers arrive (what
/// the student waits for before the examiner starts speaking) and <c>tts.elevenlabs</c> when the audio stream ends.
/// </summary>
public sealed class ElevenLabsTimingHandler(ApiCallRecorder recorder) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = ApiCallScope.Current;
        var startedAt = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            recorder.Record("tts.elevenlabs", startedAt, watch.ElapsedMilliseconds, false, ApiCallRecorder.Summary(ex), context);
            throw;
        }
        var ok = response.IsSuccessStatusCode;
        var status = ok ? null : $"HTTP {(int)response.StatusCode}";
        recorder.Record("tts.elevenlabs.firstByte", startedAt, watch.ElapsedMilliseconds, ok, status, context);
        if (response.Content is { } content)
            response.Content = new TimedContent(content, early =>
                recorder.Record("tts.elevenlabs", startedAt, watch.ElapsedMilliseconds, ok, status ?? (early ? "ended early (stopped or replaced)" : null), context));
        else
            recorder.Record("tts.elevenlabs", startedAt, watch.ElapsedMilliseconds, ok, status, context);
        return response;
    }

    /// <summary>The response body, reporting once when it has been read to the end (or disposed before that).</summary>
    private sealed class TimedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly Action<bool> _done;
        private int _reported;

        public TimedContent(HttpContent inner, Action<bool> done)
        {
            _inner = inner;
            _done = done;
            foreach (var h in inner.Headers) Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        internal void Report(bool early)
        {
            if (Interlocked.Exchange(ref _reported, 1) == 0) _done(early);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync() => new TimedStream(await _inner.ReadAsStreamAsync(), this);

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            new TimedStream(await _inner.ReadAsStreamAsync(cancellationToken), this);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await _inner.CopyToAsync(stream, context);
            Report(false);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await _inner.CopyToAsync(stream, context, cancellationToken);
            Report(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? 0;
            return _inner.Headers.ContentLength is not null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Report(true);
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class TimedStream(Stream inner, TimedContent owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Seen(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Seen(inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Seen(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Seen(await inner.ReadAsync(buffer, cancellationToken));

        private int Seen(int read)
        {
            if (read == 0) owner.Report(false);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner.Report(true);
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            owner.Report(true);
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}

public static class ApiCallTimingServices
{
    /// <summary>
    /// Registers the timing recorder and its SQL writer, wraps the registered panel in <see cref="TimedVotingClient"/>, and
    /// labels each provider's fan-out call with the panel call it belongs to (GAD-T413). Call after <c>AddGadflyPanel</c>.
    /// </summary>
    public static IServiceCollection AddApiCallTimings(this IServiceCollection services)
    {
        services.AddSingleton<ApiCallRecorder>();
        services.AddHostedService<ApiCallTimingWriter>();
        services.AddTransient<ElevenLabsTimingHandler>();
        var original = services.LastOrDefault(d => d.ServiceType == typeof(ISocraticVotingClient));
        if (original is not null && original.Lifetime == ServiceLifetime.Singleton)
        {
            services.Remove(original);
            services.AddSingleton<ISocraticVotingClient>(sp =>
            {
                var inner = original.ImplementationInstance as ISocraticVotingClient
                    ?? (original.ImplementationFactory is { } factory ? (ISocraticVotingClient)factory(sp)
                        : (ISocraticVotingClient)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!));
                var recorder = sp.GetRequiredService<ApiCallRecorder>();
                PanelTimings.Observer = t => recorder.Record($"{ApiCallScope.CallName ?? "panel"}@{t.ProviderId}", t.StartedAt, t.DurationMs, t.Succeeded, t.Error);
                return new TimedVotingClient(inner, recorder);
            });
        }
        return services;
    }

    /// <summary>
    /// GET /healthz/call-timings?session=&lt;id&gt;&amp;limit=200 (GAD-T413): a plain-text table of the session's latest outside calls
    /// (or the latest across all sessions when no session is given), newest first, then the same calls slowest first, then
    /// count / average / max per call name.
    /// </summary>
    public static RouteHandlerBuilder MapCallTimings(IEndpointRouteBuilder app) =>
        app.MapGet("/healthz/call-timings", async (Guid? session, int? limit, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            using var scope = scopes.CreateScopeFor(TenantSnapshot.System);
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var take = Math.Clamp(limit ?? 200, 1, 2000);
            var query = db.ApiCallTimings.AsNoTracking();
            if (session is { } id) query = query.Where(t => t.SessionId == id);
            var rows = await query.OrderByDescending(t => t.StartedAtUtc).Take(take).ToListAsync(ct);
            var text = new System.Text.StringBuilder();
            void Table(string title, IEnumerable<ApiCallTiming> list)
            {
                text.AppendLine(title);
                foreach (var r in list)
                    text.AppendLine($"{r.StartedAtUtc:HH:mm:ss.fff}  {r.DurationMs,7} ms  {(r.Succeeded ? "ok  " : "FAIL")}  turn {r.TurnIndex?.ToString() ?? "-",3}  {r.CallName,-48} {r.SessionId}  {r.ErrorSummary}");
                text.AppendLine();
            }
            Table($"Latest {rows.Count} call(s){(session is null ? "" : $" for session {session}")}, newest first:", rows);
            Table("Same calls, slowest first:", rows.OrderByDescending(r => r.DurationMs));
            text.AppendLine("Per call name: count, avg ms, max ms");
            foreach (var g in rows.GroupBy(r => r.CallName).OrderByDescending(g => g.Average(r => r.DurationMs)))
                text.AppendLine($"{g.Key,-48} {g.Count(),5} {g.Average(r => r.DurationMs),8:0} {g.Max(r => r.DurationMs),8}");
            return Results.Text(text.ToString(), "text/plain; charset=utf-8");
        });
}
