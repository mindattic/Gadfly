using MindAttic.Avatar.AspNetCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// Prepares the examiner's fixed lines before they are needed (GAD-T303): the wait fillers (GAD-T290, "Let me think about that"
/// and the long-wait "Just a moment more"), the eye-contact remarks and the like are synthesized into the avatar library's
/// fixed-phrase cache when an exam room opens, so the first time a student hears one it starts at once instead of waiting for
/// the speech provider. Questions are not warmed (they are written for each student and spoken the moment they exist); they are
/// cached after their first playing, so "Repeat the question" and a reload replay them instantly.
/// <para>
/// The phrase cache is a bounded least-recently-used store that every question also goes into, so on a busy server a fixed
/// line can be pushed out by questions (GAD-T329). A phrase is therefore checked again by a later exam room once
/// <see cref="RecheckAfter"/> has passed: a line still cached is a cache hit (no provider call, and it is marked recently used,
/// which keeps it in), and only a line that was pushed out is synthesized again, which it would have been anyway the next time
/// it was said.
/// </para>
/// <para>
/// Runs in the background and never blocks or fails the exam: a phrase that can't be prepared is simply synthesized when it is
/// spoken. In mock mode this exercises <see cref="MockTextToSpeech"/>; in live mode it costs the characters of these few fixed
/// lines once per process, plus a line again only after it was pushed out (turn off with <c>Gadfly:Voice:WarmPhrases=false</c>).
/// </para>
/// </summary>
public sealed class ExaminerSpeechWarmup(IServiceProvider services, VoiceSettings settings, TimeProvider clock, ILogger<ExaminerSpeechWarmup> log)
{
    /// <summary>How long a prepared phrase is trusted to still be in the cache before a newly opened exam room checks it again.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(10);

    /// <summary>When each phrase was last prepared or confirmed in the cache (or is being prepared now).</summary>
    private readonly Dictionary<string, DateTimeOffset> _checked = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>Warms <paramref name="phrases"/> in the background, in the order given (most urgent first); returns at once.</summary>
    public void Warm(IEnumerable<string> phrases)
    {
        if (!settings.Available || !settings.WarmPhrases) return;
        var todo = Due(phrases, clock.GetUtcNow());
        if (todo.Length == 0) return;
        _ = Task.Run(() => RunAsync(todo));
    }

    /// <summary>The phrases not prepared yet, or not checked within <see cref="RecheckAfter"/>; each is marked as being checked now.</summary>
    public string[] Due(IEnumerable<string> phrases, DateTimeOffset now)
    {
        var due = new List<string>();
        lock (_checked)
        {
            foreach (var phrase in phrases.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal))
            {
                if (_checked.TryGetValue(phrase, out var at) && now - at < RecheckAfter) continue;
                _checked[phrase] = now;
                due.Add(phrase);
            }
        }
        return [.. due];
    }

    private async Task RunAsync(string[] phrases)
    {
        var speech = services.GetService<AvatarSpeechService>();
        if (speech is null) return;
        await _one.WaitAsync();
        try
        {
            foreach (var phrase in phrases)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    // Draining the stream with cacheable set stores the whole utterance; a cached one returns at once.
                    await foreach (var _ in speech.StreamAsync(phrase, null, cacheable: true, cts.Token)) { }
                }
                catch (Exception ex)
                {
                    lock (_checked) _checked.Remove(phrase);   // try again next time
                    log.LogDebug(ex, "An examiner phrase could not be prepared ahead; it will be synthesized when spoken.");
                }
            }
        }
        finally
        {
            _one.Release();
        }
    }
}
