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
        IEnumerable<Turn> turns, IEnumerable<MasterySnapshot> mastery)
    {
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
    public DateTimeOffset StartedAt { get; }
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

    /// <summary>Time left in the budget at <paramref name="now"/> (never negative).</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        var left = Policy.TimeBudget - (now - StartedAt);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Turns asked so far on <paramref name="conceptId"/>.</summary>
    public int TurnsOn(string conceptId) => _turns.Count(t => t.ConceptId == conceptId);

    /// <summary>Asks a new question. The previous turn must be answered first.</summary>
    public Turn Ask(string conceptId, MoveKind move, string question, DateTimeOffset askedAt, IReadOnlyList<string>? targetClaimIds = null)
    {
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
        return Replace(open with { Answer = answer });
    }

    /// <summary>Records the judgment of the last turn's answer and the concept's new mastery estimate.</summary>
    public Turn RecordJudgment(AnswerJudgment judgment, double masteryEstimate)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(judgment);
        var open = AwaitingJudgment ?? throw new InvalidOperationException("There is no answered, unjudged turn to judge.");
        var snapshot = new MasterySnapshot(open.ConceptId, Math.Clamp(masteryEstimate, 0, 1), TurnsOn(open.ConceptId));
        _mastery[open.ConceptId] = snapshot;
        return Replace(open with { Judgment = judgment, MasteryAfter = snapshot });
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
