using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Sources;

namespace Gadfly.Server.Sessions;

/// <summary>
/// Prefetching the next step while the student is still answering (GAD-T497). The exam room sends the answer so far as speech
/// recognition hears it (<see cref="NotePartialAnswerAsync"/>). Once those words have stood still for a moment, the whole next step
/// (judge the answer, plan, word and guard the next question) runs speculatively on a copy of the session. When the student
/// finishes with the same words, and nothing else changed the session meanwhile, the copy becomes the session and the next question
/// is ready at once; otherwise the copy is dropped. Cost is bounded: at most one speculation in flight per session, a debounce on
/// the partials, a minimum answer length and a cap per question. Every speculative call is metered ("exam-prefetch") and timed,
/// adopted or not.
/// </summary>
public sealed partial class SessionOrchestrator
{
    /// <summary>Settings under <c>Gadfly:LiveTurn:Prefetch*</c>.</summary>
    public const string PrefetchSetting = "Gadfly:LiveTurn:Prefetch";

    private bool PrefetchEnabled => !string.Equals(configuration?[PrefetchSetting], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>How long the answer's words must stand still before a speculation starts.</summary>
    private TimeSpan PrefetchDebounce => TimeSpan.FromMilliseconds(Setting("DebounceMs", 700, 100, 10_000));

    /// <summary>Speculations started per question at most (each costs a full judged step).</summary>
    private int PrefetchMaxPerTurn => Setting("MaxPerTurn", 2, 1, 10);

    /// <summary>An answer shorter than this is not speculated on (it is likely to go on, or to be "I don't know").</summary>
    private int PrefetchMinWords => Setting("MinWords", 4, 1, 100);

    private int Setting(string name, int fallback, int min, int max) =>
        int.TryParse(configuration?[$"{PrefetchSetting}:{name}"], out var v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>One session's speculation. Guarded by its own lock; never held across an await.</summary>
    private sealed class PrefetchState
    {
        public object Lock { get; } = new();
        public int TurnIndex { get; set; } = -1;
        public long Version { get; set; }
        public int Started { get; set; }
        public string LastSpeculatedWords { get; set; } = "";
        public Speculation? Current { get; set; }

        /// <summary>Forgets this turn's speculation, cancelling it if it is still running.</summary>
        public void Reset(int turnIndex)
        {
            Current?.Cancel.Cancel();
            Current = null;
            TurnIndex = turnIndex;
            Started = 0;
            LastSpeculatedWords = "";
        }
    }

    private sealed record Speculation(string Words, string BaseTranscript, string BaseLedger, CancellationTokenSource Cancel)
    {
        public Task<SourceExamEngine?> Result { get; set; } = Task.FromResult<SourceExamEngine?>(null);
    }

    /// <summary>The adopted step's next question (null when the speculative step ended the exam).</summary>
    private sealed record Prefetched(Turn? Next);

    /// <summary>
    /// The answer so far, as speech recognition hears it while the student is still talking (GAD-T497): finished segments plus the
    /// current partial, for the question at <paramref name="turnIndex"/>. Cheap and never blocks: it only notes the words and, once
    /// they have stood still for the debounce, may start one speculative step. Ignored when prefetching is off, for another question,
    /// while a step is running, or for a session that is not loaded.
    /// </summary>
    public async Task NotePartialAnswerAsync(Guid organizationId, Guid sessionId, Guid studentUserId, int turnIndex, string? text,
        CancellationToken ct = default)
    {
        if (!PrefetchEnabled) return;
        var words = SocraticSession.Words(text);
        if (words.Length == 0 || words.Length > 20_000) return;
        Live live;
        try { live = await GetLiveAsync(organizationId, sessionId, studentUserId, ct); }
        catch (SessionRefusedException) { return; }
        var state = live.Prefetch;
        long version;
        lock (state.Lock)
        {
            if (state.TurnIndex != turnIndex) state.Reset(turnIndex);
            version = ++state.Version;
        }
        if (words.Split(' ').Length < PrefetchMinWords) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PrefetchDebounce, CancellationToken.None);
                await StartSpeculationAsync(live, turnIndex, version, text!, words);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "A speculative next step could not start.");
            }
        }, CancellationToken.None);
    }

    private async Task StartSpeculationAsync(Live live, int turnIndex, long version, string text, string words)
    {
        var state = live.Prefetch;
        lock (state.Lock)
        {
            // Still talking (newer words), already speculating, out of budget, or these words were already tried.
            if (state.Version != version || state.TurnIndex != turnIndex || state.Started >= PrefetchMaxPerTurn
                || state.LastSpeculatedWords == words || state.Current is { Result.IsCompleted: false }) return;
        }
        // A consistent copy of the session: only when no step is running (never wait for one; the next partial can try again).
        if (!await live.Gate.WaitAsync(0)) return;
        string transcript, ledger;
        try
        {
            var s = live.Engine.Session;
            if (s.IsEnded || s.OpenTurn?.Index != turnIndex || s.IsHeld || TooLate(live)) return;
            transcript = SessionTranscriptSerializer.Serialize(s);
            ledger = live.Engine.Ledger.ToJson();
        }
        finally
        {
            live.Gate.Release();
        }
        var speculation = new Speculation(words, transcript, ledger, new CancellationTokenSource(TimeSpan.FromMinutes(2)));
        lock (state.Lock)
        {
            if (state.Version != version || state.TurnIndex != turnIndex || state.Started >= PrefetchMaxPerTurn
                || state.Current is { Result.IsCompleted: false }) return;
            state.Current?.Cancel.Dispose();
            state.Started++;
            state.LastSpeculatedWords = words;
            state.Current = speculation;
            speculation.Result = SpeculateAsync(live, turnIndex, text, speculation);
        }
        log.LogInformation("Speculative next step started on turn {Turn} ({Words} words).", turnIndex, words.Split(' ').Length);
    }

    /// <summary>Judges <paramref name="text"/> and prepares the next question on a copy of the session; null when it fails.</summary>
    private async Task<SourceExamEngine?> SpeculateAsync(Live live, int turnIndex, string text, Speculation speculation)
    {
        await Task.Yield();
        var org = live.OrganizationId;
        var sessionId = live.Engine.Session.Id;
        using var logScope = log.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId, ["OrganizationId"] = org });
        using var timing = Diagnostics.ApiCallScope.Begin(org, sessionId, turnIndex);
        using var usage = Gadfly.Socrates.PanelUsage.Track(out var calls);
        var ct = speculation.Cancel.Token;
        try
        {
            var copy = await SourceExamEngine.ResumeAsync(panel, live.Engine.Subject, SessionTranscriptSerializer.Deserialize(speculation.BaseTranscript),
                ClaimLedger.FromJson(speculation.BaseLedger), clock, ct);
            copy.AssessTextStyle = false;
            copy.LiveTurnModel = liveModel;
            copy.SourceTranslator = live.Engine.SourceTranslator;
            // Judged provisionally like the live step (GAD-T508); a copy never runs the panel on answers still pending.
            copy.DeferFullJudgment = live.Engine.DeferFullJudgment;
            copy.Speculative = true;
            await copy.SubmitAnswerAsync(new Answer { Text = text, Mode = "voice", CompletedAt = clock.GetUtcNow() }, ct);
            await copy.NextQuestionAsync(ct);
            return copy;
        }
        catch (Exception ex)
        {
            log.LogInformation("Speculative next step on turn {Turn} dropped: {Reason}", turnIndex, Diagnostics.ApiCallRecorder.Summary(ex));
            return null;
        }
        finally
        {
            // Spent whether or not it is used: metered against the session like any step (GAD-T47).
            if (calls.Count > 0)
            {
                try
                {
                    using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(org));
                    var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                    meter.Stage(db, org, sessionId, calls, "exam-prefetch");
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "The usage of a speculative step could not be saved.");
                }
            }
        }
    }

    /// <summary>
    /// Called in the answer step, under the session gate, just before the answer would be judged: adopts the speculative step when it
    /// was prepared from the same words on the same session state, so the next question is ready. Null when there is none to adopt (the
    /// answer is then judged as usual). Waits for a matching speculation still running: it started earlier than a fresh step would.
    /// </summary>
    private async Task<Prefetched?> AdoptPrefetchAsync(Live live, int turnIndex, Answer final, CancellationToken ct)
    {
        Speculation? speculation;
        var state = live.Prefetch;
        lock (state.Lock)
        {
            speculation = state.TurnIndex == turnIndex ? state.Current : null;
            state.Current = null;   // taken: a later partial for this turn starts nothing (the turn is being answered)
            state.Started = int.MaxValue;
        }
        if (speculation is null) return null;
        if (speculation.Words != SocraticSession.Words(final.Text))
        {
            speculation.Cancel.Cancel();
            log.LogInformation("Speculative next step on turn {Turn} not used: the final answer has other words.", turnIndex);
            return null;
        }
        try
        {
            return await AdoptAsync(live, turnIndex, final, speculation, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A speculative step is only ever a shortcut: anything wrong with it and the answer is judged as usual.
            log.LogWarning(ex, "Speculative next step on turn {Turn} not used: it could not be checked.", turnIndex);
            return null;
        }
    }

    private async Task<Prefetched?> AdoptAsync(Live live, int turnIndex, Answer final, Speculation speculation, CancellationToken ct)
    {
        var copy = await speculation.Result.WaitAsync(ct);
        if (copy is null) return null;
        // Anything that changed the session since the copy was taken (a clock resumed, a leak flagged, staff time) means the copy is stale.
        if (SessionTranscriptSerializer.Serialize(live.Engine.Session) != speculation.BaseTranscript)
        {
            log.LogInformation("Speculative next step on turn {Turn} not used: the session changed meanwhile.", turnIndex);
            return null;
        }
        // The answer on record is the one submitted (its timings, words and speech measurements), not the partial it was judged from.
        copy.Session.ReplaceAnswerDetails(turnIndex, final);
        // The recitation check reads the answer's timing (GAD-T242): if the real timing changes its verdict, judge again from scratch.
        var turn = copy.Session.Turns[turnIndex];
        var passages = await copy.Subject.PassagesAsync(turn.ConceptId, final.Text, ct);
        var recited = await copy.RecitationAsync(turn, passages, ct);
        if ((recited is null) != (SocraticSession.SteeringJudgment(turn)?.Recitation is null))
        {
            log.LogInformation("Speculative next step on turn {Turn} not used: the recitation check reads the final answer differently.", turnIndex);
            return null;
        }
        // The full panel judgment the live engine has running (an earlier answer's, GAD-T508) carries over, so it is applied once.
        copy.TakeOverJudgingFrom(live.Engine);
        copy.Speculative = false;
        live.Engine = copy;
        log.LogInformation("Speculative next step on turn {Turn} used: the next question was ready when the student finished.", turnIndex);
        return new Prefetched(copy.Session.IsEnded ? null : copy.Session.OpenTurn);
    }
}
