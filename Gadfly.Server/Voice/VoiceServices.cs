using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using MindAttic.Avatar.AspNetCore;
using Microsoft.AspNetCore.SignalR;
using MindAttic.SpeechRecognition;
using MindAttic.SpeechRecognition.AspNetCore;
using MindAttic.SpeechRecognition.Providers.Simulated;

namespace Gadfly.Server.Voice;

/// <summary>
/// Wires the voice exam room (GAD-T53): MindAttic.SpeechRecognition (the student's answers, relayed to a recognizer) and
/// MindAttic.Avatar (the examiner's voice and talking head). Everything is behind <see cref="VoiceSettings.Mode"/>:
/// <c>mock</c> uses the library's Simulated recognizer and an in-process fake text-to-speech (nothing leaves the process, nothing
/// is billed); <c>live</c> uses the real providers with keys from Vault on the server; <c>off</c> registers no voice hub at all.
/// </summary>
public static class VoiceServices
{
    public const string SpeechHubPath = "/hubs/speech";
    public const string AvatarHubPath = "/hubs/avatar";

    public static VoiceSettings AddVoiceExam(this WebApplicationBuilder builder)
    {
        var settings = VoiceSettings.From(builder.Configuration, builder.Environment.IsDevelopment(), builder.Environment.WebRootPath);
        var services = builder.Services;
        services.AddSingleton<VoiceGrants>();
        if (settings.Mode == VoiceMode.Live && string.IsNullOrWhiteSpace(builder.Configuration["MindAttic:Avatar:ElevenLabs:VoiceId"]))
        {
            // Live voice needs the examiner's ElevenLabs voice id; without it, voice stays off rather than half working.
            Console.Error.WriteLine("Gadfly:Voice:Mode is live but MindAttic:Avatar:ElevenLabs:VoiceId is not set; voice mode is off.");
            settings = VoiceSettings.Disabled;
        }
        services.AddSingleton(settings);
        services.AddSingleton<ExaminerSpeechWarmup>();   // a no-op while voice is off
        if (!settings.Available) return settings;

        var mock = settings.Mode == VoiceMode.Mock;
        if (mock) services.AddSingleton<ISpeechRecognizer>(_ => new SimulatedSpeechRecognizer());   // TryAdd in the library keeps this one
        services.AddMindAtticSpeechRecognition(o =>
        {
            // Live never falls back to the Simulated recognizer: its words are placeholders.
            if (!mock && (string.IsNullOrWhiteSpace(o.Provider) || o.Provider.Equals("Simulated", StringComparison.OrdinalIgnoreCase)))
                o.Provider = "ElevenLabs";
            // Gadfly asks Scribe to label speakers (the second-speaker signal), which only scribe_v3_live supports; the library's
            // default model refuses the session with "diarize is only supported by scribe_v3_live" (GAD-T228).
            var scribeModel = builder.Configuration["Gadfly:Voice:ScribeModel"] is { Length: > 0 } m ? m : "scribe_v3_live";
            o.ConfigureElevenLabs = e => e.ModelId = scribeModel;
        });
        var modelConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["MindAttic:Avatar:ElevenLabs:ModelId"]);
        services.AddMindAtticAvatar(builder.Configuration, o =>
        {
            // The low-latency voice model unless one is configured, so speech starts within about a second (GAD-T303).
            if (!modelConfigured) o.ModelId = settings.TtsModel;
            if (!mock) return;
            o.ApiKey = "mock-key-not-real";
            o.VoiceId ??= "mock-voice";
        });
        // The mock voice is the client's primary handler: no request can reach api.elevenlabs.io, whatever else is configured.
        if (mock) services.AddHttpClient("ElevenLabsTtsClient").ConfigurePrimaryHttpMessageHandler(() => new MockTextToSpeech());

        services.AddSignalR()
            .AddHubOptions<SpeechRecognitionHub>(o => o.AddFilter<VoiceHubFilter>())
            .AddHubOptions<AvatarSpeechHub>(o => o.AddFilter<VoiceHubFilter>());
        return settings;
    }

    /// <summary>Static file options that also serve the avatar model (.glb) and ES modules (.mjs), which the default content types leave out.</summary>
    public static StaticFileOptions StaticFiles()
    {
        var types = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        types.Mappings[".glb"] = "model/gltf-binary";
        types.Mappings[".mjs"] = "text/javascript";
        return new StaticFileOptions { ContentTypeProvider = types };
    }

    public static void MapVoiceExam(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<VoiceSettings>();
        if (!settings.Available) return;
        app.Logger.LogWarning(settings.Mode == VoiceMode.Mock
            ? "Voice exam room: MOCK mode. Simulated speech recognition and a fake examiner voice; nothing is sent to a speech provider and nothing is billed."
            : "Voice exam room: LIVE mode. Speech recognition and the examiner's voice use paid providers.");
        app.MapMindAtticSpeechRecognition(SpeechHubPath).RequireAuthorization(GadflyPolicies.Student).RequireRateLimiting(RateLimits.Hub);
        app.MapMindAtticAvatar(AvatarHubPath).RequireAuthorization(GadflyPolicies.Student).RequireRateLimiting(RateLimits.Hub);
    }
}
