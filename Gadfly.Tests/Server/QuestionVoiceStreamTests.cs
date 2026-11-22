using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Diagnostics;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Avatar.ElevenLabs;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// A question spoken as it is written (GAD-T495) plays as one utterance: each part synthesized as soon as it is said, played in order,
/// its word timings offset by the audio before it, silence filling any wait, and the end only once the question is finished.
/// </summary>
[TestFixture]
public class QuestionVoiceStreamTests
{
    private const int Rate = 22050;

    /// <summary>Fake synthesis: 100 ms of audio per word, one chunk per word, each with that word's timing from the part's start.</summary>
    private static async IAsyncEnumerable<SpeechChunk> Tts(string text, string? voice, [EnumeratorCancellation] CancellationToken ct)
    {
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new SpeechChunk(new byte[Rate * 2 / 10], [new WordTiming(words[i], i * 100, 90)]);
        }
    }

    private static QuestionVoiceStream NewStream() =>
        new("s1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow, Tts,
            new ApiCallRecorder(NullLogger<ApiCallRecorder>.Instance), NullLogger.Instance);

    private sealed record Sent(string Name, JsonElement Payload);

    private static Func<string, object, CancellationToken, Task> Collect(List<Sent> sent) => (name, payload, _) =>
    {
        lock (sent) sent.Add(new Sent(name, JsonSerializer.SerializeToElement(payload)));
        return Task.CompletedTask;
    };

    [Test]
    public async Task ThePartsPlayAsOneUtterance_InOrder_WithWordTimesFromItsStart()
    {
        var stream = NewStream();
        var sent = new List<Sent>();
        stream.Say("You said energy is stored.", MoveKind.Probe);
        var play = stream.PlayAsync("u1", "pcm_22050", Rate, Collect(sent), CancellationToken.None);
        stream.Say("Where is it stored?", MoveKind.Probe);
        stream.Finish("You said energy is stored. Where is it stored?");
        await play;

        Assert.That(sent.First().Name, Is.EqualTo("speechStart"));
        Assert.That(sent.Last().Name, Is.EqualTo("speechEnd"));
        Assert.That(sent.Where(s => s.Name == "questionText").Select(s => s.Payload.GetProperty("text").GetString()),
            Is.EqualTo(new[] { "You said energy is stored.", "Where is it stored?" }));
        var words = sent.Where(s => s.Name == "speechChunk").SelectMany(s => s.Payload.GetProperty("words").EnumerateArray())
            .Select(w => (Word: w.GetProperty("word").GetString(), Start: w.GetProperty("startMs").GetDouble())).ToList();
        Assert.That(words.Select(w => w.Word), Is.EqualTo("You said energy is stored. Where is it stored?".Split(' ')));
        // The second part starts after all of the first part's audio (5 words x 100 ms) and any silence sent while waiting for it.
        var second = words.First(w => w.Word == "Where").Start;
        Assert.That(second, Is.GreaterThanOrEqualTo(500));
        Assert.That(words.Zip(words.Skip(1)).All(p => p.Second.Start > p.First.Start), Is.True);
        Assert.That(stream.Question, Is.EqualTo("You said energy is stored. Where is it stored?"));
    }

    [Test]
    public async Task WhileTheNextPartIsNotReady_SilenceKeepsThePlayerFed()
    {
        var stream = NewStream();
        var sent = new List<Sent>();
        stream.Say("One two three four.", MoveKind.Probe);
        var play = stream.PlayAsync("u1", "pcm_22050", Rate, Collect(sent), CancellationToken.None);
        await Task.Delay(900);   // the 400 ms of audio sent runs out meanwhile
        stream.Say("Five six?", MoveKind.Probe);
        stream.Finish("One two three four. Five six?");
        await play;

        var silences = sent.Count(s => s.Name == "speechChunk" && s.Payload.GetProperty("words").GetArrayLength() == 0);
        Assert.That(silences, Is.GreaterThan(0));
        var five = sent.Where(s => s.Name == "speechChunk").SelectMany(s => s.Payload.GetProperty("words").EnumerateArray())
            .First(w => w.GetProperty("word").GetString() == "Five").GetProperty("startMs").GetDouble();
        Assert.That(five, Is.GreaterThanOrEqualTo(400 + QuestionVoiceStream.SilenceMs), "the silence counts in the word timings");
    }

    [Test]
    public void AStreamPlaysOnce_AndNothingIsSaidAfterItEnds()
    {
        var stream = NewStream();
        Assert.That(stream.TryClaim(), Is.True);
        Assert.That(stream.TryClaim(), Is.False);
        stream.Say("First part of it.", MoveKind.Probe);
        stream.Abandon("failed");
        stream.Say("Never heard.", MoveKind.Probe);
        Assert.That(stream.Question, Is.Null);
        Assert.That(QuestionVoiceStreams.IsToken(QuestionVoiceStreams.Token(stream)), Is.True);
        Assert.That(QuestionVoiceStreams.IsToken("What is it?"), Is.False);
    }
}
