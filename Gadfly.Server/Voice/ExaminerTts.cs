using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MindAttic.Avatar.ElevenLabs;
using LibraryVoiceSettings = MindAttic.Avatar.ElevenLabs.VoiceSettings;

namespace Gadfly.Server.Voice;

/// <summary>
/// The examiner's voice when a session needs something the avatar library's one process-wide voice can't give (GAD-T530, GAD-T555):
/// another ElevenLabs model for the exam language (<see cref="VoiceSettings.TtsModels"/>), or the slightly slower, plainer pace for a
/// student answering in a second language (<see cref="VoiceSettings.PlainSpeechSpeed"/>). The library binds its model and voice
/// settings once per process and only lets a call choose the voice id (filed as MAA-T20), so such a session gets its own
/// <see cref="ElevenLabsTtsClient"/> built from the library's options with only the model and speed changed: same key, voice, output
/// format and HTTP client (in mock mode <see cref="MockTextToSpeech"/> is that client's handler, so nothing leaves the process), and the
/// library's own phrase cache, whose key already includes model and speed, so fixed lines are kept per voice, model, speed and text.
/// <para>
/// <see cref="For"/> returns null for every session the library's voice already fits, which includes every
/// <see cref="SessionSpeech.IsDefault"/> one (the owner's rule: an English exam by an English speaker sounds exactly as before), and a
/// native Korean speaker on the default model (it speaks Korean). Only those sessions that need it are routed here
/// (<see cref="VoiceHubFilter"/>, <see cref="QuestionVoiceStreams"/>).
/// </para>
/// </summary>
public sealed class ExaminerTts(IServiceProvider services, VoiceSettings settings)
{
    /// <summary>What a session's voice differs in: the model and the speed (null = the voice's own).</summary>
    public sealed record Profile(string ModelId, double? Speed);

    private readonly ConcurrentDictionary<Profile, ElevenLabsTtsClient> _clients = new();

    /// <summary>The library's own options (model, voice settings): what every other session speaks with.</summary>
    private ElevenLabsOptions? Base => services.GetService<ElevenLabsOptions>();

    /// <summary>The profile for <paramref name="speech"/>, or null when the library's own voice already fits it.</summary>
    public Profile? For(SessionSpeech? speech) => Choose(speech, settings, Base?.ModelId, Base?.VoiceSettings.Speed);

    /// <summary>
    /// The profile for <paramref name="speech"/> given the library's model and speed, or null when they already fit: an unchanged
    /// (<see cref="SessionSpeech.IsDefault"/>) session, or one whose language model and speed are the library's.
    /// </summary>
    public static Profile? Choose(SessionSpeech? speech, VoiceSettings settings, string? baseModel, double? baseSpeed)
    {
        if (speech is null || speech.IsDefault || string.IsNullOrWhiteSpace(baseModel)) return null;
        var model = settings.TtsModelFor(speech.Language) ?? baseModel;
        var speed = speech.Speed(settings) ?? baseSpeed;
        return model == baseModel && speed == baseSpeed ? null : new Profile(model, speed);
    }

    /// <summary>The client that speaks with <paramref name="profile"/>, made once per profile.</summary>
    public ElevenLabsTtsClient Client(Profile profile) => _clients.GetOrAdd(profile, p =>
    {
        var o = Base ?? throw new InvalidOperationException("The examiner's voice is not configured.");
        var v = o.VoiceSettings;
        var options = new ElevenLabsOptions
        {
            ApiKey = o.ApiKey, BaseUrl = o.BaseUrl, WebSocketBaseUrl = o.WebSocketBaseUrl, VoiceId = o.VoiceId,
            ModelId = p.ModelId, OutputFormat = o.OutputFormat, CacheMaxEntries = o.CacheMaxEntries,
            VoiceSettings = new LibraryVoiceSettings
            {
                Stability = v.Stability, SimilarityBoost = v.SimilarityBoost, Style = v.Style, UseSpeakerBoost = v.UseSpeakerBoost, Speed = p.Speed,
            },
        };
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ElevenLabsTtsClient));
        return new ElevenLabsTtsClient(http, options, services.GetService<SpeechCache>());
    });

    /// <summary>
    /// Speaks <paramref name="text"/> with <paramref name="profile"/>: streamed as it is synthesized, or, for a fixed line
    /// (<paramref name="cacheable"/>), whole from the phrase cache when it is there (synthesized and kept the first time).
    /// </summary>
    public async IAsyncEnumerable<SpeechChunk> StreamAsync(Profile profile, string text, string? voiceId, bool cacheable,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = Client(profile);
        if (cacheable)
        {
            var whole = await client.SynthesizeAsync(text, voiceId, useCache: true, ct);
            yield return new SpeechChunk(whole.Audio, whole.Words);
            yield break;
        }
        await foreach (var chunk in client.StreamAsync(text, voiceId, ct)) yield return chunk;
    }
}
