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
    [Inject] private Sessions.CircuitReconnectSignal Reconnect { get; set; } = default!;

    private IDisposable? reconnectSubscription;

    /// <summary>Starts listening for this circuit's own connection coming back after being lost (GAD-T306). Called once,
    /// from the room's first render; stopped with the room (<see cref="DisposeReconnectWatch"/>).</summary>
    private void WatchReconnect() => reconnectSubscription ??= Reconnect.Subscribe(OnReconnectedAsync);

    private void DisposeReconnectWatch() => reconnectSubscription?.Dispose();

    /// <summary>
    /// The page's own connection to the server was lost and has just come back (GAD-T306): re-sync from the server's
    /// current state (the same path a reload takes) rather than carrying on from whatever this page last knew, which
    /// may be stale by now (a staff pause or time change, or the exam ending, while the connection was down). Runs on
    /// the circuit handler's own thread, not a render, so every touch of the room's state is marshalled through
    /// <c>InvokeAsync</c>.
    /// </summary>
    private Task OnReconnectedAsync(CancellationToken ct) => InvokeAsync(async () =>
    {
        if (stop.IsCancellationRequested || phase is Phase.NotFound or Phase.Ended) return;
        await LoadAsync();
        StateHasChanged();
    });

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
            // Only the clock changed: keep whatever this page shows now (a newer step may have arrived meanwhile), but always take the
            // server's clock state, whichever turn its step names.
            TakeClock(next);
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

    /// <summary>
    /// The panel is judging an answer and writing the next question (GAD-T290): the examiner fills the wait with a short neutral line
    /// if it lasts more than a moment. A spoken answer goes straight to judging (GAD-T405), so this is the wait after every answer.
    /// </summary>
    private bool VoicePondering => thinkingAbout == ThinkingAbout.Answer && phase == Phase.Thinking;

    /// <summary>
    /// A meaning clarification is spoken instead of the question. A read-back request (only ever one left over from before GAD-T405)
    /// is never spoken: the page takes that answer as heard (<c>TakeAsHeardAsync</c>).
    /// </summary>
    private string? VoicePrompt => step?.Verification is { Kind: ResponseLedger.MeaningKind } v ? v.Prompt : null;

    /// <summary>
    /// An answer may be given by voice when a question is waiting, or a meaning clarification left over from before GAD-T405 is (it
    /// is answered out loud). A spoken answer is never read back, said again or corrected (GAD-T405).
    /// </summary>
    private bool VoiceCanAnswer => phase == Phase.Answering && !busy && !staffHeld && step is { } s
        && (s.Verification is null || s.Verification.Kind == ResponseLedger.MeaningKind);

    /// <summary>
    /// Staff have paused the exam from the live monitor (GAD-T299), as <see cref="StaffNoticeBanner"/> learns it from the live feed
    /// (GAD-T471). The server refuses answers meanwhile, so the microphone closes (keeping what was already said) instead of letting
    /// the end-of-answer silence send words that would be turned away; it opens again on release.
    /// </summary>
    private bool staffHeld;

    private void OnStaffHeld(bool held) => staffHeld = held;

    /// <summary>
    /// "I don't know" may be pressed: nothing is being judged and staff are not holding the exam (GAD-T474). The server refuses any
    /// answer during a staff pause, so the button is off until they resume rather than inviting a press that would only be turned away.
    /// </summary>
    private bool CanSayDontKnow => !busy && !staffHeld;

    /// <summary>Why "I don't know" is off during a staff pause (GAD-T474); no tooltip otherwise.</summary>
    private string? DontKnowTitle => staffHeld ? "Your instructor paused the exam. You can use this again once they resume it." : null;

    /// <summary>
    /// Takes only the server's clock state from <paramref name="next"/>: keep whatever this page shows now (a newer step may have
    /// arrived meanwhile), whichever turn the step names.
    /// </summary>
    private void TakeClock(Gadfly.Server.Sessions.SessionStep next)
    {
        if (step is not { } current || next.State == SessionState.Ended) return;
        step = current with
        {
            Remaining = next.Remaining,
            ClockStarted = next.ClockStarted,
            ClockPaused = next.ClockPaused,
            PauseAllowanceLeft = next.PauseAllowanceLeft,
        };
        deadline = Clock.GetUtcNow() + next.Remaining;
    }

    private async Task OnVoiceActive(bool on)
    {
        voiceOn = on;
        await SyncPauseAsync();
    }

    private bool pauseSyncing;

    /// <summary>The exam is paused and the clock is stopped for it right now (the pause allowance isn't spent yet).</summary>
    private bool PauseStopsClock => step is { ClockPaused: true } p
        && Clock.GetUtcNow() - (deadline - p.Remaining) < p.PauseAllowanceLeft;

    /// <summary>
    /// A lost microphone, camera or examiner connection pauses the exam clock on the server, and getting them back resumes it
    /// (GAD-T263): the pause doesn't cost exam time, up to the exam's pause allowance. Only once the clock has started, and only for a
    /// device lost after the examiner spoke on this page; a reload with a pause still open resumes it when the devices are back.
    /// </summary>
    private async Task SyncPauseAsync()
    {
        if (pauseSyncing) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        pauseSyncing = true;
        try
        {
            // Loop until the server's clock matches the room: the devices may come and go again while a call is in flight.
            for (var tries = 0; tries < 4; tries++)
            {
                if (phase is not (Phase.Answering or Phase.Thinking) || step is not { ClockStarted: true } s) return;
                var wantPaused = !voiceOn && examinerHasSpoken;
                if (wantPaused == s.ClockPaused) return;
                if (wantPaused && s.PauseAllowanceLeft <= TimeSpan.Zero) return;
                var next = wantPaused
                    ? await Sessions.PauseClockAsync(org, SessionId, user, stop.Token)
                    : await Sessions.ResumeClockAsync(org, SessionId, user, stop.Token);
                TakeClock(next);
                // The server declined (allowance spent, exam ended): stop asking.
                if (next.State == SessionState.Ended || next.ClockPaused != wantPaused) return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The server's clock simply keeps running: a failure here never gives extra time.
            Log.LogWarning(ex, "The exam clock could not be paused or resumed for session {SessionId}.", SessionId);
        }
        finally
        {
            pauseSyncing = false;
        }
    }

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

    /// <summary>
    /// A finished spoken answer from the browser: it joins the ledger as a voice response and is judged as heard (GAD-T405). Returns
    /// whether the answer was taken (GAD-T473). Not taken (false) means it arrived while something else was going on (another step
    /// running, the reconnect re-load, a staff pause, a refusal) or for a question no longer being asked: the browser keeps the words
    /// and its reload draft, and sends them again once this question can be answered, or drops them with a plain note if the exam has
    /// moved to another question. A spoken answer is never cleared in the browser without having been taken here.
    /// </summary>
    private async Task<bool> OnVoiceAnswerAsync(VoiceAnswerPayload payload)
    {
        try { return await TakeVoiceAnswerAsync(payload); }
        // Not an EventCallback (it returns whether the answer was taken), so the page renders the step's result itself.
        finally { StateHasChanged(); }
    }

    /// <summary>
    /// The answer so far while the student is still speaking (GAD-T497): the recognizer's finished segments plus its current partial,
    /// for the question at <paramref name="turn"/>. The server may prepare the next step from it; nothing changes on the page. For
    /// VoiceRoom's <c>OnPartialAnswer</c> callback (voice-room.js sends the running transcript on each recognizer partial).
    /// </summary>
    internal async Task OnPartialAnswerAsync(int turn, string text)
    {
        if (busy || phase != Phase.Answering || step is null || step.Verification is not null || turn != step.TurnIndex) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        try { await Sessions.NotePartialAnswerAsync(org, SessionId, user, turn, text); }
        catch (Exception) { /* a hint only: never disturbs the exam */ }
    }

    private async Task<bool> TakeVoiceAnswerAsync(VoiceAnswerPayload payload)
    {
        if (busy || phase != Phase.Answering || step is null || !VoiceCanAnswer) return false;
        // Meant for another question (GAD-T321): never the answer to this one.
        if (payload.Turn is { } turn && turn != step.TurnIndex) return false;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return false;
        var built = VoiceAnswerBuilder.Build(payload, Clock.GetUtcNow());
        if (built is null)
        {
            error = "I didn't catch an answer to record. Please say it again.";
            // The browser shut the microphone when it sent this: open it again for the same question (GAD-T316). Taken, in that
            // nothing in it can be used: keeping it would only send the same unusable answer again.
            if (voiceRoom is not null) await voiceRoom.ListenAgainAsync();
            return true;
        }
        var text = built.Answer.Text;
        error = null;
        switch (step.Verification)
        {
            case null:
                // Voice strain indicators (GAD-T232): the answer's voice against the student's own first spoken answers, stored with it.
                // Busy before the first await, so a second answer arriving meanwhile is turned away instead of overwriting this one.
                busy = true;
                Answer answer;
                try { answer = await WithVoiceStrainAsync(org, built.Answer); }
                finally { busy = false; }   // RunStepAsync sets it again at once, with no await in between
                pendingAnswer = text;
                pendingVoiceAnswer = answer;
                answerStartedAt = answer.StartedAt;
                // Refused by the server (a staff pause that landed meanwhile, say): the page shows why, and the browser keeps the words.
                // A step that failed outright is still taken: the page's Try again resends this answer (pendingVoiceAnswer).
                return await RunStepAsync(ThinkingAbout.Answer, () => Sessions.AnswerAsync(org, SessionId, user, answer, expectedTurn: payload.Turn));
            case { Kind: ResponseLedger.MeaningKind } request:
                await ClarifyAsync(request, text);
                return true;
            default:
                return false;
        }
    }
}
