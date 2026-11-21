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
        IEnumerable<Turn> turns, IEnumerable<MasterySnapshot> mastery, bool clockHeld = false)
    {
        ClockHeld = clockHeld;
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
    public DateTimeOffset? EndedAt { get; private set; }
    public EndReason EndReason { get; private set; } = EndReason.None;
    public bool IsEnded => EndedAt is not null;

    public IReadOnlyList<Turn> Turns => _turns;

    /// <summary>Latest mastery estimate per concept.</summary>
    public IReadOnlyDictionary<string, MasterySnapshot> Mastery => _mastery;

    /// <summary>The last turn, while it still waits for the student's answer.</summary>
    public Turn? OpenTurn => _turns.Count > 0 && _turns[^1].Answer is null ? _turns[^1] : null;

    /// <summary>The last turn, while it is answered but not yet judged. Subjects that don't judge (the
    /// personal introspection tool) leave every turn here; exam subjects judge before planning on.</summary>
    public Turn? AwaitingJudgment => _turns.Count > 0 && _turns[^1].Answer is not null && _turns[^1].Judgment is null ? _turns[^1] : null;

    /// <summary>Time left in the budget (with any accommodation multiplier) at <paramref name="now"/> (never negative).</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        var left = Policy.EffectiveTimeBudget - (now - StartedAt);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Turns asked so far on <paramref name="conceptId"/>.</summary>
    public int TurnsOn(string conceptId) => _turns.Count(t => t.ConceptId == conceptId);

    /// <summary>Asks a new question. The previous turn must be answered first.</summary>
    public Turn Ask(string conceptId, MoveKind move, string question, DateTimeOffset askedAt, IReadOnlyList<string>? targetClaimIds = null,
        string reason = "", string? objectiveKey = null, bool isReplacement = false)
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

    /// <summary>Ends the session. An unanswered open turn is kept as asked.</summary>
    public void End(EndReason reason, DateTimeOffset endedAt)
    {
        EnsureRunning();
        if (reason == EndReason.None) throw new ArgumentException("Give a real end reason.", nameof(reason));
        EndReason = reason;
        EndedAt = endedAt;
    }

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
