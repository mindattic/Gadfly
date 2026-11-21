using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Server.Voice;
using Microsoft.AspNetCore.Components;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The exam room's talking examiner (GAD-T53): how a spoken answer joins the ledger, verification and judging steps. Voice and camera
/// are mandatory for every exam (GAD-T239): there is no typed answer mode, and the exam clock starts when the examiner begins the first
/// question. The browser side is wwwroot/js/voice-room.js and <see cref="VoiceRoom"/>.
/// </summary>
public partial class ExamRoom
{
    [Inject] private VoiceSettings VoiceConfig { get; set; } = default!;

    /// <summary>The talking examiner is switched on for this server. Without it a spoken exam can't run (there is no typed mode).</summary>
    private bool voiceAvailable;
    /// <summary>The examiner, microphone and camera are running; while they aren't, the exam is paused and the question hidden.</summary>
    private bool voiceOn;
    /// <summary>The examiner has spoken at least once on this page (a later pause is a lost device, not a first connection).</summary>
    private bool examinerHasSpoken;
    private bool clockStarting;
    /// <summary>The spoken answer to resend, with its word timings and metrics, if a step failed partway.</summary>
    private Answer? pendingVoiceAnswer;

    /// <summary>
    /// Every exam is spoken (GAD-T239), whatever mode it was built with: the consent notice every student accepts describes the
    /// microphone and the camera. The only question is whether this server has the talking examiner turned on.
    /// </summary>
    private void LoadVoice() => voiceAvailable = VoiceConfig.Available;

    /// <summary>
    /// The examiner is beginning to speak. The first time, the held exam clock starts (GAD-T239): the student's time begins with the
    /// first spoken question, not with the click on Begin exam. The server starts it once, so a reload can't restart it.
    /// </summary>
    private async Task OnExaminerSpeaksAsync()
    {
        examinerHasSpoken = true;
        if (step is not { ClockStarted: false } || clockStarting) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        clockStarting = true;
        try
        {
            var next = await Sessions.StartClockAsync(org, SessionId, user, stop.Token);
            // Only the clock changed: keep whatever this page shows now (a newer step may have arrived meanwhile).
            if (step is { } current && next.State != SessionState.Ended && next.TurnIndex == current.TurnIndex)
            {
                step = current with { Remaining = next.Remaining, ClockStarted = next.ClockStarted };
                deadline = Clock.GetUtcNow() + next.Remaining;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The server's clock still counts from when the session started, so a failure here never gives extra time.
            Log.LogWarning(ex, "The exam clock could not be started for session {SessionId}.", SessionId);
        }
        finally
        {
            clockStarting = false;
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

    /// <summary>Adds the strain-vs-baseline comparison to a spoken answer; on any failure the answer goes on without it.</summary>
    private async Task<Answer> WithVoiceStrainAsync(Guid org, Answer answer)
    {
        try
        {
            await using var scope = Scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(org));
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            return await VoiceStrain.ApplyAsync(db, SessionId, answer, stop.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.LogDebug(ex, "Voice strain could not be compared for session {SessionId}.", SessionId);
            return answer;
        }
    }

    /// <summary>A finished spoken answer from the browser: it joins the ledger as a voice response, then goes through the transcript read-back.</summary>
    private async Task OnVoiceAnswerAsync(VoiceAnswerPayload payload)
    {
        if (busy || phase != Phase.Answering || step is null || !VoiceCanAnswer) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        var built = VoiceAnswerBuilder.Build(payload, Clock.GetUtcNow());
        if (built is null)
        {
            error = "I didn't catch an answer to record. Please say it again.";
            return;
        }
        var text = built.Answer.Text;
        error = null;
        switch (step.Verification)
        {
            case null:
                // Voice strain indicators (GAD-T232): the answer's voice against the student's own first spoken answers, stored with it.
                var answer = await WithVoiceStrainAsync(org, built.Answer);
                pendingAnswer = text;
                pendingVoiceAnswer = answer;
                answerStartedAt = answer.StartedAt;
                await RunStepAsync(ThinkingAbout.Answer, () => Sessions.AnswerAsync(org, SessionId, user, answer));
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
