using Gadfly.Engine;
using Gadfly.Server.Voice;
using Microsoft.Extensions.Configuration;
using MindAttic.Avatar.ElevenLabs;
using VoiceSettings = Gadfly.Server.Voice.VoiceSettings;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The examiner's delivery (GAD-T303): speaking styles, the mock voice's timing data and the portrait settings. No provider.</summary>
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
    public void The_portrait_mouth_is_known_for_the_default_portrait_and_validated_when_configured()
    {
        static IConfiguration Config(params (string, string?)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

        Assert.That(VoiceSettings.From(Config(), true, null).PortraitMouth, Is.EqualTo(VoiceSettings.DefaultPortraitMouth));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:PortraitUrl", "/me.png")), true, null).PortraitMouth, Is.Null, "unknown for a custom portrait");
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:PortraitUrl", "/me.png"), ("Gadfly:Voice:PortraitMouth", "0.5, 0.7, 0.2")), true, null).PortraitMouth, Is.EqualTo("0.5,0.7,0.2"));
        Assert.That(VoiceSettings.ParseMouth("0.5,1.2,0.2"), Is.Null);
        Assert.That(VoiceSettings.ParseMouth("nonsense"), Is.Null);
        Assert.That(VoiceSettings.From(Config(), true, null).TtsModel, Is.EqualTo("eleven_flash_v2_5"));
        Assert.That(VoiceSettings.From(Config(("Gadfly:Voice:WarmPhrases", "false")), true, null).WarmPhrases, Is.False);
    }
}
