using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gadfly.Server.Voice;

/// <summary>
/// An in-process stand-in for ElevenLabs' <c>with-timestamps</c> text-to-speech, used in <see cref="VoiceMode.Mock"/> (GAD-T53).
/// It is the primary HTTP handler of the avatar library's client, so no request ever leaves the process, whatever the
/// configuration says. The reply has the documented shape (newline-delimited JSON, base64 PCM 22.05 kHz mono with per-character
/// alignment, chunks that may end mid-word) with a quiet synthetic voice, so mock mode runs exactly the live lip-sync path
/// (GAD-T303): the library turns the character alignment into word timings, the avatar shapes the mouth from them, and the
/// captions follow them. Each word is one soft burst per syllable (so the mouth visibly opens and closes on long words), and
/// commas and full stops leave the pauses a speaker would. Korean works the same way (GAD-T530): the text is read as UTF-8, every
/// Hangul character gets its own alignment entry, and a Hangul syllable block is timed as a syllable.
/// </summary>
public sealed class MockTextToSpeech : HttpMessageHandler
{
    public const int SampleRate = 22050;
    private static int _requests;

    /// <summary>How many fake synthesis requests were served (verification: proves nothing paid was called).</summary>
    public static int Requests => Volatile.Read(ref _requests);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var text = JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? "";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(Synthesize(text), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>The pause after a word: longer after a comma, longest after the end of a sentence.</summary>
    private static double PauseAfter(string word) => word.Length == 0 ? 0.07 : word[^1] switch
    {
        ',' or ';' or ':' => 0.2,
        '.' or '?' or '!' => 0.34,
        _ => 0.07,
    };

    /// <summary>A rough syllable count (groups of vowels; each Hangul syllable block is one syllable, GAD-T530), at least one.</summary>
    public static int Syllables(string word)
    {
        var count = 0;
        var inVowel = false;
        foreach (var c in word.ToLowerInvariant())
        {
            if (IsHangulSyllable(c)) { count++; inVowel = false; continue; }
            var vowel = "aeiouy".Contains(c);
            if (vowel && !inVowel) count++;
            inVowel = vowel;
        }
        return Math.Max(1, count);
    }

    /// <summary>A precomposed Hangul syllable (U+AC00 to U+D7A3): one character, one spoken syllable.</summary>
    public static bool IsHangulSyllable(char c) => c is >= '\uAC00' and <= '\uD7A3';

    /// <summary>
    /// How long a word takes to say: the English estimate (0.14 s plus 0.045 s a letter), with each Hangul syllable block counted as a
    /// syllable (0.17 s) instead of a letter, since one block is a whole consonant-vowel(-consonant) syllable (GAD-T530). English text
    /// has none, so its timing is unchanged.
    /// </summary>
    public static double WordSeconds(string word)
    {
        var hangul = word.Count(IsHangulSyllable);
        return 0.14 + 0.045 * (word.Length - hangul) + 0.17 * hangul;
    }

    /// <summary>The newline-delimited JSON chunks for <paramref name="text"/>.</summary>
    public static string Synthesize(string text)
    {
        var chars = new List<string>();
        var starts = new List<double>();
        var ends = new List<double>();
        var pcm = new List<short>();
        double t = 0;
        void Silence(double seconds) { for (var i = 0; i < (int)(seconds * SampleRate); i++) pcm.Add(0); t += seconds; }

        var all = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (word, index) in all.Select((w, i) => (w, i)))
        {
            if (index > 0)
            {
                var gap = PauseAfter(all[index - 1]);
                chars.Add(" "); starts.Add(t); ends.Add(t + gap);
                Silence(gap);
            }
            var seconds = WordSeconds(word);
            var samples = (int)(seconds * SampleRate);
            var pitch = 150 + (word.Length * 13 % 60);
            var syllables = Syllables(word);
            for (var i = 0; i < samples; i++)
            {
                // One rise and fall per syllable inside the word's overall envelope.
                var envelope = Math.Sin(Math.PI * i / samples) * (0.35 + 0.65 * Math.Abs(Math.Sin(Math.PI * syllables * i / samples)));
                var v = 0.07 * envelope * (Math.Sin(2 * Math.PI * pitch * i / SampleRate) + 0.5 * Math.Sin(2 * Math.PI * pitch * 3 * i / SampleRate));
                pcm.Add((short)(v * short.MaxValue));
            }
            for (var c = 0; c < word.Length; c++)
            {
                chars.Add(word[c].ToString());
                starts.Add(t + seconds * c / word.Length);
                ends.Add(t + seconds * (c + 1) / word.Length);
            }
            t += seconds;
        }

        // Cut into chunks of about four words' worth of characters; a chunk may end mid-word, as the real service's can.
        var lines = new StringBuilder();
        const int Parts = 4;
        for (var p = 0; p < Parts && chars.Count > 0; p++)
        {
            var from = chars.Count * p / Parts;
            var to = chars.Count * (p + 1) / Parts;
            var sampleFrom = p == 0 ? 0 : (int)(starts[from] * SampleRate);
            var sampleTo = p == Parts - 1 ? pcm.Count : Math.Min(pcm.Count, (int)(starts[to] * SampleRate));
            sampleTo = Math.Max(sampleFrom, sampleTo);
            var bytes = new byte[(sampleTo - sampleFrom) * 2];
            for (var i = sampleFrom; i < sampleTo; i++) BitConverter.TryWriteBytes(bytes.AsSpan((i - sampleFrom) * 2), pcm[i]);
            var chunk = new JsonObject
            {
                ["audio_base64"] = Convert.ToBase64String(bytes),
                ["alignment"] = new JsonObject
                {
                    ["characters"] = new JsonArray(chars.Skip(from).Take(to - from).Select(c => (JsonNode?)c).ToArray()),
                    ["character_start_times_seconds"] = new JsonArray(starts.Skip(from).Take(to - from).Select(x => (JsonNode?)x).ToArray()),
                    ["character_end_times_seconds"] = new JsonArray(ends.Skip(from).Take(to - from).Select(x => (JsonNode?)x).ToArray()),
                },
            };
            lines.Append(chunk.ToJsonString()).Append('\n');
        }
        return lines.ToString();
    }
}
