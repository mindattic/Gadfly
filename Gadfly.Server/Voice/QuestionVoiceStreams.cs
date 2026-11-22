using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Gadfly.Engine;
using Gadfly.Server.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using MindAttic.Avatar.AspNetCore;
using MindAttic.Avatar.ElevenLabs;

namespace Gadfly.Server.Voice;

/// <summary>
/// The examiner's next question, spoken while it is still being written (GAD-T495). The question generator hands each guarded part
/// to a <see cref="QuestionVoiceStream"/> (it is the step's <see cref="IQuestionSpeech"/>); each part is synthesized at once, one
/// ElevenLabs with-timestamps request per part, started as soon as the part is safe so the next part's audio is ready while the
/// previous one plays. The browser plays the whole question as one utterance on its avatar hub connection: it asks the hub to
/// <c>Speak</c> the stream's token, which <see cref="VoiceHubFilter"/> hands to <see cref="Play"/> instead of the library, and the
/// parts' audio and word timings go out on the library's own events (<c>speechStart</c>, <c>speechChunk</c>, <c>speechEnd</c>) with
/// the word times offset by the audio already sent, plus a <c>questionText</c> event with each part's words for the caption. If the
/// next part is not ready when the audio sent so far is about to run out, a short stretch of silence is sent, so the browser's
/// player never runs dry (it would end the utterance) and lip-sync stays on time.
/// Only text the generator guarded is ever spoken: a stream is created by the exam room for the student's own session and is
/// fed only by the server; the browser can only ask for it to be played, by its id, as that student.
/// </summary>
public sealed class QuestionVoiceStreams(IServiceProvider services, ApiCallRecorder timings, ILogger<QuestionVoiceStreams> log)
{
    /// <summary>What the browser passes to the avatar hub's <c>Speak</c> to play a stream: this prefix and the stream's id.</summary>
    public const string Prefix = "\u0001gadfly-question:";

    /// <summary>How long a stream is kept for the browser to play.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<string, QuestionVoiceStream> _streams = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Id, CancellationTokenSource Cts)> _playing = new(StringComparer.Ordinal);

    /// <summary>The PCM sample rate questions are streamed at, or null when the voice's output format is not PCM (no streaming then).</summary>
    public int? SampleRate => services.GetService<AvatarSpeechService>()?.SampleRate;

    /// <summary>
    /// A new stream for the next question in <paramref name="session"/>, for <paramref name="user"/> only. <paramref name="committedAt"/>
    /// (a <see cref="Stopwatch"/> timestamp) is when the answer before it was committed: the stream's timings count from there.
    /// </summary>
    public QuestionVoiceStream Open(Guid user, Guid organization, Guid session, string? voiceId, long committedAt, DateTimeOffset committedAtUtc)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, old) in _streams)
            if (now - old.CreatedAt > KeepFor) { _streams.TryRemove(id, out _); old.Dispose(); }
        var stream = new QuestionVoiceStream(Guid.NewGuid().ToString("N"), user, organization, session, voiceId, committedAt, committedAtUtc,
            (text, voice, ct) => services.GetRequiredService<ElevenLabsTtsClient>().StreamAsync(text, voice, ct), timings, log);
        _streams[stream.Id] = stream;
        return stream;
    }

    /// <summary>The token the browser passes to <c>Speak</c> to play <paramref name="stream"/>.</summary>
    public static string Token(QuestionVoiceStream stream) => Prefix + stream.Id;

    public static bool IsToken(string? text) => text is not null && text.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Plays the stream named by <paramref name="token"/> to one avatar hub connection of <paramref name="user"/>; returns the utterance
    /// id. Replaces anything this connection was playing. Refused (a <see cref="HubException"/>) for another student's stream, an
    /// unknown or expired one, or one already played.
    /// </summary>
    public string Play(string connectionId, Guid user, string token)
    {
        var id = token[Prefix.Length..];
        if (!_streams.TryGetValue(id, out var stream) || stream.User != user || !stream.TryClaim())
            throw new HubException("The examiner can only say what the exam asked.");
        var rate = SampleRate ?? throw new HubException("The examiner's voice can't stream this question.");
        var utterance = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        Cancel(connectionId);
        _playing[connectionId] = (utterance, cts);
        var client = services.GetRequiredService<IHubContext<AvatarSpeechHub>>().Clients.Client(connectionId);
        var format = services.GetRequiredService<AvatarSpeechService>().Format;
        _ = Task.Run(async () =>
        {
            try
            {
                await stream.PlayAsync(utterance, format, rate, (name, payload, ct) => client.SendAsync(name, payload, ct), cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { /* stopped or replaced */ }
            catch (Exception ex)
            {
                log.LogWarning(ex, "The streamed question {Id} could not be played.", stream.Id);
                try { await client.SendAsync("speechError", new { id = utterance, message = "Speech failed." }); } catch { /* connection gone */ }
            }
            finally
            {
                if (_playing.TryGetValue(connectionId, out var cur) && cur.Id == utterance) _playing.TryRemove(connectionId, out _);
                _streams.TryRemove(stream.Id, out _);
                stream.Dispose();
                cts.Dispose();
            }
        });
        return utterance;
    }

    /// <summary>Stops what this connection is playing (barge-in, or another utterance replacing it); tells the browser when asked.</summary>
    public async Task StopAsync(string connectionId, bool tell)
    {
        if (!_playing.TryRemove(connectionId, out var run)) return;
        try { run.Cts.Cancel(); } catch (ObjectDisposedException) { }
        if (!tell) return;
        try { await services.GetRequiredService<IHubContext<AvatarSpeechHub>>().Clients.Client(connectionId).SendAsync("speechStopped", new { id = run.Id }); }
        catch { /* connection gone */ }
    }

    /// <summary>The connection is gone: stop what it was playing.</summary>
    public void Forget(string connectionId) => Cancel(connectionId);

    private void Cancel(string connectionId)
    {
        if (_playing.TryRemove(connectionId, out var run))
            try { run.Cts.Cancel(); } catch (ObjectDisposedException) { }
    }
}

/// <summary>
/// One question being spoken as it is written (GAD-T495): the parts the generator handed over, each with its own synthesis running,
/// and whether the question is finished. Played once (<see cref="PlayAsync"/>).
/// </summary>
public sealed class QuestionVoiceStream : IQuestionSpeech, IDisposable
{
    /// <summary>Send silence when less than this much audio is left queued in the browser and the next part isn't ready.</summary>
    public const int LowWaterMs = 300;
    /// <summary>Each stretch of silence sent while waiting for the next part.</summary>
    public const int SilenceMs = 200;
    /// <summary>The longest wait for a part, after which the question is given up (the browser then reads it itself).</summary>
    public static TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);

    private sealed record Part(string Text, Channel<SpeechChunk> Audio);

    private readonly object _lock = new();
    private readonly List<Part> _parts = [];
    private readonly CancellationTokenSource _synth = new();
    private readonly Func<string, string?, CancellationToken, IAsyncEnumerable<SpeechChunk>> _tts;
    private readonly ApiCallRecorder _timings;
    private readonly ILogger _log;
    private readonly long _committedAt;
    private readonly DateTimeOffset _committedAtUtc;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApiCallContext? _context;
    private bool _ended, _claimed, _firstAudio;
    private int _disposed;

    public QuestionVoiceStream(string id, Guid user, Guid organization, Guid session, string? voiceId, long committedAt, DateTimeOffset committedAtUtc,
        Func<string, string?, CancellationToken, IAsyncEnumerable<SpeechChunk>> tts, ApiCallRecorder timings, ILogger log)
    {
        Id = id;
        User = user;
        Organization = organization;
        Session = session;
        VoiceId = voiceId;
        _committedAt = committedAt;
        _committedAtUtc = committedAtUtc;
        _tts = tts;
        _timings = timings;
        _log = log;
    }

    public string Id { get; }
    public Guid User { get; }
    public Guid Organization { get; }
    public Guid Session { get; }
    public string? VoiceId { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>The whole question once the generator finished it (every part said, joined); null until then or when abandoned.</summary>
    public string? Question { get; private set; }

    /// <summary>The move of the question (her speaking style), known from the first part.</summary>
    public MoveKind? Move { get; private set; }

    /// <summary>True once something has been said.</summary>
    public bool HasStarted { get { lock (_lock) return _parts.Count > 0; } }

    /// <summary>Raised once, on the first part (on the generator's thread): the browser should start playing the stream now.</summary>
    public event Action<QuestionVoiceStream>? Started;

    public void Say(string part, MoveKind move)
    {
        if (string.IsNullOrWhiteSpace(part) || Volatile.Read(ref _disposed) == 1) return;   // stopped or expired: nothing more is synthesized
        Part added;
        bool first;
        lock (_lock)
        {
            if (_ended) return;
            first = _parts.Count == 0;
            if (first)
            {
                Move = move;
                // The step's own call context (organization, session, turn), so the voice calls are timed against it.
                _context = ApiCallScope.Current is { } c ? new ApiCallContext(c.OrganizationId, c.SessionId) { TurnIndex = c.TurnIndex }
                    : new ApiCallContext(Organization, Session);
            }
            added = new Part(part.Trim(), Channel.CreateUnbounded<SpeechChunk>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }));
            _parts.Add(added);
            Pulse();
        }
        if (first) Record("voice.question.firstPart");
        _ = Task.Run(() => SynthesizeAsync(added));
        if (first)
        {
            try { Started?.Invoke(this); }
            catch (Exception ex) { _log.LogDebug(ex, "The browser could not be told the question {Id} is being spoken.", Id); }
        }
    }

    public void Finish(string question)
    {
        lock (_lock)
        {
            if (_ended) return;
            _ended = true;
            Question = question;
            Pulse();
        }
    }

    public void Abandon(string reason)
    {
        lock (_lock)
        {
            if (_ended) return;
            _ended = true;
            Pulse();
        }
        _log.LogInformation("The streamed question {Id} was abandoned after it started: {Reason}.", Id, reason);
    }

    /// <summary>Marks the stream as played; false when it already was.</summary>
    public bool TryClaim()
    {
        lock (_lock)
        {
            if (_claimed) return false;
            _claimed = true;
            return true;
        }
    }

    private void Pulse()
    {
        var old = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    private async Task SynthesizeAsync(Part part)
    {
        var context = _context;
        using var scope = context is null ? null : ApiCallScope.Begin(context.OrganizationId, context.SessionId, context.TurnIndex);
        try
        {
            await foreach (var chunk in _tts(part.Text, VoiceId, _synth.Token))
            {
                if (chunk.Audio.Length > 0 && !_firstAudio)
                {
                    _firstAudio = true;
                    Record("voice.question.firstAudio");
                }
                await part.Audio.Writer.WriteAsync(chunk, _synth.Token);
            }
            part.Audio.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            part.Audio.Writer.TryComplete(ex);
        }
    }

    /// <summary>
    /// Sends the question to the browser as one utterance: <c>speechStart</c>, then for each part a <c>questionText</c> and its audio
    /// chunks (word times offset by the audio sent before it), silence while a part is not ready yet, and <c>speechEnd</c> once the
    /// question is finished. A failed synthesis or a part that never comes throws (the caller tells the browser).
    /// </summary>
    public async Task PlayAsync(string utterance, string format, int sampleRate, Func<string, object, CancellationToken, Task> send, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var ok = false;
        try
        {
            await send("speechStart", new { id = utterance, format, sampleRate }, ct);
            var sentMs = 0.0;        // audio sent so far, in ms of playback
            long? firstSent = null;  // when the first audio went out: the browser has been playing since about then
            var bytesPerMs = sampleRate * 2 / 1000.0;
            var silence = new byte[(int)(SilenceMs * bytesPerMs) & ~1];

            // Waits for <task>, sending silence whenever the audio queued in the browser is about to run out.
            async Task WaitAsync(Task task)
            {
                var waitStarted = Stopwatch.GetTimestamp();
                while (!task.IsCompleted)
                {
                    if (Stopwatch.GetElapsedTime(waitStarted) > MaxWait) throw new TimeoutException("The next part of the question did not come.");
                    var delay = TimeSpan.FromMilliseconds(250);
                    if (firstSent is { } since)
                    {
                        var aheadMs = sentMs - Stopwatch.GetElapsedTime(since).TotalMilliseconds;
                        if (aheadMs < LowWaterMs)
                        {
                            await send("speechChunk", new { id = utterance, audio = Convert.ToBase64String(silence), words = Array.Empty<object>() }, ct);
                            sentMs += SilenceMs;
                            continue;
                        }
                        delay = TimeSpan.FromMilliseconds(Math.Max(10, aheadMs - LowWaterMs));
                    }
                    await Task.WhenAny(task, Task.Delay(delay, ct));
                    ct.ThrowIfCancellationRequested();
                }
            }

            for (var i = 0; ; i++)
            {
                Part? part;
                while (true)
                {
                    Task changed;
                    lock (_lock)
                    {
                        part = i < _parts.Count ? _parts[i] : null;
                        if (part is not null || _ended) break;
                        changed = _changed.Task;
                    }
                    await WaitAsync(changed);
                }
                if (part is null) break;   // finished (or abandoned) and every part played

                await send("questionText", new { id = utterance, text = part.Text }, ct);
                var partStart = sentMs;
                byte? carry = null;
                var reader = part.Audio.Reader;
                while (true)
                {
                    var ready = reader.WaitToReadAsync(ct).AsTask();
                    await WaitAsync(ready);
                    if (!await ready) break;   // this part's audio is complete (a failed synthesis throws here)
                    while (reader.TryRead(out var chunk))
                    {
                        var audio = chunk.Audio;
                        if (carry is { } c) { audio = [c, .. audio]; carry = null; }
                        if (audio.Length % 2 == 1) { carry = audio[^1]; audio = audio[..^1]; }
                        if (audio.Length == 0 && chunk.Words.Count == 0) continue;
                        await send("speechChunk", new
                        {
                            id = utterance,
                            audio = Convert.ToBase64String(audio),
                            words = chunk.Words.Select(w => new { word = w.Word, startMs = w.StartMs + partStart, durationMs = w.DurationMs }),
                        }, ct);
                        if (audio.Length > 0 && firstSent is null)
                        {
                            firstSent = Stopwatch.GetTimestamp();
                            Record("voice.question.firstAudioSent");
                        }
                        sentMs += audio.Length / bytesPerMs;
                    }
                }
                // The next part starts after all of this one's audio (its own timings start from zero).
                sentMs = Math.Max(sentMs, partStart);
            }
            await send("speechEnd", new { id = utterance }, ct);
            ok = true;
        }
        finally
        {
            _timings.Record("voice.question.played", DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(started),
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, ok, ok ? null : "stopped or failed", _context);
        }
    }

    /// <summary>A timing from the answer's commit to now (GAD-T495: how long the student waited for this much of the question).</summary>
    private void Record(string name) =>
        _timings.Record(name, _committedAtUtc, (long)Stopwatch.GetElapsedTime(_committedAt).TotalMilliseconds, true, null, _context);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _synth.Cancel(); } catch (ObjectDisposedException) { }
        _synth.Dispose();
    }
}
