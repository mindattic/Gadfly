using Gadfly.Assessment;

namespace Gadfly.Engine;

/// <summary>Where an objective stands (GAD-T89).</summary>
public enum ObjectiveStatus
{
    NotStarted,
    /// <summary>Being examined; more evidence is needed.</summary>
    Open,
    /// <summary>Enough corroborated, consistent evidence to state a level.</summary>
    Sufficient,
    /// <summary>Closed without enough evidence (limits, time, or the session ended): reported as Level 0, Insufficient Evidence.</summary>
    Inconclusive,
}

/// <summary>How one judged answer reads for its objective.</summary>
public enum AnswerResult { Positive, Mixed, Negative }

/// <summary>One judged answer as evidence for its objective (a clarification is merged into the answer it clarifies).</summary>
public sealed record AnswerOutcome(int TurnIndex, MoveKind Move, int ProbedLevel, double Score, AnswerResult Result,
    bool Contaminated, bool Assisted, bool Aporia)
{
    /// <summary>Counts as independently demonstrated examination evidence.</summary>
    public bool Independent => !Contaminated && !Assisted;
}

/// <summary>
/// The examination control engine's view of one objective (GAD-T89, Part 6): what has been shown, how
/// corroborated and consistent it is, what is still uncertain, and whether the evidence is sufficient. The
/// reported <see cref="Level"/> is Level 0 (Insufficient Evidence) unless the evidence is sufficient; the
/// tentative level is kept separately for reviewers. Deterministic: the same session gives the same result.
/// </summary>
public sealed record ObjectiveAssessment
{
    public required string ObjectiveKey { get; init; }
    public string Title { get; init; } = "";
    public required ObjectiveStatus Status { get; init; }
    /// <summary>
    /// The level to report, from independent evidence only: Level 0 unless <see cref="Status"/> is Sufficient. In Learning
    /// Mode it can stay 0 while the objective is done, because what was shown after help is <see cref="AssistedLevel"/>.
    /// </summary>
    public required MasteryLevel Level { get; init; }
    /// <summary>The level the independent evidence so far points to (Level 0 below the corroboration minimum).</summary>
    public MasteryLevel ProvisionalLevel { get; init; }
    /// <summary>Learning Mode: the level including answers given after hints or explanations; null when none were given.</summary>
    public MasteryLevel? AssistedLevel { get; init; }
    public int Questions { get; init; }
    /// <summary>Follow-up questions after the opening one (GAD-T267): reported so thread length never silently becomes a score.</summary>
    public int FollowUps { get; init; }
    public int IndependentAnswers { get; init; }
    public int SupportingAnswers { get; init; }
    public int ContradictingAnswers { get; init; }
    public int ContaminatedAnswers { get; init; }
    public int AssistedAnswers { get; init; }
    public int AssistsGiven { get; init; }
    /// <summary>At least the policy's minimum of independent answers support the stated level.</summary>
    public bool Corroborated { get; init; }
    /// <summary>No unresolved contradiction between the student's claims on this objective.</summary>
    public bool Consistent { get; init; } = true;
    public bool ReasoningShown { get; init; }
    public bool TransferShown { get; init; }
    public bool CriticalReasoningShown { get; init; }
    /// <summary>Misconceptions not yet resolved by a later independent answer.</summary>
    public IReadOnlyList<string> UnresolvedMisconceptions { get; init; } = [];
    /// <summary>Misconceptions found but not yet tested with a counterexample or assumption question.</summary>
    public IReadOnlyList<string> UntestedMisconceptions { get; init; } = [];
    /// <summary>Uncertainties about the meaning of the latest answer on this objective.</summary>
    public IReadOnlyList<string> Uncertainties { get; init; } = [];
    /// <summary>Contradictions in the student's claims not yet put to the student.</summary>
    public int UnconfrontedContradictions { get; init; }
    /// <summary>Turns whose question (or a clarification on it) revealed the answer.</summary>
    public IReadOnlyList<int> LeakedTurns { get; init; } = [];
    /// <summary>Highest level any independent answer was positive for (the tentative top).</summary>
    public int TentativeHigh { get; init; } = 1;
    /// <summary>Highest level still plausible (below the lowest failed level above the tentative top).</summary>
    public int PlausibleCeiling { get; init; } = 5;
    /// <summary>Independent negative answers on levels above the corroborated level.</summary>
    public int FailuresAbove { get; init; }
    /// <summary>The corroborated level on the 1-5 ladder (before the Level 0 rule), for the planner.</summary>
    public int CorroboratedLevel { get; init; } = 1;
    /// <summary>Why the objective is (or is not yet) sufficiently evidenced, in plain words.</summary>
    public string Explanation { get; init; } = "";
    public IReadOnlyList<AnswerOutcome> Answers { get; init; } = [];

    public bool IsClosed => Status is ObjectiveStatus.Sufficient or ObjectiveStatus.Inconclusive;
}

/// <summary>One evidence item ready to persist (an AssessmentEvidence row), with its contamination and assistance flags.</summary>
public sealed record CollectedEvidence(int TurnIndex, string ObjectiveKey, EvidencePolarity Polarity, AssessmentDimension Dimension,
    string Quote, string Note, int Support, bool Assisted, bool Contaminated, MoveKind Move);

/// <summary>
/// Pure evidence-sufficiency rules (GAD-T89, Part 6; GAD-T92 contamination; GAD-T93 assisted evidence).
/// One answer never establishes mastery or its absence: a level needs <see cref="SessionPolicy.MinCorroboratingAnswers"/>
/// independent supporting answers, a conclusion needs that many independent answers in all, and the level's
/// ceiling must have been tested. Contaminated answers (after a leaked question) and assisted answers (after a
/// hint) never count as independent evidence.
/// </summary>
public static class ObjectiveAssessor
{
    /// <summary>An answer at or above this score supports the level its question probed.</summary>
    public const double PositiveAtOrAbove = 0.6;
    /// <summary>An answer below this score is evidence against the level its question probed.</summary>
    public const double NegativeBelow = 0.4;

    /// <summary>Moves whose answers need reasoning a leaked definition does not supply: a replacement question uses one.</summary>
    public static readonly IReadOnlyList<MoveKind> LeakResistantMoves =
        [MoveKind.ScenarioVariation, MoveKind.Apply, MoveKind.AssumptionTesting, MoveKind.Counterexample];

    /// <summary>Dimensions a replacement question can still show independently after a leak.</summary>
    private static readonly HashSet<AssessmentDimension> LeakResistantDimensions =
    [
        AssessmentDimension.Application, AssessmentDimension.KnowledgeTransfer,
        AssessmentDimension.LogicalReasoning, AssessmentDimension.MisconceptionRecognition,
    ];

    public static IReadOnlyList<ObjectiveAssessment> AssessAll(SocraticSession session, IReadOnlyList<ExamObjective> objectives,
        IReadOnlyList<ContradictionCandidate>? contradictions = null) =>
        objectives.Select(o => Assess(session, o, contradictions)).ToList();

    /// <summary>Turns on <paramref name="objectiveKey"/>, in order.</summary>
    public static IReadOnlyList<Turn> TurnsOn(SocraticSession session, string objectiveKey) =>
        session.Turns.Where(t => ExamObjectives.KeyOf(t) == objectiveKey).ToList();

    /// <summary>Index of the first turn on the objective whose question leaked; null when none did.</summary>
    public static int? FirstLeak(SocraticSession session, string objectiveKey) =>
        TurnsOn(session, objectiveKey).Where(t => t.Leak is not null && !MoveProfiles.IsAssistance(t.Move))
            .Select(t => (int?)t.Index).FirstOrDefault();

    /// <summary>Index of the first hint or guided explanation on the objective; null when none was given.</summary>
    public static int? FirstAssist(SocraticSession session, string objectiveKey) =>
        TurnsOn(session, objectiveKey).Where(t => MoveProfiles.IsAssistance(t.Move)).Select(t => (int?)t.Index).FirstOrDefault();

    /// <summary>
    /// Whether the answer to <paramref name="turn"/> is contaminated evidence for <paramref name="objectiveKey"/>:
    /// it came at or after a leaked question on that objective. A replacement question's answer still counts for
    /// the dimensions a leak cannot give away (application, transfer, reasoning, misconception recognition).
    /// </summary>
    public static bool IsContaminated(SocraticSession session, Turn turn, string objectiveKey, AssessmentDimension? dimension = null)
    {
        var leak = FirstLeak(session, objectiveKey);
        if (leak is null || turn.Index < leak) return false;
        if (!turn.IsReplacement || turn.Leak is not null) return true;
        return dimension is { } d && !LeakResistantDimensions.Contains(d);
    }

    /// <summary>True when the answer read as recited and was not accepted as the student's own understanding (GAD-T242).</summary>
    public static bool IsRecited(Turn turn) => turn.Judgment?.Recitation is not null;

    public static bool IsAssisted(SocraticSession session, Turn turn, string objectiveKey) =>
        FirstAssist(session, objectiveKey) is { } a && turn.Index >= a;

    /// <summary>True when the judgment records an honest "I don't know" (scored 0 without criteria, not a failed panel).</summary>
    public static bool IsAporia(AnswerJudgment j) => j.Criteria.Count == 0 && j.OverallScore == 0 && !j.NeedsHumanReview;

    /// <summary>True when no judge gave a usable verdict: the answer is not evidence either way until a human grades it.</summary>
    public static bool IsUnjudged(AnswerJudgment j) => j.Criteria.Count == 0 && j.NeedsHumanReview;

    /// <summary>
    /// How well the student resolved a contradiction put to them (GAD-T266). A contradiction shows only that two claims
    /// cannot both stand, not which is wrong, and revising either is a legitimate resolution, so the confrontation is
    /// settled by the judge's consistency criterion (which, on a Contradiction move, scores the resolution) rather than by
    /// the overall score, whose accuracy part depends on which claim the student kept. Falls back to the overall score
    /// when the panel gave no consistency score.
    /// </summary>
    public static double ResolutionScore(AnswerJudgment j) =>
        j.Criteria.FirstOrDefault(c => string.Equals(c.Criterion, "consistency", StringComparison.OrdinalIgnoreCase))?.Score
        ?? j.OverallScore;

    public static AnswerResult ResultOf(double score) =>
        score >= PositiveAtOrAbove ? AnswerResult.Positive : score < NegativeBelow ? AnswerResult.Negative : AnswerResult.Mixed;

    /// <summary>Every evidence item in the session with its flags, in turn order (the server persists these).</summary>
    public static IReadOnlyList<CollectedEvidence> CollectEvidence(SocraticSession session)
    {
        var list = new List<CollectedEvidence>();
        foreach (var t in session.Turns.Where(t => t.Analysis is not null && t.Answer is not null))
        {
            foreach (var e in t.Analysis!.Evidence)
            {
                list.Add(new CollectedEvidence(t.Index, e.ObjectiveKey, e.Polarity, e.Dimension, e.Quote, e.Note, e.Support,
                    Assisted: IsAssisted(session, t, e.ObjectiveKey) || MoveProfiles.IsAssistance(t.Move),
                    Contaminated: IsContaminated(session, t, e.ObjectiveKey, e.Dimension) || IsRecited(t), t.Move));
            }
        }
        return list;
    }

    public static ObjectiveAssessment Assess(SocraticSession session, ExamObjective objective, IReadOnlyList<ContradictionCandidate>? contradictions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(objective);
        var p = session.Policy;
        var key = objective.Key;
        var turns = TurnsOn(session, key);
        if (turns.Count == 0)
        {
            return new ObjectiveAssessment
            {
                ObjectiveKey = key, Title = objective.Title, Status = ObjectiveStatus.NotStarted,
                Level = MasteryLevel.InsufficientEvidence, ProvisionalLevel = MasteryLevel.InsufficientEvidence,
                Explanation = "Not examined yet.",
            };
        }

        var leak = FirstLeak(session, key);
        var assist = FirstAssist(session, key);
        var outcomes = new List<AnswerOutcome>();
        Turn? previousJudged = null;
        foreach (var t in turns)
        {
            // A skipped turn still counts as the previous one, so a Clarify after it never merges into an earlier answer.
            if (t.Judgment is null || t.Answer is null || IsUnjudged(t.Judgment)) { previousJudged = t; continue; }
            if (t.Move is MoveKind.Contradiction or MoveKind.Close) { previousJudged = t; continue; }
            // A recited answer (GAD-T242) is not the student's own understanding: left out like a leaked one.
            var contaminated = (leak is { } l && t.Index >= l && (!t.IsReplacement || t.Leak is not null)) || IsRecited(t);
            var assisted = (assist is { } a && t.Index >= a) || MoveProfiles.IsAssistance(t.Move);
            var score = t.Judgment.OverallScore;
            if (t.Move == MoveKind.Clarify)
            {
                // A clarification refines the answer it clarifies: one answer, judged on its clarified form.
                if (previousJudged is not null && outcomes.Count > 0 && outcomes[^1].TurnIndex == previousJudged.Index)
                {
                    var prior = outcomes[^1];
                    outcomes[^1] = prior with
                    {
                        Score = score, Result = ResultOf(score), Aporia = IsAporia(t.Judgment),
                        // Saying a recited answer differently is exactly what the examiner asked for: only a leak carries over.
                        Contaminated = (prior.Contaminated && !IsRecited(previousJudged)) || contaminated, Assisted = prior.Assisted || assisted,
                    };
                }
                previousJudged = t;
                continue;
            }
            var probed = MoveProfiles.ProbedLevel(t.Move);
            if (MoveProfiles.IsAssistance(t.Move)) probed = MoveProfiles.ProbedLevel(LastProbing(turns, t.Index) ?? MoveKind.Elicit);
            outcomes.Add(new AnswerOutcome(t.Index, t.Move, probed, score, ResultOf(score), contaminated, assisted, IsAporia(t.Judgment)));
            previousJudged = t;
        }

        var independent = outcomes.Where(o => o.Independent).ToList();
        var minCorr = p.MinCorroboratingAnswers;
        var ladder = Settled(independent, minCorr);

        // Consistency: contradictions between the student's claims on this objective's concepts.
        var concepts = new HashSet<string>(objective.ConceptKeys.Count > 0 ? objective.ConceptKeys : [key], StringComparer.Ordinal);
        var mine = (contradictions ?? []).Where(c => concepts.Contains(c.ConceptId)).ToList();
        var confronting = session.Turns.Where(t => t.Move == MoveKind.Contradiction).ToList();
        int unconfronted = 0, failedConfrontations = 0, unjudgedConfrontations = 0;
        foreach (var c in mine)
        {
            var asked = confronting.Where(t => t.TargetClaimIds.Contains(c.FirstClaimId) && t.TargetClaimIds.Contains(c.SecondClaimId)).ToList();
            // Only a usable verdict settles a confrontation: asked but unanswered or unjudged stays pending, never consistent.
            var judgedTurn = asked.FirstOrDefault(t => t.Answer is not null && t.Judgment is { } tj && !IsUnjudged(tj));
            if (asked.Count == 0) unconfronted++;
            else if (judgedTurn is null) unjudgedConfrontations++;
            else if (ResolutionScore(judgedTurn.Judgment!) < 0.5) failedConfrontations++;
        }
        var consistent = unconfronted == 0 && failedConfrontations == 0 && unjudgedConfrontations == 0;

        // Misconceptions: resolved by a later independent positive answer that probed understanding (level 3+).
        var misconceptions = session.Turns.Where(t => t.Analysis is not null && t.Judgment is not null)
            .SelectMany(t => t.Analysis!.Misconceptions.Where(m => m.ObjectiveKey == key).Select(m => (t.Index, m.Text)))
            .ToList();
        var unresolved = new List<string>();
        var untested = new List<string>();
        foreach (var (index, text) in misconceptions)
        {
            if (unresolved.Any(u => Similar(u, text))) continue;
            var resolved = independent.Any(o => o.TurnIndex > index && o.Result == AnswerResult.Positive && o.ProbedLevel >= 3);
            if (resolved) continue;
            unresolved.Add(text);
            var tested = turns.Any(t => t.Index > index && t.Move is MoveKind.Counterexample or MoveKind.AssumptionTesting && t.Judgment is not null);
            if (!tested) untested.Add(text);
        }

        var level = ladder.Level;
        var capReasons = new List<string>();
        if (failedConfrontations > 0 && level > 2) { level = 2; capReasons.Add("the student could not reconcile two of their own claims"); }
        if (unresolved.Count > untested.Count && level > 2) { level = 2; capReasons.Add("a misconception persisted after it was tested"); }
        var enough = independent.Count >= minCorr;
        var provisional = enough ? (MasteryLevel)level : MasteryLevel.InsufficientEvidence;

        // Sufficiency: enough independent answers, nothing pending, the stated level corroborated and its ceiling tested.
        var ceilingTested = CeilingTested(ladder, independent.Count, minCorr);
        // The tentative top must be corroborated, or outweighed: a single success at a level the student then failed
        // repeatedly at or above (Scenario A: a correct definition, then no explanation or application).
        var topCorroborated = TopSettled(ladder, minCorr);
        var pending = unconfronted > 0 || unjudgedConfrontations > 0 || untested.Count > 0;
        var sufficient = enough && topCorroborated && ceilingTested && !pending;

        // Learning Mode: the assisted view counts every uncontaminated answer, assisted or not, and decides when to stop.
        MasteryLevel? assistedLevel = null;
        var assistedCount = outcomes.Count(o => o.Assisted && !o.Contaminated);
        if (assist is not null)
        {
            var all = outcomes.Where(o => !o.Contaminated).ToList();
            var assistedLadder = Settled(all, minCorr);
            assistedLevel = all.Count >= minCorr ? (MasteryLevel)assistedLadder.Level : MasteryLevel.InsufficientEvidence;
            if (p.Mode == AssessmentMode.Learning)
            {
                // Learning goes on while help can still make a difference: the last answer after the last hint must be
                // known, and a weak one gets more help while assists remain (Part 3.2: reassess after instruction).
                var lastAssist = turns.Last(t => MoveProfiles.IsAssistance(t.Move)).Index;
                var reassessed = outcomes.Any(o => o.TurnIndex > lastAssist);
                var lastWeak = outcomes.Count > 0 && outcomes[^1].Result != AnswerResult.Positive;
                var assistsLeft = turns.Count(t => MoveProfiles.IsAssistance(t.Move)) < p.MaxAssistsPerObjective;
                sufficient = all.Count >= minCorr && TopSettled(assistedLadder, minCorr) && CeilingTested(assistedLadder, all.Count, minCorr)
                             && unconfronted == 0 && unjudgedConfrontations == 0 && reassessed && !(lastWeak && assistsLeft);
            }
        }

        var lastOnObjective = turns[^1];
        var left = session.Turns.Any(t => t.Index > lastOnObjective.Index && ExamObjectives.KeyOf(t) != key);
        var atLimit = turns.Count >= p.MaxTurnsPerConcept;
        var status = sufficient ? ObjectiveStatus.Sufficient
            : atLimit || left || session.IsEnded ? ObjectiveStatus.Inconclusive
            : ObjectiveStatus.Open;

        var uncertainties = turns.LastOrDefault(t => t.Analysis is not null && t.Judgment is not null)?.Analysis?.Uncertainties ?? [];
        var explanation = Explain(status, enough, independent.Count, minCorr, topCorroborated, ceilingTested, unconfronted, unjudgedConfrontations, untested.Count,
            ladder, capReasons, outcomes.Count(o => o.Contaminated), assistedCount, atLimit, session.IsEnded, left);

        return new ObjectiveAssessment
        {
            ObjectiveKey = key,
            Title = objective.Title,
            Status = status,
            Level = status == ObjectiveStatus.Sufficient
                ? provisional
                : MasteryLevel.InsufficientEvidence,
            ProvisionalLevel = provisional,
            AssistedLevel = assistedLevel,
            Questions = turns.Count,
            FollowUps = turns.Count(t => MoveProfiles.IsFollowUp(t.Move)),
            IndependentAnswers = independent.Count,
            SupportingAnswers = independent.Count(o => o.Result == AnswerResult.Positive),
            ContradictingAnswers = independent.Count(o => o.Result == AnswerResult.Negative),
            ContaminatedAnswers = outcomes.Count(o => o.Contaminated),
            AssistedAnswers = assistedCount,
            AssistsGiven = turns.Count(t => MoveProfiles.IsAssistance(t.Move)),
            Corroborated = enough && topCorroborated,
            Consistent = consistent,
            ReasoningShown = independent.Any(o => o.Result == AnswerResult.Positive && o.ProbedLevel >= 3),
            TransferShown = independent.Any(o => o.Result == AnswerResult.Positive && o.ProbedLevel >= 4),
            CriticalReasoningShown = independent.Any(o => o.Result == AnswerResult.Positive && o.ProbedLevel >= 5),
            UnresolvedMisconceptions = unresolved,
            UntestedMisconceptions = untested,
            Uncertainties = uncertainties,
            UnconfrontedContradictions = unconfronted,
            LeakedTurns = turns.Where(t => t.Leak is not null).Select(t => t.Index).ToList(),
            TentativeHigh = ladder.TentativeHigh,
            PlausibleCeiling = ladder.Ceiling,
            FailuresAbove = ladder.FailuresAbove,
            CorroboratedLevel = ladder.Level,
            Explanation = explanation,
            Answers = outcomes,
        };
    }

    private static MoveKind? LastProbing(IReadOnlyList<Turn> turns, int before) =>
        turns.LastOrDefault(t => t.Index < before && MoveProfiles.ProbedLevel(t.Move) > 0)?.Move;

    /// <summary>The level ladder from a set of outcomes.</summary>
    internal readonly record struct Ladder(int Level, int TentativeHigh, int Ceiling, int FailuresAbove, int FailuresAtOrAboveHigh, bool AnyPositive);

    private static bool CeilingTested(Ladder l, int answers, int minCorr) =>
        l.Level >= 5 || l.FailuresAbove >= 2 || (l.FailuresAbove >= 1 && answers >= minCorr + 2);

    private static bool TopSettled(Ladder l, int minCorr) => l.Level >= l.TentativeHigh || l.FailuresAtOrAboveHigh >= minCorr;

    /// <summary>
    /// The ladder, where a good answer never unsettles a settled result: a positive Synthesize answer (asked only once the
    /// objective was sufficient) above the settled level can raise it once corroborated, but never reopen the objective.
    /// </summary>
    private static Ladder Settled(IReadOnlyList<AnswerOutcome> outcomes, int minCorr)
    {
        var ladder = Climb(outcomes, minCorr);
        if (TopSettled(ladder, minCorr)) return ladder;
        var before = outcomes.Where(o => !(o.Move == MoveKind.Synthesize && o.Result == AnswerResult.Positive)).ToList();
        if (before.Count == outcomes.Count || before.Count < minCorr) return ladder;
        var basis = Climb(before, minCorr);
        return TopSettled(basis, minCorr) && CeilingTested(basis, before.Count, minCorr) ? basis : ladder;
    }

    internal static Ladder Climb(IReadOnlyList<AnswerOutcome> outcomes, int minCorroboration)
    {
        var pos = new int[6];
        var neg = new int[6];
        foreach (var o in outcomes.Where(o => o.ProbedLevel is >= 2 and <= 5))
        {
            if (o.Result == AnswerResult.Positive) pos[o.ProbedLevel]++;
            else if (o.Result == AnswerResult.Negative) neg[o.ProbedLevel]++;
            else
            {
                // A partial answer shows partial accuracy, which is Level 2 by definition (Part 5.3), and falls short
                // of any higher level it probed.
                pos[2]++;
                if (o.ProbedLevel > 2) neg[o.ProbedLevel]++;
            }
        }
        // Corroborated level: the highest level with enough positive answers at or above it.
        var level = 1;
        for (var lv = 5; lv >= 2; lv--)
            if (pos.Skip(lv).Sum() >= minCorroboration) { level = lv; break; }
        // Level 5 also needs application shown (Part 5.3: transfer *and* critical reasoning).
        if (level == 5 && pos[4] == 0) level = 4;
        var high = 1;
        for (var lv = 5; lv >= 2; lv--)
            if (pos[lv] > 0) { high = lv; break; }
        var ceiling = 5;
        for (var lv = high + 1; lv <= 5; lv++)
            if (neg[lv] > 0) { ceiling = lv - 1; break; }
        var failuresAbove = neg.Skip(level + 1).Sum();
        return new Ladder(level, high, ceiling, failuresAbove, neg.Skip(high).Sum(), pos.Sum() > 0);
    }

    private static string Explain(ObjectiveStatus status, bool enough, int independent, int minCorr, bool topCorroborated, bool ceilingTested,
        int unconfronted, int unjudgedConfrontations, int untested, Ladder ladder, List<string> capReasons, int contaminated, int assisted, bool atLimit, bool ended, bool left)
    {
        var parts = new List<string>();
        if (status == ObjectiveStatus.Sufficient)
            parts.Add(ladder.Level <= 1
                ? $"Sufficient evidence: {independent} independent answers; nothing above Recognition held up ({ladder.FailuresAbove} answer(s) fell short at higher levels)."
                : $"Sufficient evidence: {independent} independent answers; level {ladder.Level} corroborated and its ceiling tested.");
        else
        {
            if (!enough) parts.Add($"Only {independent} independent answer(s); at least {minCorr} are needed before any conclusion.");
            else if (!topCorroborated) parts.Add($"Level {ladder.TentativeHigh} was shown once but neither corroborated nor outweighed.");
            else if (!ceilingTested) parts.Add($"Level {ladder.Level} is corroborated but the next level has not been tested enough.");
            if (unconfronted > 0) parts.Add($"{unconfronted} contradiction(s) in the student's claims not yet put to them.");
            if (unjudgedConfrontations > 0) parts.Add($"{unjudgedConfrontations} contradiction(s) put to the student but not yet answered or judged.");
            if (untested > 0) parts.Add($"{untested} possible misconception(s) not yet tested.");
            if (status == ObjectiveStatus.Inconclusive)
                parts.Add(atLimit ? "The question limit for this objective was reached: Insufficient Evidence for Reliable Assessment."
                    : ended ? "The session ended first: Insufficient Evidence for Reliable Assessment."
                    : left ? "The examiner moved on to leave time for other objectives: Insufficient Evidence for Reliable Assessment."
                    : "Insufficient Evidence for Reliable Assessment.");
        }
        if (capReasons.Count > 0) parts.Add("Level capped at 2: " + string.Join("; ", capReasons) + ".");
        if (contaminated > 0) parts.Add($"{contaminated} answer(s) after a question that revealed the answer were excluded.");
        if (assisted > 0) parts.Add($"{assisted} answer(s) came after a hint or explanation and are reported as assisted.");
        return string.Join(" ", parts);
    }

    private static bool Similar(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var inter = x.Count(y.Contains);
        return (double)inter / (x.Count + y.Count - inter) >= 0.5;
    }

    private static HashSet<string> Words(string s) =>
        s.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',', ';', ':', '!', '?', '"', '\''))
            .Where(w => w.Length > 2).ToHashSet();
}
