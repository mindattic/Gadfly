using Gadfly.Server.Integrity;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// Leaving the exam window (GAD-T511, docs/research/attention-and-gaze.md 4.4). integrity.js watchFocus reports an absence of a second
/// or more (the tab hidden or the window unfocused) for every exam, whatever its integrity level: the examiner stops at once and the
/// answer waits; on return a neutral line says the exam paused and she repeats the end of the question. Each absence is noted
/// (<see cref="WindowFocusRecorder"/>); three, or one of a minute, mark the attempt for an instructor's review, which the student is
/// told about with a box to explain. Nothing is cancelled or deleted, there is no strike counter on screen, and the exam clock keeps
/// its usual rules (an absence gives no extra time).
/// </summary>
public partial class ExamRoom
{
    private IJSObjectReference? focusModule;
    private DotNetObjectReference<ExamRoom>? focusRef;
    private bool focusStarted, windowAway, windowBackNotice, reviewNotice, reviewNoteSent, reviewNoteSending;
    private string reviewNote = "";
    private CancellationTokenSource? windowNoticeTimer;

    /// <summary>How long the neutral "the exam paused" line stays after the student is back.</summary>
    private static readonly TimeSpan WindowNoticeFor = TimeSpan.FromSeconds(10);

    /// <summary>Starts watching the exam window once the room is running (called after the first load). Never blocks the exam.</summary>
    private async Task StartWindowWatchAsync()
    {
        if (focusStarted || phase is not (Phase.Answering or Phase.Thinking)) return;
        focusStarted = true;
        try
        {
            focusRef ??= DotNetObjectReference.Create(this);
            focusModule = await JS.InvokeAsync<IJSObjectReference>("import", stop.Token, "./js/integrity.js");
            await focusModule.InvokeAsync<bool>("watchFocus", stop.Token, focusRef, SessionId.ToString("N"));
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            Log.LogDebug(ex, "The exam window watch did not start for session {SessionId}.", SessionId);
        }
    }

    /// <summary>The student left the exam window (for a second or more): she stops and the answer waits.</summary>
    [JSInvokable]
    public Task OnWindowLeft() => InvokeAsync(async () =>
    {
        if (phase is not (Phase.Answering or Phase.Thinking)) return;
        windowAway = true;
        windowBackNotice = false;
        windowNoticeTimer?.Cancel();
        if (voiceRoom is not null) await voiceRoom.PauseForWindowAsync();
        StateHasChanged();
    });

    /// <summary>The student is back after <paramref name="durationMs"/> away: a neutral line, the examiner resumes, the absence is noted.</summary>
    [JSInvokable]
    public Task OnWindowBack(double durationMs) => InvokeAsync(async () =>
    {
        windowAway = false;
        if (phase is Phase.Answering or Phase.Thinking)
        {
            windowBackNotice = true;
            HideWindowNoticeLater();
            if (voiceRoom is not null) await voiceRoom.ResumeAfterWindowAsync();
        }
        StateHasChanged();
        if (!double.IsFinite(durationMs) || durationMs < 0) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        try
        {
            await using var scope = Scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(org));
            var result = await WindowFocusRecorder.Create(scope.ServiceProvider)
                .RecordAbsenceAsync(org, user, SessionId, TimeSpan.FromMilliseconds(Math.Min(durationMs, TimeSpan.FromHours(4).TotalMilliseconds)), stop.Token);
            if (result is { NeedsReview: true, Exempt: false } && !reviewNoteSent)
            {
                reviewNotice = true;
                StateHasChanged();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never the student's problem: the exam goes on.
            Log.LogWarning(ex, "Could not note an absence from the exam window for session {SessionId}.", SessionId);
        }
    });

    private void HideWindowNoticeLater()
    {
        windowNoticeTimer?.Cancel();
        var cts = windowNoticeTimer = new CancellationTokenSource();
        _ = Task.Delay(WindowNoticeFor, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || stop.IsCancellationRequested) return;
            _ = InvokeAsync(() => { windowBackNotice = false; StateHasChanged(); });
        }, TaskScheduler.Default);
    }

    /// <summary>The student's optional explanation, kept beside the notes for the instructor.</summary>
    private async Task SendReviewNoteAsync()
    {
        if (reviewNoteSending || string.IsNullOrWhiteSpace(reviewNote)) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        reviewNoteSending = true;
        try
        {
            await using var scope = Scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(org));
            reviewNoteSent = await WindowFocusRecorder.Create(scope.ServiceProvider).SaveNoteAsync(org, user, SessionId, reviewNote, stop.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.LogWarning(ex, "Could not keep the student's explanation for session {SessionId}.", SessionId);
        }
        finally
        {
            reviewNoteSending = false;
        }
    }

    private async Task DisposeWindowWatchAsync()
    {
        windowNoticeTimer?.Cancel();
        if (focusModule is not null)
        {
            try
            {
                await focusModule.InvokeVoidAsync("unwatchFocus");
                await focusModule.DisposeAsync();
            }
            catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException or ObjectDisposedException) { }
        }
        focusRef?.Dispose();
    }
}
