using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Verification;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sessions;

/// <summary>
/// The panel judges each answer off the live turn (GAD-T508). On the turn, the one fast model (<see cref="Gadfly.Socrates.ILiveTurnModel"/>)
/// judges the answer provisionally and the planner picks the next move from that, so the examiner answers in conversational time. Right
/// after the step, the full multi-model panel judgment of that answer starts in the background (<see cref="SourceExamEngine.StartFullJudgment"/>);
/// when it is done it is applied under the session's step gate (<see cref="SourceExamEngine.ApplyFinishedJudgmentAsync"/>) and saved like
/// any step. The full judgment is the one that counts: it is what is graded, reported and reviewed, and the planner uses it from then
/// on. A material disagreement with the provisional judgment is recorded on the turn and in the audit trail. Recovery: the provisional
/// and pending state is in the saved transcript, so a restart resumes the pending judgments when the session loads
/// (<see cref="ResumeJudging"/>), every step starts the next one, and the exam never ends (nor writes its report) before they are in
/// (<see cref="SourceExamEngine.EndAsync"/>), including when the stale-session sweep ends it.
/// </summary>
public sealed partial class SessionOrchestrator
{
    /// <summary><c>Gadfly:LiveTurn:DeferJudging</c>: <c>false</c> puts the panel judgment back on the live turn.</summary>
    public const string DeferJudgingSetting = "Gadfly:LiveTurn:DeferJudging";

    /// <summary>The audit action recorded when the panel's full judgment disagreed materially with the provisional one.</summary>
    public const string ProvisionalDisagreedAction = "judgment.provisional.disagreed";

    /// <summary>
    /// The audit action recording how long the exam's end waited for the panel's last full judgments (GAD-T516), against the session,
    /// so the wait students see after their last answer can be measured from live exams before deciding to hide or shorten it.
    /// </summary>
    public const string EndJudgingWaitAction = "judgment.end.waited";

    /// <summary>On when the live turn has its fast model and the setting does not turn it off.</summary>
    private bool DeferJudging => liveModel is not null
        && !string.Equals(configuration?[DeferJudgingSetting], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts the earliest pending full judgment, if one is waiting and none is running, and applies it in the background when it is
    /// done. Call only while holding the session's step gate. Never throws: judging off the turn must never fail a step.
    /// </summary>
    private void KickJudging(Live live, bool retryFailed = true)
    {
        try
        {
            var task = live.Engine.StartFullJudgment(retryFailed);
            if (task is null || ReferenceEquals(live.JudgingWatched, task)) return;
            live.JudgingWatched = task;
            _ = task.ContinueWith(_ => ApplyJudgmentInBackgroundAsync(live), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "The panel judgment of an answer could not be started; the next step tries again.");
        }
    }

    /// <summary>A session loaded with answers judged only provisionally (after a restart): their full judgments resume.</summary>
    private void ResumeJudging(Live live) =>
        _ = Task.Run(async () =>
        {
            await live.Gate.WaitAsync();
            try { KickJudging(live); }
            finally { live.Gate.Release(); }
        });

    /// <summary>Applies a finished full judgment under the step gate, saves it, and starts the next pending one.</summary>
    private async Task ApplyJudgmentInBackgroundAsync(Live live)
    {
        var sessionId = live.Engine.Session.Id;
        using var logScope = log.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId, ["OrganizationId"] = live.OrganizationId });
        try
        {
            await live.Gate.WaitAsync();
            try
            {
                // A session evicted from the cache (ended, or advanced from another server) is no longer this one's to change.
                if (!_live.TryGetValue(sessionId, out var current) || !ReferenceEquals(current, live)) return;
                var changed = await live.Engine.ApplyFinishedJudgmentAsync(CancellationToken.None);
                // Saved even when nothing was applied: a judgment dropped or failed still spent panel calls, which are metered.
                using (Diagnostics.ApiCallScope.Begin(live.OrganizationId, sessionId, live.Engine.Session.Turns.Count - 1))
                    await PersistAsync(live, live.StudentUserId, [], CancellationToken.None);
                if (changed) log.LogInformation("Full panel judgment applied off the turn.");
                // The next pending answer, if any; one that just failed waits for the student's next step to try again.
                KickJudging(live, retryFailed: false);
            }
            finally
            {
                live.Gate.Release();
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            _live.TryRemove(new KeyValuePair<Guid, Live>(sessionId, live));
            log.LogWarning("A background panel judgment was not saved: the exam was advanced elsewhere. It resumes when the session reloads.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "A background panel judgment could not be applied; the next step tries again.");
        }
    }

    /// <summary>
    /// The end-of-exam wait for the panel's last judgments, if this step measured one (GAD-T516): logged, and written to the audit
    /// trail against the session (pending count, milliseconds waited, whether the exam then ended, failed, or was ended by the planner).
    /// </summary>
    private void RecordEndJudgingWait(GadflyDbContext db, Guid org, Guid sessionId, SourceExamEngine.EndJudgingWait? wait)
    {
        if (wait is null) return;
        log.LogInformation("The exam's end waited {WaitedMs} ms for the panel's last {Pending} judgment(s) (ended {Ended}, failed {Failed}, planned {Planned}).",
            wait.WaitedMs, wait.Pending, wait.Ended, wait.Failed, wait.Planned);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = org, ActorRole = "System", Action = EndJudgingWaitAction, EntityType = "ExamSession", EntityId = sessionId.ToString(),
            SessionId = sessionId, At = clock.GetUtcNow(),
            AfterJson = JsonSerializer.Serialize(new { wait.Pending, wait.WaitedMs, wait.Ended, wait.Failed, wait.Planned }),
            Reason = $"The exam's end waited {wait.WaitedMs} ms for the panel's last {wait.Pending} judgment(s).",
        });
    }

    /// <summary>
    /// A full judgment that disagreed materially with the provisional one the examiner steered by (GAD-T508) goes into the audit trail
    /// against the answer, for reviewers and for calibrating the fast model. The transcript keeps the comparison on the turn too.
    /// </summary>
    private async Task RecordDisagreementsAsync(GadflyDbContext db, Guid org, Guid sessionId, IReadOnlyDictionary<int, SessionTurn> stored,
        IReadOnlyList<SourceExamEngine.FullJudgment> judged, CancellationToken ct)
    {
        foreach (var j in judged)
        {
            if (j.Error is not null)
            {
                log.LogWarning("The panel could not judge turn {Turn} off the turn yet: {Error}", j.TurnIndex, j.Error);
                continue;
            }
            if (!j.Applied || j.Disagreement is null || j.Provisional is null || !stored.TryGetValue(j.TurnIndex, out var turnRow)) continue;
            log.LogInformation("Turn {Turn}: the panel's judgment disagrees with the provisional one: {Why}", j.TurnIndex, j.Disagreement);
            var ledger = new ResponseLedger(db, clock);
            if (await ledger.OriginalForTurnAsync(sessionId, turnRow.Id, ct) is not { } response) continue;
            var p = j.Provisional;
            ledger.Audit(org, null, ProvisionalDisagreedAction, response, sessionId,
                JsonSerializer.Serialize(new { provisional = p.Judgment.OverallScore, p.ProviderId, p.Model, p.ElapsedMs }),
                JsonSerializer.Serialize(new { full = j.Judgment!.OverallScore, voters = j.Judgment.Votes.Count, j.Judgment.Agreement, j.ElapsedMs }),
                $"The examiner steered by a provisional judgment the panel disagreed with: {j.Disagreement} The panel's judgment is the one graded.");
        }
    }
}
