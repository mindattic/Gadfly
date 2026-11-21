using System.Globalization;
using Gadfly.Assessment;

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
    /// <summary>The concept the question is grounded in.</summary>
    public string? ConceptId { get; init; }
    /// <summary>The learning objective the question examines (GAD-T89).</summary>
    public string? ObjectiveKey { get; init; }
    public MoveKind Move { get; init; }
    public IReadOnlyList<string> TargetClaimIds { get; init; } = [];
    /// <summary>Second concept for a Synthesize move.</summary>
    public string? RelatedConceptId { get; init; }
    /// <summary>This question replaces one that revealed the answer (GAD-T92).</summary>
    public bool IsReplacement { get; init; }
    /// <summary>Why the planner chose this, for the audit trail and the question generator.</summary>
    public string Reason { get; init; } = "";
    public bool EndsSession => End != EndReason.None;

    public static PlanDecision Ending(EndReason reason, string why) => new() { End = reason, Reason = why };
}

/// <summary>
/// The examination control engine (GAD-T89, Component G) and question selection (GAD-T90, Part 2.4). It
/// examines learning objectives one at a time; for the current objective it keeps competing hypotheses about
/// the student's level (the corroborated level, the tentative top, the plausible ceiling) and picks the
/// questioning strategy whose answer best tells them apart, avoiding repetition and respecting difficulty and
/// remaining time. An objective closes when its evidence is sufficient (corroborated, consistent, ceiling
/// tested) or when its limits are hit, in which case it is reported as Level 0, Insufficient Evidence.
/// Deterministic: the same session, contradictions and time always give the same decision, so a session can
/// be replayed for audit.
/// </summary>
public sealed class QuestionPlanner
{
    /// <summary>Below this score (with no panel analysis) an answer is treated as unclear enough to ask for clarification first.</summary>
    public const double ClarifyBelow = 0.35;
    /// <summary>At or above this score an answer is strong: uncertainties about it are not worth a clarification.</summary>
    public const double PushAbove = 0.7;
    /// <summary>Assumed seconds per turn before any turn has been timed.</summary>
    public static readonly TimeSpan DefaultTurnDuration = TimeSpan.FromSeconds(90);

    private readonly IReadOnlyList<PlannerConcept> _concepts;
    private readonly Dictionary<string, PlannerConcept> _byId;

    public QuestionPlanner(IReadOnlyList<PlannerConcept> concepts)
    {
        if (concepts.Count == 0) throw new ArgumentException("The planner needs at least one concept.", nameof(concepts));
        if (concepts.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != concepts.Count)
            throw new ArgumentException("Concept ids must be unique.", nameof(concepts));
        _concepts = concepts;
        _byId = concepts.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The status of <paramref name="conceptId"/> in <paramref name="session"/>, derived from the objective it
    /// grounds: Sufficient at Level 3+ reads as Mastered, Sufficient below as Aporia, Inconclusive as Exhausted.
    /// </summary>
    public static ConceptStatus StatusOf(SocraticSession session, string conceptId)
    {
        var objectives = ExamObjectives.Resolve(session.Policy, [conceptId]);
        var mine = objectives.Where(o => o.ConceptKeys.Contains(conceptId, StringComparer.Ordinal) || o.Key == conceptId).ToList();
        if (mine.Count == 0) return ConceptStatus.NotStarted;
        var statuses = mine.Select(o => ObjectiveAssessor.Assess(session, o)).ToList();
        if (statuses.All(a => a.Status == ObjectiveStatus.NotStarted)) return ConceptStatus.NotStarted;
        if (statuses.Any(a => a.Status is ObjectiveStatus.Open or ObjectiveStatus.NotStarted)) return ConceptStatus.Open;
        if (statuses.Any(a => a.Status == ObjectiveStatus.Inconclusive)) return ConceptStatus.Exhausted;
        return statuses.All(a => a.Level >= MasteryLevel.ConceptualUnderstanding) ? ConceptStatus.Mastered : ConceptStatus.Aporia;
    }

    /// <summary>The objectives this planner examines in <paramref name="session"/> (validated against its concepts).</summary>
    public IReadOnlyList<ExamObjective> ObjectivesFor(SocraticSession session)
    {
        var p = session.Policy;
        if (p.Objectives.Count == 0)
        {
            var unknown = p.ConceptIds.Where(id => !_byId.ContainsKey(id)).ToList();
            if (unknown.Count > 0) throw new InvalidOperationException($"The policy names unknown concepts: {string.Join(", ", unknown)}.");
            return ExamObjectives.Resolve(p, _concepts.Select(c => c.Id));
        }
        foreach (var o in p.Objectives)
        {
            if (o.ConceptKeys.Count == 0)
                throw new InvalidOperationException($"Objective '{o.Key}' is grounded in no concept; resolve its concepts before starting.");
            var unknown = o.ConceptKeys.Where(k => !_byId.ContainsKey(k)).ToList();
            if (unknown.Count > 0)
                throw new InvalidOperationException($"Objective '{o.Key}' names unknown concepts: {string.Join(", ", unknown)}.");
        }
        return p.Objectives;
    }

    /// <summary>The assessment of every objective right now.</summary>
    public IReadOnlyList<ObjectiveAssessment> Assess(SocraticSession session, IReadOnlyList<ContradictionCandidate>? contradictions = null) =>
        ObjectiveAssessor.AssessAll(session, ObjectivesFor(session), contradictions);

    /// <summary>Decides the next move.</summary>
    public PlanDecision Next(SocraticSession session, DateTimeOffset now, IReadOnlyList<ContradictionCandidate>? contradictions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsEnded) return PlanDecision.Ending(session.EndReason, "The session has already ended.");
        if (session.OpenTurn is not null || session.AwaitingJudgment is not null)
            throw new InvalidOperationException("Answer and judge the last turn before planning the next one.");

        var p = session.Policy;
        if (session.Remaining(now) <= TimeSpan.Zero) return PlanDecision.Ending(EndReason.TimeUp, "The time budget is spent.");
        if (session.Turns.Count >= p.MaxTurns) return PlanDecision.Ending(EndReason.TurnLimit, "The turn limit is reached.");

        var objectives = ObjectivesFor(session);
        var assessments = objectives.ToDictionary(o => o.Key, o => ObjectiveAssessor.Assess(session, o, contradictions), StringComparer.Ordinal);
        var notStarted = objectives.Count(o => assessments[o.Key].Status == ObjectiveStatus.NotStarted);
        var capacity = Capacity(session, now);

        // Stay on the objective in progress until its evidence is sufficient or its limits are hit, but leave
        // every unstarted objective room for a corroborated conclusion.
        var current = session.Turns.Count > 0 ? ExamObjectives.KeyOf(session.Turns[^1]) : null;
        if (current is not null && assessments.TryGetValue(current, out var a) && a.Status == ObjectiveStatus.Open)
        {
            if (notStarted == 0 || capacity > notStarted * p.MinCorroboratingAnswers)
                return FollowUp(session, objectives.First(o => o.Key == current), a, contradictions);
        }

        var synth = TrySynthesize(session, objectives, assessments, now, capacity, notStarted);
        if (synth is not null) return synth;

        var next = objectives.FirstOrDefault(o => assessments[o.Key].Status == ObjectiveStatus.NotStarted);
        if (next is not null)
        {
            var leaving = current is not null && assessments.TryGetValue(current, out var cur) && cur.Status == ObjectiveStatus.Open
                ? $" Leaving '{assessments[current].Title}' inconclusive to keep time for {notStarted} more objective(s)." : "";
            return new PlanDecision
            {
                ObjectiveKey = next.Key, ConceptId = GroundingConcept(session, next), Move = MoveKind.Elicit,
                Reason = $"Opening objective '{next.Title}': an explanation in the student's own words.{leaving}",
            };
        }

        var closed = objectives.Select(o => assessments[o.Key]).ToList();
        var sufficient = closed.Count(x => x.Status == ObjectiveStatus.Sufficient);
        return PlanDecision.Ending(EndReason.EvidenceComplete,
            $"Every objective closed: {sufficient} with sufficient evidence, {closed.Count - sufficient} inconclusive (Level 0, Insufficient Evidence).");
    }

    /// <summary>How many more questions fit: the lower of the turn limit and the time left at the session's pace so far.</summary>
    public static int Capacity(SocraticSession session, DateTimeOffset now)
    {
        var p = session.Policy;
        var byTurns = p.MaxTurns - session.Turns.Count;
        var asked = session.Turns.Count;
        var pace = asked == 0 ? DefaultTurnDuration : TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(15).Ticks, (now - session.StartedAt).Ticks / asked));
        var byTime = (int)Math.Floor(session.Remaining(now) / pace);
        return Math.Max(0, Math.Min(byTurns, byTime));
    }

    /// <summary>The objective's concept with the fewest questions so far (in order), so grounding rotates.</summary>
    public static string GroundingConcept(SocraticSession session, ExamObjective objective)
    {
        var keys = objective.ConceptKeys.Count > 0 ? objective.ConceptKeys : [objective.Key];
        return keys.OrderBy(k => session.Turns.Count(t => ExamObjectives.KeyOf(t) == objective.Key && t.ConceptId == k))
            .ThenBy(k => keys.ToList().IndexOf(k)).First();
    }

    private PlanDecision FollowUp(SocraticSession session, ExamObjective o, ObjectiveAssessment a, IReadOnlyList<ContradictionCandidate>? contradictions)
    {
        var p = session.Policy;
        var onObjective = ObjectiveAssessor.TurnsOn(session, o.Key);
        var last = onObjective[^1];
        var lastScore = last.Judgment?.OverallScore ?? 0;
        var lastAporia = last.Judgment is { } lj && ObjectiveAssessor.IsAporia(lj);
        PlanDecision Decide(MoveKind move, string reason, bool replacement = false, IReadOnlyList<string>? targets = null, string? concept = null) => new()
        {
            ObjectiveKey = o.Key, ConceptId = concept ?? GroundingConcept(session, o), Move = move, Reason = reason,
            IsReplacement = replacement, TargetClaimIds = targets ?? [],
        };

        // 1. The elenchus: a contradiction between the student's own claims not yet put to them, never twice running.
        var concepts = new HashSet<string>(o.ConceptKeys.Count > 0 ? o.ConceptKeys : [o.Key], StringComparer.Ordinal);
        // Confronted per claim pair, as the assessor counts it: one turn that targets both claims.
        var confronting = session.Turns.Where(t => t.Move == MoveKind.Contradiction).ToList();
        var pending = last.Move == MoveKind.Contradiction ? null : contradictions?.FirstOrDefault(c => concepts.Contains(c.ConceptId)
            && !confronting.Any(t => t.TargetClaimIds.Contains(c.FirstClaimId) && t.TargetClaimIds.Contains(c.SecondClaimId)));
        if (pending is not null)
            return Decide(MoveKind.Contradiction, "Two of the student's own claims conflict: consistency must be settled before any level is stated.",
                targets: [pending.FirstClaimId, pending.SecondClaimId], concept: pending.ConceptId);

        // 2. A leaked question: what followed it is contaminated, so ask a replacement that needs reasoning the leak did not give.
        var leak = ObjectiveAssessor.FirstLeak(session, o.Key);
        if (leak is not null)
        {
            var (move, value, _) = Best(session, o, a, ObjectiveAssessor.LeakResistantMoves);
            var leaked = session.Turns[leak!.Value];
            return Decide(move, $"Turn {leak + 1}'s question was found to reveal expected points ({leaked.Leak!.Source}: {leaked.Leak.Reason}); " +
                $"answers since are contaminated. Replacement question: {MoveProfiles.Tests(move)} (value {F(value)}).", replacement: true);
        }

        // 3. Learning Mode: after a weak answer or a misconception, a hint, then a guided explanation, then reassessment.
        if (p.Mode == AssessmentMode.Learning && !MoveProfiles.IsAssistance(last.Move) && a.AssistsGiven < p.MaxAssistsPerObjective)
        {
            var misconception = last.Analysis?.Misconceptions.FirstOrDefault(m => m.ObjectiveKey == o.Key);
            if (lastScore < ObjectiveAssessor.NegativeBelow || misconception is not null)
            {
                var move = a.AssistsGiven == 0 ? MoveKind.Hint : MoveKind.GuidedExplanation;
                var why = misconception is not null ? $"a misconception ({misconception.Text})" : lastAporia ? "an honest \"I don't know\"" : "a weak answer";
                return Decide(move, $"Learning Mode: after {why}, {(move == MoveKind.Hint ? "a hint" : "a guided explanation")}; what follows is assisted evidence.");
            }
        }

        // 4. An unclear answer: a neutral clarification (Scenario C), never twice running, at most twice per objective.
        var clarifications = onObjective.Count(t => t.Move == MoveKind.Clarify);
        if (last.Move != MoveKind.Clarify && !lastAporia && clarifications < 2 && !MoveProfiles.IsAssistance(last.Move))
        {
            // Only when the meaning could change the judgment: a clearly wrong answer needs no clarification, nor a strong one.
            if (last.Analysis is { IsValid: true } an && an.Uncertainties.Count > 0 && lastScore >= ObjectiveAssessor.NegativeBelow && lastScore < PushAbove)
                return Decide(MoveKind.Clarify, $"The answer's meaning is uncertain ({an.Uncertainties[0]}); asking a neutral clarification before judging further.");
            if (last.Analysis is not { IsValid: true } && lastScore < ClarifyBelow && last.Judgment is { } j && !ObjectiveAssessor.IsUnjudged(j))
                return Decide(MoveKind.Clarify, "The last answer was unclear or off target; asking a neutral clarification.");
        }

        // 5. A possible misconception: test it before anything else (counterexample or assumption testing).
        if (a.UntestedMisconceptions.Count > 0 && last.Move is not (MoveKind.Counterexample or MoveKind.AssumptionTesting))
        {
            var (move, value, _) = Best(session, o, a, [MoveKind.Counterexample, MoveKind.AssumptionTesting]);
            return Decide(move, $"Possible misconception ({a.UntestedMisconceptions[0]}); {move} tests {MoveProfiles.Tests(move)} " +
                $"to tell a held misconception from a slip (value {F(value)}).");
        }

        // 6. Information value: the strategy that best separates the competing hypotheses about the level.
        var (best, bestValue, why2) = Best(session, o, a, MoveProfiles.Strategies);
        return Decide(best, why2);
    }

    /// <summary>
    /// Scores each candidate move by how well its answer would tell the competing level hypotheses apart, and
    /// returns the best with the reason. Ties go to the earlier move in <paramref name="candidates"/>.
    /// </summary>
    internal static (MoveKind Move, double Value, string Reason) Best(SocraticSession session, ExamObjective o, ObjectiveAssessment a,
        IReadOnlyList<MoveKind> candidates)
    {
        var p = session.Policy;
        var onObjective = ObjectiveAssessor.TurnsOn(session, o.Key);
        var last = onObjective[^1];
        int target;
        string hypothesis;
        var shown = a.CorroboratedLevel;
        var high = a.TentativeHigh;
        var ceiling = a.PlausibleCeiling;
        if (MoveProfiles.IsAssistance(last.Move))
        {
            var failed = onObjective.LastOrDefault(t => t.Index < last.Index && MoveProfiles.ProbedLevel(t.Move) > 0);
            target = Math.Max(2, failed is null ? 3 : MoveProfiles.ProbedLevel(failed.Move));
            hypothesis = $"reassessing level {target} after assistance";
        }
        else if (shown < high)
        {
            target = high >= ceiling ? high : (high + ceiling + 1) / 2;
            hypothesis = high > ceiling ? $"conflicting answers around level {high}: re-testing it"
                : high == ceiling ? $"level {high} was shown once (one answer never establishes it): corroborating it"
                : $"level {high} was shown once, not corroborated (one answer never establishes it); distinguishing level {high} from {ceiling}";
        }
        else if (ceiling > shown)
        {
            target = (shown + 1 + ceiling + 1) / 2;
            hypothesis = shown <= 1 && high <= 1
                ? $"nothing above level 1 shown yet; distinguishing level 2 from {ceiling}"
                : $"level {shown} is corroborated; distinguishing level {shown + 1} from {ceiling}";
        }
        else
        {
            target = Math.Min(5, shown + 1);
            hypothesis = $"level {shown} is corroborated and level {shown + 1} failed once; checking the ceiling again before concluding";
        }

        var evidenced = new HashSet<AssessmentDimension>(onObjective.Where(t => t.Judgment is not null && t.Analysis is not null)
            .SelectMany(t => t.Analysis!.Evidence.Where(e => e.ObjectiveKey == o.Key && e.Polarity != EvidencePolarity.Uncertain).Select(e => e.Dimension)));
        foreach (var t in onObjective.Where(t => t.Judgment is not null))
            foreach (var d in MoveProfiles.Dimensions(t.Move)) evidenced.Add(d);
        var gaps = o.Dimensions.Where(d => !evidenced.Contains(d)).ToHashSet();
        var misconception = a.UntestedMisconceptions.FirstOrDefault();

        var scored = new List<(MoveKind Move, double Value)>();
        foreach (var m in candidates)
        {
            var level = MoveProfiles.ProbedLevel(m);
            var used = onObjective.Count(t => t.Move == m);
            var v = 1.0 - 0.4 * Math.Abs(level - target);
            v -= 0.35 * used;
            if (m == last.Move) v -= 0.5;
            if (misconception is not null && m is MoveKind.Counterexample or MoveKind.AssumptionTesting) v += 0.6;
            if (MoveProfiles.Dimensions(m).Any(gaps.Contains)) v += 0.15;
            v += (p.Difficulty - 3) * 0.05 * (level - 3);
            scored.Add((m, Math.Round(v, 4)));
        }
        var ordered = scored.Select((s, i) => (s.Move, s.Value, i)).OrderByDescending(x => x.Value).ThenBy(x => x.i).ToList();
        var best = ordered[0];
        var runnerUp = ordered.Count > 1 ? $"; next best {ordered[1].Move} {F(ordered[1].Value)}" : "";
        var because = misconception is not null && best.Move is MoveKind.Counterexample or MoveKind.AssumptionTesting
            ? $"Possible misconception ({misconception}); {best.Move} tests {MoveProfiles.Tests(best.Move)}"
            : $"Hypotheses for '{o.Title}': {hypothesis}. {best.Move} tests {MoveProfiles.Tests(best.Move)} (level {MoveProfiles.ProbedLevel(best.Move)})";
        return (best.Move, best.Value, $"{because} (value {F(best.Value)}{runnerUp}).");
    }

    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private PlanDecision? TrySynthesize(SocraticSession session, IReadOnlyList<ExamObjective> objectives,
        IReadOnlyDictionary<string, ObjectiveAssessment> assessments, DateTimeOffset now, int capacity, int notStarted)
    {
        var p = session.Policy;
        // Leave room: only when a quarter of the budget remains, and never at the cost of an unstarted objective.
        if (session.Remaining(now) < p.EffectiveTimeBudget / 4 || p.MaxTurns - session.Turns.Count < 2) return null;
        if (capacity - 1 < notStarted * p.MinCorroboratingAnswers) return null;
        if (notStarted > 0 && session.Remaining(now) < p.EffectiveTimeBudget / 2) return null;
        var done = new HashSet<string>(session.Turns.Where(t => t.Move == MoveKind.Synthesize).Select(ExamObjectives.KeyOf), StringComparer.Ordinal);
        bool Strong(ExamObjective x) => assessments[x.Key] is { Status: ObjectiveStatus.Sufficient, Level: >= MasteryLevel.ConceptualUnderstanding };
        foreach (var o in objectives)
        {
            if (!Strong(o) || done.Contains(o.Key)) continue;
            foreach (var conceptKey in o.ConceptKeys.Count > 0 ? o.ConceptKeys : [o.Key])
            {
                if (!_byId.TryGetValue(conceptKey, out var concept)) continue;
                foreach (var related in concept.RelatedConceptIds ?? [])
                {
                    var partner = objectives.FirstOrDefault(x => x.Key != o.Key && Strong(x) && x.ConceptKeys.Contains(related, StringComparer.Ordinal));
                    // One connection question per pair: the partner's own Synthesize already asked about this link.
                    if (partner is null || done.Contains(partner.Key)) continue;
                    return new PlanDecision
                    {
                        ObjectiveKey = o.Key, ConceptId = conceptKey, RelatedConceptId = related, Move = MoveKind.Synthesize,
                        Reason = $"'{o.Title}' and '{partner.Title}' both show conceptual understanding; testing how the student connects them (synthesis, not transfer: only Apply probes transfer).",
                    };
                }
            }
        }
        return null;
    }
}
