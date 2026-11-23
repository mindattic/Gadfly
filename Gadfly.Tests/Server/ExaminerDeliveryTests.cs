using Gadfly.Engine;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Configuration;
using MindAttic.Avatar.ElevenLabs;
using VoiceSettings = Gadfly.Server.Voice.VoiceSettings;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The examiner's delivery (GAD-T303): speaking styles, the mock voice's timing data and the voice settings. No provider.</summary>
[TestFixture]
public class ExaminerDeliveryTests
{
    [Test]
    public void Every_move_has_a_style_and_moves_after_weak_answers_look_like_any_follow_up()
    {
        foreach (var move in Enum.GetValues<MoveKind>())
            Assert.That(ExaminerStyle.All, Does.Contain(ExaminerStyle.For(move)), move.ToString());
        // A counterexample or a contradiction tends to follow a weak answer: it must look exactly like a probe or a clarification.
        Assert.That(ExaminerStyle.For(MoveKind.Counterexample), Is.EqualTo(ExaminerStyle.For(MoveKind.Probe)));
        Assert.That(ExaminerStyle.For(MoveKind.Contradiction), Is.EqualTo(ExaminerStyle.For(MoveKind.Clarify)));
        Assert.That(ExaminerStyle.For(nameof(MoveKind.Elicit)), Is.EqualTo(ExaminerStyle.Open));
        Assert.That(ExaminerStyle.For((string?)null), Is.EqualTo(ExaminerStyle.Neutral));
        Assert.That(ExaminerStyle.For("not a move"), Is.EqualTo(ExaminerStyle.Neutral));
    }

    [Test]
    public async Task The_mock_voice_gives_the_library_the_same_timing_data_as_the_live_provider()
    {
        // The library's own client reads the mock exactly as it reads ElevenLabs' with-timestamps stream: lip sync is exercised in mock mode.
        const string text = "Why does that follow, in your own words?";
        var options = new ElevenLabsOptions { ApiKey = "mock-key-not-real", VoiceId = "mock-voice", OutputFormat = "pcm_22050" };
        using var http = new HttpClient(new MockTextToSpeech()) { BaseAddress = new Uri("https://mock.invalid") };
        var client = new ElevenLabsTtsClient(http, options);

        var words = new List<WordTiming>();
        long bytes = 0;
        var chunks = 0;
        await foreach (var chunk in client.StreamAsync(text))
        {
            chunks++;
            bytes += chunk.Audio.Length;
            words.AddRange(chunk.Words);
        }

        Assert.That(chunks, Is.GreaterThan(1), "streamed in pieces, like the live service");
        Assert.That(words.Select(w => w.Word), Is.EqualTo(text.Split(' ')), "one timing per spoken word, in order, even across chunk boundaries");
        Assert.That(words.Zip(words.Skip(1)).All(p => p.Second.StartMs > p.First.StartMs), Is.True);
        Assert.That(words.All(w => w.DurationMs > 0), Is.True);
        var commaGap = words[4].StartMs - (words[3].StartMs + words[3].DurationMs);
        var plainGap = words[1].StartMs - (words[0].StartMs + words[0].DurationMs);
        Assert.That(commaGap, Is.GreaterThan(plainGap), "a comma leaves a longer pause");
        var audioMs = bytes / 2.0 / MockTextToSpeech.SampleRate * 1000;
        Assert.That(audioMs, Is.EqualTo(words[^1].StartMs + words[^1].DurationMs).Within(60), "the audio and the timings line up");
        Assert.That(MockTextToSpeech.Syllables("examiner"), Is.EqualTo(4));
    }

    [Test]
    public void Voice_settings_read_their_defaults_and_ignore_an_old_portrait_setting()
    {
        static IConfiguration Config(params (string, string?)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:PortraitUrl", "/me.png"), ("Gadfly:Voice:PortraitMouth", "0.5,0.7,0.2")), true, null).Mode,
            Is.EqualTo(VoiceMode.Mock), "an old portrait setting is ignored");
        Assert.That(VoiceSettings.From(Config(), true, null).TtsModel, Is.EqualTo("eleven_flash_v2_5"));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:WarmPhrases", "false")), true, null).WarmPhrases, Is.False);
    }

    [Test]
    public void Fixed_lines_are_prepared_once_then_checked_again_only_after_a_while()
    {
        // GAD-T329: the wait fillers are among the fixed lines, and a line pushed out of the phrase cache by questions is put back.
        var warmup = new ExaminerSpeechWarmup(null!, VoiceSettings.From(new ConfigurationBuilder().Build(), true, null),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExaminerSpeechWarmup>.Instance);
        var start = DateTimeOffset.UnixEpoch;
        var lines = ExaminerFiller.First.Concat(ExaminerFiller.Again).Append(" Let me think. ").Append("").ToArray();

        var first = warmup.Due(lines, start);
        Assert.That(first, Is.EqualTo(ExaminerFiller.First.Concat(ExaminerFiller.Again)), "in order, trimmed, blanks and repeats dropped");
        Assert.That(warmup.Due(lines, start + TimeSpan.FromMinutes(1)), Is.Empty, "just prepared: nothing to do");
        Assert.That(warmup.Due(lines, start + ExaminerSpeechWarmup.RecheckAfter), Is.EqualTo(first), "checked again after a while");
    }

    [Test]
    public void Acknowledgements_never_repeat_back_to_back_and_never_judge_the_answer()
    {
        // GAD-T496: receipt, not assessment, so nothing in them can leak how an answer was graded.
        var judging = new[] { "good", "great", "right", "correct", "exactly", "excellent", "interesting", "wrong", "nice", "perfect" };
        foreach (var line in ExaminerFiller.Acknowledgements)
            Assert.That(judging.Any(w => line.Contains(w, StringComparison.OrdinalIgnoreCase)), Is.False, line);
        var random = new Random(7);
        string? last = null;
        for (var i = 0; i < 200; i++)
        {
            var next = ExaminerFiller.PickAcknowledgement(random, last);
            Assert.That(next, Is.Not.EqualTo(last));
            Assert.That(ExaminerFiller.Acknowledgements, Does.Contain(next));
            last = next;
        }
    }
}
