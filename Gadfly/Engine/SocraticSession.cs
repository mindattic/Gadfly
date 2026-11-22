namespace Gadfly.Engine;

/// <summary>
/// One Socratic session: the policy it ran under, the prompt set that worded it, and every turn.
/// It is the unit the CLI writes to disk, the server persists turn by turn, and the report reads.
/// Mutations go through the methods so the turn sequence stays consistent.
/// </summary>
public sealed class SocraticSession
{
    private readonly List<Turn> _turns = [];
    private readonly Dictionary<string, MasterySnapshot> _mastery = new(StringComparer.Ordinal);

    public SocraticSession(string subjectId, SessionPolicy policy, PromptSet promptSet, DateTimeOffset startedAt, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("A session needs a subject.", nameof(subjectId));
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(promptSet);
        policy.Validate();
        Id = id ?? Guid.NewGuid();
        SubjectId = subjectId;
        Policy = policy;
        PromptSetId = promptSet.Id;
        PromptSetVersion = promptSet.Version;
        PromptSetSha256 = promptSet.Sha256;
        StartedAt = startedAt;
    }

    /// <summary>For the serializer: restores a session exactly as recorded.</summary>
    internal SocraticSession(Guid id, string subjectId, SessionPolicy policy, string promptSetId, int promptSetVersion,
        string promptSetSha256, DateTimeOffset startedAt, DateTimeOffset? endedAt, EndReason endReason,
        IEnumerable<Turn> turns, IEnumerable<MasterySnapshot> mastery, bool clockHeld = false,
        TimeSpan pausedTotal = default, DateTimeOffset? pausedSince = null,
        TimeSpan extraTime = default, TimeSpan heldTotal = default, DateTimeOffset? heldSince = null)
    {
        ClockHeld = clockHeld;
        ExtraTime = extraTime < TimeSpan.Zero ? TimeSpan.Zero : extraTime > MaxExtraTime ? MaxExtraTime : extraTime;
        HeldTotal = heldTotal < TimeSpan.Zero ? TimeSpan.Zero : heldTotal;
        HeldSince = heldSince;
        PausedTotal = pausedTotal < TimeSpan.Zero ? TimeSpan.Zero : pausedTotal;
        PausedSince = pausedSince;
        Id = id;
        SubjectId = subjectId;
        Policy = policy;
        PromptSetId = promptSetId;
        PromptSetVersion = promptSetVersion;
        PromptSetSha256 = promptSetSha256;
        StartedAt = startedAt;
        EndedAt = endedAt;
        EndReason = endReason;
        _turns.AddRange(turns);
        foreach (var m in mastery) _mastery[m.ConceptId] = m;
    }

    public Guid Id { get; }
    public string SubjectId { get; }
    public SessionPolicy Policy { get; }
    public string PromptSetId { get; }
    public int PromptSetVersion { get; }
    public string PromptSetSha256 { get; }
    /// <summary>When the exam clock started: when the session was created, or when a held clock was started (<see cref="StartClock"/>).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>
    /// True while the exam clock waits for the examiner to begin the first question (GAD-T239): the server's voice exam room holds
    /// it so planning the first question and connecting the microphone and camera don't use the student's time. Until it starts,
    /// <see cref="Remaining"/> still counts from <see cref="StartedAt"/>, so a clock that never starts can't give unlimited time.
    /// </summary>
    public bool ClockHeld { get; private set; }

    /// <summary>Holds the clock until <see cref="StartClock"/>. Only before any answer was recorded.</summary>
    public void HoldClock()
    {
        EnsureRunning();
        if (_turns.Any(t => t.Answer is not null)) throw new InvalidOperationException("The clock can only be held before the first answer.");
        ClockHeld = true;
    }

    /// <summary>
    /// Starts a held clock at <paramref name="at"/> (the examiner begins the first question); the open first question counts as asked
    /// then. Returns false when the clock was not held: it starts once, so a reload can't restart it.
    /// </summary>
    public bool StartClock(DateTimeOffset at)
    {
        if (!ClockHeld || IsEnded || at < StartedAt) return false;
        // Answers were already given while held: the clock was running for them, so it never restarts with a fresh budget.
        if (_turns.Any(t => t.Answer is not null))
        {
            ClockHeld = false;
            return false;
        }
        ClockHeld = false;
        StartedAt = at;
        if (OpenTurn is { } open && _turns.All(t => t.Answer is null)) Replace(open with { AskedAt = at });
        return true;
    }

    /// <summary>
    /// The most paused time that can be added back to one exam (GAD-T263): ten minutes, and never more than half the time budget. A
    /// pause is reported by the student's own browser, so the cap keeps a microphone pulled on purpose from buying unlimited time.
    /// </summary>
    public static readonly TimeSpan MaxPauseAllowance = TimeSpan.FromMinutes(10);

    /// <summary>The paused time this exam can have added back in all (see <see cref="MaxPauseAllowance"/>).</summary>
    public TimeSpan PauseAllowance
    {
        get
        {
            var half = Policy.EffectiveTimeBudget / 2;
            return half < MaxPauseAllowance ? half : MaxPauseAllowance;
        }
    }

    /// <summary>Paused time from pauses that have ended, already capped at <see cref="PauseAllowance"/>.</summary>
    public TimeSpan PausedTotal { get; private set; }

    /// <summary>
    /// When the current pause began (the microphone, the camera or the examiner's connection was lost after the clock started), or
    /// null while the clock runs (GAD-T263). While paused, the clock stops until the pause allowance is spent.
    /// </summary>
    public DateTimeOffset? PausedSince { get; private set; }

    public bool IsPaused => PausedSince is not null;

    /// <summary>The paused time added back at <paramref name="now"/>: ended pauses plus the open one, capped at <see cref="PauseAllowance"/>.</summary>
    public TimeSpan PausedCredit(DateTimeOffset now)
    {
        var total = PausedTotal;
        if (PausedSince is { } since && now > since) total += now - since;
        var cap = PauseAllowance;
        return total > cap ? cap : total;
    }

    /// <summary>The pause allowance still unused at <paramref name="now"/>.</summary>
    public TimeSpan PauseAllowanceLeft(DateTimeOffset now) => PauseAllowance - PausedCredit(now);

    /// <summary>
    /// Stops the exam clock at <paramref name="at"/> because the microphone, camera or examiner connection was lost (GAD-T263). Only
    /// once the clock has started and while the exam runs; returns false when nothing changed (already paused, held, ended, or the
    /// allowance is spent).
    /// </summary>
    public bool PauseClock(DateTimeOffset at)
    {
        if (IsEnded || ClockHeld || PausedSince is not null || HeldSince is not null || at < StartedAt) return false;
        if (PausedTotal >= PauseAllowance) return false;
        PausedSince = at;
        return true;
    }

    /// <summary>Restarts a paused exam clock at <paramref name="at"/>, adding the paused time back (up to the allowance). False when not paused.</summary>
    public bool ResumeClock(DateTimeOffset at)
    {
        if (PausedSince is null) return false;
        PausedTotal = PausedCredit(at < PausedSince.Value ? PausedSince.Value : at);
        PausedSince = null;
        return true;
    }

    // ------------------------------------------------------------------ staff time changes (GAD-T299)

    /// <summary>The most extra time staff can add to one exam in all (GAD-T299).</summary>
    public static readonly TimeSpan MaxExtraTime = TimeSpan.FromHours(2);

    /// <summary>The most time one exam can stand still under staff pauses in all; past it the clock counts down again (GAD-T299).</summary>
    public static readonly TimeSpan MaxHeldTime = TimeSpan.FromHours(4);

    /// <summary>Extra time added by staff during the exam (GAD-T299), on top of the budget and any accommodation multiplier.</summary>
    public TimeSpan ExtraTime { get; private set; }

    /// <summary>
    /// Adds <paramref name="by"/> to the exam's time, up to <see cref="MaxExtraTime"/> in all. Returns the time actually added
    /// (zero once the cap is reached). Only while the exam runs.
    /// </summary>
    public TimeSpan ExtendTime(TimeSpan by)
    {
        EnsureRunning();
        if (by <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), "Extra time must be positive.");
        var room = MaxExtraTime - ExtraTime;
        var applied = by < room ? by : room;
        if (applied <= TimeSpan.Zero) return TimeSpan.Zero;
        ExtraTime += applied;
        return applied;
    }

    /// <summary>When a staff pause began (GAD-T299), or null when staff have not paused the exam.</summary>
    public DateTimeOffset? HeldSince { get; private set; }

    /// <summary>Time added back from staff pauses that have ended, already capped at <see cref="MaxHeldTime"/>.</summary>
    public TimeSpan HeldTotal { get; private set; }

    /// <summary>True while staff have paused the exam: the clock stands still and answers wait.</summary>
    public bool IsHeld => HeldSince is not null;

    /// <summary>Time added back for staff pauses at <paramref name="now"/>: ended pauses plus the open one, capped at <see cref="MaxHeldTime"/>.</summary>
    public TimeSpan HeldCredit(DateTimeOffset now)
    {
        var total = HeldTotal;
        if (HeldSince is { } since && now > since) total += now - since;
        return total > MaxHeldTime ? MaxHeldTime : total;
    }

    /// <summary>
    /// Staff pause the exam at <paramref name="at"/> (GAD-T299). Unlike a device pause this is not limited by the pause allowance and is
    /// not ended by the student's activity; it ends with <see cref="Release"/>. An open device pause ends first, so the two never overlap.
    /// False when nothing changed (ended, not started yet, already paused by staff).
    /// </summary>
    public bool Hold(DateTimeOffset at)
    {
        if (IsEnded || ClockHeld || HeldSince is not null || at < StartedAt) return false;
        if (PausedSince is not null) ResumeClock(at);
        HeldSince = at;
        return true;
    }

    /// <summary>Ends a staff pause at <paramref name="at"/>, adding the paused time back. False when the exam was not paused by staff.</summary>
    public bool Release(DateTimeOffset at)
    {
        if (HeldSince is null) return false;
        HeldTotal = HeldCredit(at < HeldSince.Value ? HeldSince.Value : at);
        HeldSince = null;
        return true;
    }

    /// <summary>
    /// When time runs out, as things stand at <paramref name="now"/>: the start, the budget (with any accommodation), staff extra time,
    /// and the device-pause and staff-pause time added back.
    /// </summary>
    public DateTimeOffset Deadline(DateTimeOffset now) =>
        StartedAt + Policy.EffectiveTimeBudget + ExtraTime + PausedCredit(now) + HeldCredit(now);

    public DateTimeOffset? EndedAt { get; private set; }
    public EndReason EndReason { get; private set; } = EndReason.None;
    public bool IsEnded => EndedAt is not null;

    public IReadOnlyList<Turn> Turns => _turns;

    /// <summary>Latest mastery estimate per concept.</summary>
    public IReadOnlyDictionary<string, MasterySnapshot> Mastery => _mastery;

    /// <summary>The last turn, while it still waits for the student's answer.</summary>
    public Turn? OpenTurn => _turns.Count > 0 && _turns[^1].Answer is null ? _turns[^1] : null;

    /// <summary>The last turn, while it is answered but not yet judged at all (neither by the panel nor provisionally, GAD-T508).
    /// Subjects that don't judge (the personal introspection tool) leave every turn here; exam subjects judge before planning on.</summary>
    public Turn? AwaitingJudgment => _turns.Count > 0 && _turns[^1].Answer is not null && _turns[^1].Judgment is null
        && _turns[^1].Provisional is null ? _turns[^1] : null;

    /// <summary>
    /// Answered turns judged only provisionally so far (GAD-T508): the fast model's judgment steered the next move and the full
    /// panel judgment, the one that counts, is still to come. In turn order; usually empty or the newest one or two turns.
    /// </summary>
    public IReadOnlyList<Turn> PendingFullJudgments => _turns.Where(t => t.Answer is not null && t.Judgment is null && t.Provisional is not null).ToList();

    /// <summary>The judgment the examiner steers by: the full panel judgment once it arrived, else the provisional one (GAD-T508).</summary>
    public static AnswerJudgment? SteeringJudgment(Turn turn) => turn.Judgment ?? turn.Provisional?.Judgment;

    /// <summary>
    /// The session as the planner sees it (GAD-T508): every turn still waiting for its full panel judgment carries its provisional
    /// judgment in <see cref="Turn.Judgment"/>, so the next move is planned from the newest full judgments plus the provisional ones
    /// still pending. A copy, never persisted; this session itself when nothing is pending.
    /// </summary>
    public SocraticSession ForPlanning()
    {
        if (!_turns.Any(t => t.Judgment is null && t.Provisional is not null)) return this;
        return new SocraticSession(Id, SubjectId, Policy, PromptSetId, PromptSetVersion, PromptSetSha256, StartedAt, EndedAt, EndReason,
            _turns.Select(t => t.Judgment is null && t.Provisional is { } p ? t with { Judgment = p.Judgment } : t), _mastery.Values,
            ClockHeld, PausedTotal, PausedSince, ExtraTime, HeldTotal, HeldSince);
    }

    /// <summary>Time left in the budget (with any accommodation multiplier and paused time added back) at <paramref name="now"/> (never negative).</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        // Paused time (a lost microphone or camera, GAD-T263) is added back, up to the pause allowance; staff extra time and staff
        // pauses (GAD-T299) are added too.
        var left = Deadline(now) - now;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Turns asked so far on <paramref name="conceptId"/>.</summary>
    public int TurnsOn(string conceptId) => _turns.Count(t => t.ConceptId == conceptId);

    /// <summary>Asks a new question. The previous turn must be answered first.</summary>
    public Turn Ask(string conceptId, MoveKind move, string question, DateTimeOffset askedAt, IReadOnlyList<string>? targetClaimIds = null,
        string reason = "", string? objectiveKey = null, bool isReplacement = false, ApplyNoveltyCheck? novelty = null, QuestionAuthorship? author = null)
    {
        if (MoveProfiles.IsAssistance(move) && Policy.Mode != Gadfly.Assessment.AssessmentMode.Learning)
            throw new InvalidOperationException("Examination mode never teaches: hints and guided explanations are Learning Mode only.");
        EnsureRunning();
        if (OpenTurn is not null) throw new InvalidOperationException("The previous turn has not been answered yet.");
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("A turn needs a question.", nameof(question));
        var turn = new Turn
        {
            Index = _turns.Count,
            ConceptId = conceptId,
            Move = move,
            Question = question,
            AskedAt = askedAt,
            TargetClaimIds = targetClaimIds ?? [],
            Reason = reason,
            ObjectiveKey = objectiveKey,
            IsReplacement = isReplacement,
            Novelty = move == MoveKind.Apply ? novelty : null,
            QuestionAuthor = author,
        };
        _turns.Add(turn);
        return turn;
    }

    /// <summary>Records the student's answer to the open turn.</summary>
    public Turn RecordAnswer(Answer answer)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(answer);
        var open = OpenTurn ?? throw new InvalidOperationException("There is no open turn to answer.");
        // The first answer releases a held clock where it stands (counting from StartedAt); it can no longer be restarted.
        ClockHeld = false;
        // An answer means the microphone works again, so an open pause ends. The answer's times are the browser's clock, so none of
        // the open pause is added back here; the server resumes the clock with its own time first (GAD-T263).
        if (PausedSince is { } since) ResumeClock(since);
        return Replace(open with { Answer = answer });
    }

    /// <summary>Records the judgment of the last turn's answer and the concept's new mastery estimate.</summary>
    public Turn RecordJudgment(AnswerJudgment judgment, double masteryEstimate, ResponseAnalysis? analysis = null)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(judgment);
        var open = AwaitingJudgment ?? throw new InvalidOperationException("There is no answered, unjudged turn to judge.");
        var snapshot = new MasterySnapshot(open.ConceptId, Math.Clamp(masteryEstimate, 0, 1), TurnsOn(open.ConceptId));
        _mastery[open.ConceptId] = snapshot;
        return Replace(open with { Judgment = judgment, MasteryAfter = snapshot, Analysis = analysis ?? open.Analysis });
    }

    /// <summary>
    /// Records the live turn's provisional judgment of the last turn's answer and the concept's new mastery estimate from it
    /// (GAD-T508). The turn then counts as judged for planning on, while its full panel judgment is still to come
    /// (<see cref="PendingFullJudgments"/>).
    /// </summary>
    public Turn RecordProvisional(ProvisionalJudgment provisional, double masteryEstimate)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(provisional);
        var open = AwaitingJudgment ?? throw new InvalidOperationException("There is no answered, unjudged turn to judge.");
        var snapshot = new MasterySnapshot(open.ConceptId, Math.Clamp(masteryEstimate, 0, 1), TurnsOn(open.ConceptId));
        _mastery[open.ConceptId] = snapshot;
        return Replace(open with { Provisional = provisional, MasteryAfter = snapshot });
    }

    /// <summary>
    /// Records the full panel judgment of a turn that was judged provisionally (GAD-T508), which may no longer be the last turn.
    /// The provisional record is kept, updated with how the two compared. The concept's mastery is then replayed over its turns
    /// in order (<see cref="RecomputeMastery"/>), so a later turn's estimate builds on the full judgment, as if it had come first.
    /// </summary>
    public Turn RecordFullJudgment(int turnIndex, AnswerJudgment judgment, ResponseAnalysis? analysis, ProvisionalJudgment? provisional)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(judgment);
        if (turnIndex < 0 || turnIndex >= _turns.Count) throw new ArgumentOutOfRangeException(nameof(turnIndex));
        var turn = _turns[turnIndex];
        if (turn.Answer is null || turn.Judgment is not null || turn.Provisional is null)
            throw new InvalidOperationException($"Turn {turnIndex} is not waiting for its full judgment.");
        Replace(turn with { Judgment = judgment, Analysis = analysis ?? turn.Analysis, Provisional = provisional ?? turn.Provisional });
        RecomputeMastery(turn.ConceptId);
        return _turns[turnIndex];
    }

    /// <summary>
    /// Replays a concept's mastery over its answered turns in order (GAD-T508), each from its steering judgment (<see cref="SteeringJudgment"/>),
    /// with the same update the live step applies: an unjudged or recited answer leaves the estimate where it was.
    /// </summary>
    public void RecomputeMastery(string conceptId)
    {
        var prior = MasteryModel.Prior;
        var judgedBefore = 0;
        MasterySnapshot? last = null;
        foreach (var t in _turns.Where(t => t.ConceptId == conceptId && t.Answer is not null).ToList())
        {
            if (SteeringJudgment(t) is not { } j) continue;
            var unjudged = ObjectiveAssessor.IsUnjudged(j);
            var estimate = unjudged || j.Recitation is not null ? prior
                : MasteryModel.Update(prior, j.OverallScore, MasteryModel.QuestionDifficulty(t.Move, Policy.Difficulty), judgedBefore);
            last = new MasterySnapshot(conceptId, Math.Clamp(estimate, 0, 1), _turns.Count(x => x.ConceptId == conceptId && x.Index <= t.Index));
            Replace(t with { MasteryAfter = last });
            prior = last.Estimate;
            if (!unjudged) judgedBefore++;
        }
        if (last is not null) _mastery[conceptId] = last;
    }

    /// <summary>
    /// Records that turn <paramref name="turnIndex"/>'s question (or a clarification on it) revealed the answer
    /// (GAD-T92). The first flag on a turn stands; later ones are ignored. Allowed after the session ended,
    /// because a leak can be found in review: answers on that objective from this turn on become contaminated.
    /// Returns false when the turn was already flagged.
    /// </summary>
    public bool FlagLeak(int turnIndex, LeakFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);
        if (turnIndex < 0 || turnIndex >= _turns.Count) throw new ArgumentOutOfRangeException(nameof(turnIndex));
        var turn = _turns[turnIndex];
        if (turn.Leak is not null) return false;
        Replace(turn with { Leak = flag });
        return true;
    }

    /// <summary>
    /// Records that turn <paramref name="turnIndex"/>'s spoken answer may have been cut off by the end-of-answer silence (GAD-T462):
    /// the student spoke again <paramref name="resumeSeconds"/> after it closed. Only the answer's speech metrics change, never its
    /// text, judgment or mastery; a turn not watched for cut-offs (<see cref="AnswerCutOff.Mark"/>) or already marked is left as it
    /// is. Returns true when the mark was added.
    /// </summary>
    public bool NoteAnswerCutOff(int turnIndex, double resumeSeconds)
    {
        if (turnIndex < 0 || turnIndex >= _turns.Count) return false;
        var turn = _turns[turnIndex];
        if (turn.Answer is not { Mode: "voice" } answer || AnswerCutOff.Mark(answer.SpeechMetrics, resumeSeconds) is not { } marked) return false;
        Replace(turn with { Answer = answer with { SpeechMetrics = marked } });
        return true;
    }

    /// <summary>
    /// Records that the open question was said another way at the student's request (GAD-T555); only its rephrasings change.
    /// Returns false when no question is open.
    /// </summary>
    public bool RecordRephrasing(string text)
    {
        if (OpenTurn is not { } open || string.IsNullOrWhiteSpace(text)) return false;
        Replace(open with { Rephrasings = [.. open.Rephrasings, text] });
        return true;
    }

    /// <summary>Ends the session. An unanswered open turn is kept as asked.</summary>
    public void End(EndReason reason, DateTimeOffset endedAt)
    {
        EnsureRunning();
        if (reason == EndReason.None) throw new ArgumentException("Give a real end reason.", nameof(reason));
        if (PausedSince is not null) ResumeClock(endedAt);
        if (HeldSince is not null) Release(endedAt);
        EndReason = reason;
        EndedAt = endedAt;
    }

    /// <summary>
    /// Puts the answer as finally submitted (its timings, words and speech measurements) on a turn that was answered, judged and
    /// followed up speculatively from the same words (GAD-T497): the words must be the same (<see cref="SameWords"/>; case,
    /// punctuation and spacing may differ), so nothing judged changes.
    /// </summary>
    public Turn ReplaceAnswerDetails(int turnIndex, Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (turnIndex < 0 || turnIndex >= _turns.Count) throw new ArgumentOutOfRangeException(nameof(turnIndex));
        var turn = _turns[turnIndex];
        if (turn.Answer is null || !SameWords(turn.Answer.Text, answer.Text))
            throw new InvalidOperationException("Only the details of the same answer can be replaced.");
        return Replace(turn with { Answer = answer });
    }

    /// <summary>The words of an answer, for comparing two transcripts of it: lower case, letters, digits and apostrophes, single spaces.</summary>
    public static string Words(string? text)
    {
        const char Apostrophe = (char)0x27, RightQuote = (char)0x2019;
        var b = new System.Text.StringBuilder((text ?? "").Length);
        var space = false;
        foreach (var ch in (text ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == Apostrophe || ch == RightQuote)
            {
                if (space && b.Length > 0) b.Append(' ');
                b.Append(ch == RightQuote ? Apostrophe : ch);
                space = false;
            }
            else space = true;
        }
        return b.ToString();
    }

    /// <summary>True when two transcripts say the same words (<see cref="Words"/>).</summary>
    public static bool SameWords(string? a, string? b) => string.Equals(Words(a), Words(b), StringComparison.Ordinal);

    private Turn Replace(Turn updated)
    {
        _turns[updated.Index] = updated;
        return updated;
    }

    private void EnsureRunning()
    {
        if (IsEnded) throw new InvalidOperationException($"Session {Id} has ended ({EndReason}).");
    }
}
