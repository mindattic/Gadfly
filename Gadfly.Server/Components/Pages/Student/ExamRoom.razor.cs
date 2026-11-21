using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Server.Voice;
using Microsoft.AspNetCore.Components;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The exam room's voice mode (GAD-T53): which exams get it, and how a spoken answer joins the same ledger, verification and
/// judging steps a typed one uses. The browser side is wwwroot/js/voice-room.js and <see cref="VoiceRoom"/>.
/// </summary>
public partial class ExamRoom
{
    [Inject] private VoiceSettings VoiceConfig { get; set; } = default!;

    /// <summary>Voice is offered: it is switched on for the server and this exam is a voice exam. Typing stays available until the student turns voice on.</summary>
    private bool voiceAvailable;
    /// <summary>Voice mode is running: the main answer box is replaced by speaking (corrections can still be typed).</summary>
    private bool voiceOn;
    /// <summary>The spoken answer to resend, with its word timings and metrics, if a step failed partway.</summary>
    private Answer? pendingVoiceAnswer;

    /// <summary>Only exams the instructor set up for voice offer it; a text exam's consent never covered sending audio to a speech service.</summary>
    private async Task LoadVoiceAsync(Guid org, Guid user)
    {
        if (!VoiceConfig.Available || voiceAvailable) return;
        try
        {
            await using var scope = Scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(org));
            var settings = await IntegrityRecorder.Create(scope.ServiceProvider).SettingsAsync(org, user, SessionId, stop.Token);
            voiceAvailable = settings?.Mode == ExamMode.Voice;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.LogWarning(ex, "Voice mode could not be checked for session {SessionId}; the exam stays in text mode.", SessionId);
        }
    }

    /// <summary>A request about the last answer is spoken instead of the question.</summary>
    private string? VoicePrompt => step?.Verification?.Prompt;

    /// <summary>
    /// An answer may be given by voice when a question is waiting, or the pending request is one a spoken reply answers: saying the answer
    /// again, or a meaning clarification. The read-back's Yes / correct buttons are clicked, and a typed correction opens its own box.
    /// </summary>
    private bool VoiceCanAnswer => phase == Phase.Answering && !busy && step is { } s
        && (s.Verification is null
            || (s.Verification.Kind == ResponseLedger.TranscriptKind && verifyMode == VerifyMode.Repeat)
            || (s.Verification.Kind == ResponseLedger.MeaningKind && verifyMode == VerifyMode.None));

    private void OnVoiceActive(bool on) => voiceOn = on;

    /// <summary>A finished spoken answer from the browser: it joins the ledger as a voice response, then goes through the transcript read-back.</summary>
    private async Task OnVoiceAnswerAsync(VoiceAnswerPayload payload)
    {
        if (busy || phase != Phase.Answering || step is null || !VoiceCanAnswer) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        var built = VoiceAnswerBuilder.Build(payload, Clock.GetUtcNow());
        if (built is null)
        {
            error = "I didn't catch an answer to record. Please try again, or type it.";
            return;
        }
        var text = built.Answer.Text;
        error = null;
        switch (step.Verification)
        {
            case null:
                pendingAnswer = text;
                pendingVoiceAnswer = built.Answer;
                answerStartedAt = built.Answer.StartedAt;
                await RunStepAsync(ThinkingAbout.Answer, () => Sessions.AnswerAsync(org, SessionId, user, built.Answer));
                break;
            case { Kind: ResponseLedger.TranscriptKind }:
                // "Repeat my answer", said out loud: the new recognition replaces the doubtful one (the original stays on record).
                await VerifyStepAsync(ThinkingAbout.Answer, (o, u) => Sessions.CorrectAnswerAsync(o, SessionId, u, text, true));
                break;
            case { Kind: ResponseLedger.MeaningKind } request:
                await ClarifyAsync(request, text);
                break;
        }
    }
}
