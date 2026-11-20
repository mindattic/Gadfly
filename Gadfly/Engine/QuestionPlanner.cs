namespace Gadfly.Engine;

/// <summary>A concept the planner can examine, with the concepts it relates to (for Synthesize moves).</summary>
public sealed record PlannerConcept(string Id, IReadOnlyList<string>? RelatedConceptIds = null);

/// <summary>A pair of the student's own claims that conflict, found by the claim ledger.</summary>
public sealed record ContradictionCandidate(string ConceptId, string FirstClaimId, string SecondClaimId);

/// <summary>Where a concept stands in the session.</summary>
public enum ConceptStatus { NotStarted, Open, Mastered, Aporia, Exhausted }

/// <summary>The planner's decision: the next move, or the end of the session.</summary>
public sealed record PlanDecision
{
    public EndReason End { get; init; } = EndReason.None;
    public string? ConceptId { get; init; }
    public MoveKind Move { get; init; }
    public IReadOnlyList<string> TargetClaimIds { get; init; } = [];
    /// <summary>Second concept for a Synthesize move.</summary>
    public string? RelatedConceptId { get; init; }
    /// <summary>Why the planner chose this, for the audit trail and the question generator.</summary>
    public string Reason { get; init; } = "";
    public bool EndsSession => End != EndReason.None;

    public static PlanDecision Ending(EndReason reason, string why) => new() { End = reason, Reason = why };
}

/// <summary>
/// Chooses each turn's concept and move from the session so far. Deterministic: the same session,
/// concepts, contradictions and time always give the same decision (the rules use no randomness),
/// so a session can be replayed for audit.
/// </summary>
public sealed class QuestionPlanner
{
    /// <summary>Below this score an answer is treated as unclear enough to ask for clarification first.</summary>
    public const double ClarifyBelow = 0.35;
    /// <summary>At or above this score the planner pushes harder (counterexample, apply).</summary>
    public const double PushAbove = 0.7;

    private readonly IReadOnlyList<PlannerConcept> _concepts;

    public QuestionPlanner(IReadOnlyList<PlannerConcept> concepts)
    {
        if (concepts.Count == 0) throw new ArgumentException("The planner needs at least one concept.", nameof(concepts));
        if (concepts.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != concepts.Count)
            throw new ArgumentException("Concept ids must be unique.", nameof(concepts));
        _concepts = concepts;
    }

    /// <summary>The status of <paramref name="conceptId"/> in <paramref name="session"/>.</summary>
    public static ConceptStatus StatusOf(SocraticSession session, string conceptId)
    {
        var turns = session.Turns.Where(t => t.ConceptId == conceptId && t.Judgment is not null).ToList();
        if (turns.Count == 0)
            return session.Turns.Any(t => t.ConceptId == conceptId) ? ConceptStatus.Open : ConceptStatus.NotStarted;
        var p = session.Policy;
        var estimate = session.Mastery.TryGetValue(conceptId, out var m) ? m.Estimate : MasteryModel.Prior;
        if (estimate >= p.MasteryThreshold && turns.Count >= 2) return ConceptStatus.Mastered;
        if (estimate <= p.AporiaThreshold && turns.Count >= p.MinTurnsBeforeAporia) return ConceptStatus.Aporia;
        if (session.TurnsOn(conceptId) >= p.MaxTurnsPerConcept) return ConceptStatus.Exhausted;
        return ConceptStatus.Open;
    }

    /// <summary>Decides the next move.</summary>
    public PlanDecision Next(SocraticSession session, DateTimeOffset now, IReadOnlyList<ContradictionCandidate>? contradictions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsEnded) return PlanDecision.Ending(session.EndReason, "The session has already ended.");
        if (session.OpenTurn is not null)
            throw new InvalidOperationException("Judge the open turn before planning the next one.");

        var p = session.Policy;
        if (session.Remaining(now) <= TimeSpan.Zero) return PlanDecision.Ending(EndReason.TimeUp, "The time budget is spent.");
        if (session.Turns.Count >= p.MaxTurns) return PlanDecision.Ending(EndReason.TurnLimit, "The turn limit is reached.");

        var ordered = Ordered(p);
        var statuses = ordered.ToDictionary(c => c.Id, c => StatusOf(session, c.Id), StringComparer.Ordinal);

        // Stay on the concept in progress until it closes.
        var current = session.Turns.Count > 0 ? session.Turns[^1].ConceptId : null;
        if (current is not null && statuses.TryGetValue(current, out var st) && st == ConceptStatus.Open)
            return FollowUp(session, current, contradictions);

        // Synthesize: two mastered, related concepts not yet connected, once enough time is left.
        var synth = TrySynthesize(session, ordered, statuses, now);
        if (synth is not null) return synth;

        var next = ordered.FirstOrDefault(c => statuses[c.Id] == ConceptStatus.NotStarted);
        if (next is not null)
            return new PlanDecision { ConceptId = next.Id, Move = MoveKind.Elicit, Reason = "Opening the next concept." };

        var closed = statuses.Values.ToList();
        if (closed.All(s => s == ConceptStatus.Mastered))
            return PlanDecision.Ending(EndReason.Mastery, "Every concept reached the mastery threshold.");
        if (closed.Any(s => s == ConceptStatus.Aporia) && closed.All(s => s is ConceptStatus.Mastered or ConceptStatus.Aporia))
            return PlanDecision.Ending(EndReason.Aporia, "Every concept closed; at least one in aporia.");
        return PlanDecision.Ending(EndReason.TurnLimit, "Every concept closed; some used their full turn budget.");
    }

    private IReadOnlyList<PlannerConcept> Ordered(SessionPolicy p)
    {
        if (p.ConceptIds.Count == 0) return _concepts;
        var byId = _concepts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var unknown = p.ConceptIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0) throw new InvalidOperationException($"The policy names unknown concepts: {string.Join(", ", unknown)}.");
        return p.ConceptIds.Select(id => byId[id]).ToList();
    }

    private static PlanDecision FollowUp(SocraticSession session, string conceptId, IReadOnlyList<ContradictionCandidate>? contradictions)
    {
        var onConcept = session.Turns.Where(t => t.ConceptId == conceptId).ToList();
        var last = onConcept[^1];
        var lastScore = last.Judgment?.OverallScore ?? 0;

        // 1. The elenchus first: a contradiction not yet put to the student.
        var confronted = new HashSet<string>(
            session.Turns.Where(t => t.Move == MoveKind.Contradiction).SelectMany(t => t.TargetClaimIds), StringComparer.Ordinal);
        var pending = contradictions?.FirstOrDefault(c => c.ConceptId == conceptId
            && !(confronted.Contains(c.FirstClaimId) && confronted.Contains(c.SecondClaimId)));
        if (pending is not null)
            return new PlanDecision
            {
                ConceptId = conceptId, Move = MoveKind.Contradiction,
                TargetClaimIds = [pending.FirstClaimId, pending.SecondClaimId],
                Reason = "Two of the student's own claims conflict.",
            };

        // 2. An unclear answer: clarify, but never twice in a row.
        if (lastScore < ClarifyBelow && last.Move != MoveKind.Clarify)
            return new PlanDecision { ConceptId = conceptId, Move = MoveKind.Clarify, Reason = "The last answer was unclear or off target." };

        // 3. A strong answer: push harder with the hardest move not yet used on this concept.
        if (lastScore >= PushAbove)
        {
            var used = onConcept.Select(t => t.Move).ToHashSet();
            foreach (var move in new[] { MoveKind.Counterexample, MoveKind.Apply })
                if (!used.Contains(move))
                    return new PlanDecision { ConceptId = conceptId, Move = move, Reason = "A strong answer; testing whether the understanding holds up." };
            return new PlanDecision { ConceptId = conceptId, Move = MoveKind.Apply, Reason = "A strong answer; another transfer case." };
        }

        // 4. Otherwise probe the reasoning.
        return new PlanDecision { ConceptId = conceptId, Move = MoveKind.Probe, Reason = "Probing the reasoning behind the answer." };
    }

    private static PlanDecision? TrySynthesize(SocraticSession session, IReadOnlyList<PlannerConcept> ordered,
        IReadOnlyDictionary<string, ConceptStatus> statuses, DateTimeOffset now)
    {
        var p = session.Policy;
        // Leave room: only when a quarter of the budget remains and at least two turns are allowed.
        if (session.Remaining(now) < p.TimeBudget / 4 || p.MaxTurns - session.Turns.Count < 2) return null;
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in session.Turns.Where(t => t.Move == MoveKind.Synthesize))
            done.Add(t.ConceptId);
        foreach (var c in ordered)
        {
            if (statuses[c.Id] != ConceptStatus.Mastered || done.Contains(c.Id)) continue;
            var partner = c.RelatedConceptIds?.FirstOrDefault(r => statuses.TryGetValue(r, out var s) && s == ConceptStatus.Mastered);
            if (partner is null) continue;
            // Only once per concept, and only before any unstarted concept would be starved of time.
            if (statuses.Values.Any(s => s == ConceptStatus.NotStarted) && session.Remaining(now) < p.TimeBudget / 2) return null;
            return new PlanDecision
            {
                ConceptId = c.Id, RelatedConceptId = partner, Move = MoveKind.Synthesize,
                Reason = "Both concepts are mastered; testing how the student connects them.",
            };
        }
        return null;
    }
}
