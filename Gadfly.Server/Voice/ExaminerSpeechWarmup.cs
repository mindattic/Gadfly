using MindAttic.Avatar.AspNetCore;

namespace Gadfly.Server.Voice;

/// <summary>
/// Prepares the examiner's fixed lines before they are needed (GAD-T303): the eye-contact remarks, the short read-back for a long
/// answer and the like are synthesized once per server into the avatar library's fixed-phrase cache, so the first time a student
/// hears one it starts at once instead of waiting for the speech provider. Questions are not warmed (they are written for each
/// student and spoken the moment they exist); they are cached after their first playing, so "Repeat the question" and a reload
/// replay them instantly.
/// <para>
/// Runs in the background, once per phrase per server process, and never blocks or fails the exam: a phrase that can't be
/// prepared is simply synthesized when it is spoken. In mock mode this exercises <see cref="MockTextToSpeech"/>; in live mode it
/// costs the characters of these few fixed lines once per process (turn off with <c>Gadfly:Voice:WarmPhrases=false</c>).
/// </para>
/// </summary>
public sealed class ExaminerSpeechWarmup(IServiceProvider services, VoiceSettings settings, ILogger<ExaminerSpeechWarmup> log)
{
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>Warms <paramref name="phrases"/> in the background; returns at once.</summary>
    public void Warm(IEnumerable<string> phrases)
    {
        if (!settings.Available || !settings.WarmPhrases) return;
        string[] todo;
        lock (_done) todo = phrases.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Where(_done.Add).ToArray();
        if (todo.Length == 0) return;
        _ = Task.Run(() => RunAsync(todo));
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
                    lock (_done) _done.Remove(phrase);   // try again next time
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
