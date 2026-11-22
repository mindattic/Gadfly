using System.Text.Json;
using System.Text.RegularExpressions;
using Gadfly.Socrates;
using MindAttic.Legion;

namespace Gadfly.Engine;

/// <summary>One thing the student asserted, in their words and restated plainly.</summary>
public sealed record StudentClaim(string Id, int TurnIndex, string ConceptId, string Quote, string Text);

/// <summary>Two of the student's claims the panel judged incompatible.</summary>
public sealed record ClaimContradiction(string FirstClaimId, string SecondClaimId, string ConceptId, double Confidence);

/// <summary>
/// The student explicitly withdrew an earlier claim ("I was wrong earlier", GAD-T441). <see cref="ClaimId"/> is the ledger claim
/// it was matched to, or null when no earlier claim matched well enough (kept for the reviewer all the same).
/// </summary>
public sealed record ClaimRetraction(int TurnIndex, string Quote, string Text, string? ClaimId);

/// <summary>
/// The elenchus's memory: every claim the student has made in the session, every pair of claims found to contradict, and
/// every claim the student withdrew. The planner turns unconfronted contradictions into Contradiction moves. A withdrawn
/// claim stays in the ledger for the record but is no longer a premise: it is not checked against new claims, and a
/// contradiction resting on it is not put to the student (ElenchusAtScale M6, 3.4).
/// </summary>
public sealed class ClaimLedger
{
    private readonly List<StudentClaim> _claims = [];
    private readonly List<ClaimContradiction> _contradictions = [];
    private readonly List<ClaimRetraction> _retractions = [];

    public IReadOnlyList<StudentClaim> Claims => _claims;
    public IReadOnlyList<ClaimContradiction> Contradictions => _contradictions;
    /// <summary>Claims the student withdrew, in the order they withdrew them (GAD-T441).</summary>
    public IReadOnlyList<ClaimRetraction> Retractions => _retractions;

    /// <summary>Least word overlap between a retraction and an earlier claim for the two to be matched.</summary>
    public const double RetractionMatch = 0.3;

    public StudentClaim Add(int turnIndex, string conceptId, string quote, string text)
    {
        var claim = new StudentClaim($"c{_claims.Count + 1}", turnIndex, conceptId, quote.Trim(), text.Trim());
        _claims.Add(claim);
        return claim;
    }

    /// <summary>The turn at which the student withdrew <paramref name="claimId"/>; null while it stands.</summary>
    public int? WithdrawnAt(string claimId) => _retractions.FirstOrDefault(r => r.ClaimId == claimId)?.TurnIndex;

    public bool IsWithdrawn(string claimId) => WithdrawnAt(claimId) is not null;

    /// <summary>
    /// Records that at <paramref name="turnIndex"/> the student withdrew an earlier claim, restated as <paramref name="text"/>.
    /// Deterministic: the earlier, still-standing claim with the most word overlap (claim text or quote, at least
    /// <see cref="RetractionMatch"/>; the latest one on a tie) is marked withdrawn. No panel call.
    /// </summary>
    public ClaimRetraction Retract(int turnIndex, string quote, string text)
    {
        var best = _claims.Where(c => c.TurnIndex < turnIndex && !IsWithdrawn(c.Id))
            .Select(c => (Claim: c, Score: Math.Max(ClaimAnalyzer.Jaccard(c.Text, text), ClaimAnalyzer.Jaccard(c.Quote, text))))
            .Where(x => x.Score >= RetractionMatch)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Claim.TurnIndex)
            .Select(x => x.Claim).FirstOrDefault();
        var r = new ClaimRetraction(turnIndex, quote.Trim(), text.Trim(), best?.Id);
        if (!_retractions.Any(x => x.TurnIndex == r.TurnIndex && x.ClaimId == r.ClaimId && (r.ClaimId is not null || x.Text == r.Text)))
            _retractions.Add(r);
        return r;
    }

    public void AddContradiction(ClaimContradiction c)
    {
        if (_contradictions.Any(x => (x.FirstClaimId == c.FirstClaimId && x.SecondClaimId == c.SecondClaimId)
                                  || (x.FirstClaimId == c.SecondClaimId && x.SecondClaimId == c.FirstClaimId)))
            return;
        _contradictions.Add(c);
    }

    public StudentClaim? Find(string id) => _claims.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// The ledger's contradictions in the shape the <see cref="QuestionPlanner"/> takes. A contradiction resting on a claim the
    /// student has since withdrawn carries the turn they withdrew it at: the planner does not put it to them, and the assessor
    /// counts it as withdrawn rather than pending, unless it had already been put to them (GAD-T441).
    /// </summary>
    public IReadOnlyList<ContradictionCandidate> ForPlanner() =>
        _contradictions.Select(c => new ContradictionCandidate(c.ConceptId, c.FirstClaimId, c.SecondClaimId)
        {
            WithdrawnAtTurn = WithdrawnAt(c.FirstClaimId) is { } a ? (WithdrawnAt(c.SecondClaimId) is { } b ? Math.Min(a, b) : a) : WithdrawnAt(c.SecondClaimId),
        }).ToList();

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record LedgerDto(List<StudentClaim> Claims, List<ClaimContradiction> Contradictions, List<ClaimRetraction>? Retractions = null);

    /// <summary>The whole ledger as JSON, persisted beside the session so a resumed exam keeps its memory.</summary>
    public string ToJson() => JsonSerializer.Serialize(new LedgerDto([.. _claims], [.. _contradictions], [.. _retractions]), Json);

    public static ClaimLedger FromJson(string? json)
    {
        var ledger = new ClaimLedger();
        if (string.IsNullOrWhiteSpace(json)) return ledger;
        var dto = JsonSerializer.Deserialize<LedgerDto>(json, Json);
        if (dto is null) return ledger;
        ledger._claims.AddRange(dto.Claims ?? []);
        ledger._contradictions.AddRange(dto.Contradictions ?? []);
        ledger._retractions.AddRange(dto.Retractions ?? []);
        return ledger;
    }

    /// <summary>
    /// Earlier claims as a prompt-ready list ("[turn 3] ..."). A withdrawn claim stays on the list for the record, marked as
    /// withdrawn, so the judges see the correction (which the judge prompt counts as a strength) rather than a live premise.
    /// </summary>
    public string Describe(string? conceptId = null, int max = 30) =>
        string.Join("\n", _claims.Where(c => conceptId is null || c.ConceptId == conceptId)
            .TakeLast(max).Select(c => WithdrawnAt(c.Id) is { } at
                ? $"[turn {c.TurnIndex + 1}] {c.Text} (withdrawn by the student at turn {at + 1})"
                : $"[turn {c.TurnIndex + 1}] {c.Text}"));
}

/// <summary>
/// Fills the <see cref="ClaimLedger"/> from each answer through the Legion panel: claim extraction
/// (merged across voters, keeping claims a majority found), then contradiction checks against
/// earlier claims on the same concept. Candidate pairs are pre-filtered by word overlap and capped,
/// so the number of panel calls per answer stays small.
/// </summary>
public sealed class ClaimAnalyzer(ISocraticVotingClient panel, PromptSet prompts)
{
    /// <summary>Most earlier claims compared against each new claim.</summary>
    public int MaxComparisonsPerClaim { get; init; } = 3;

    /// <summary>Minimum word overlap (Jaccard) for two claims to be worth a contradiction check.</summary>
    public double MinOverlap { get; init; } = 0.15;

    public const string Contradict = "CONTRADICT";
    public const string Consistent = "CONSISTENT";

    /// <summary>Extracts the answer's claims into the ledger and checks them; returns the new contradictions.</summary>
    public async Task<IReadOnlyList<ClaimContradiction>> AnalyzeAsync(ClaimLedger ledger, Turn turn, string conceptTitle,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (turn.Answer is null || string.IsNullOrWhiteSpace(turn.Answer.Text)) return [];

        var (extracted, retractions) = await ExtractAsync(turn, conceptTitle, ct);
        return await AddClaimsAsync(ledger, turn, extracted, ct, retractions);
    }

    /// <summary>
    /// Records the claims the student withdrew in this answer (GAD-T441): each is matched to an earlier claim in the ledger,
    /// which stays for the record but is no longer a premise. Deterministic; no panel call.
    /// </summary>
    public static IReadOnlyList<ClaimRetraction> ApplyRetractions(ClaimLedger ledger, Turn turn, IReadOnlyList<(string Quote, string Text)>? retractions)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (retractions is null || retractions.Count == 0) return [];
        return retractions.Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => ledger.Retract(turn.Index, r.Quote, r.Text)).ToList();
    }

    /// <summary>
    /// Adds claims already extracted elsewhere (the response analysis, GAD-T91) to the ledger and checks them
    /// against earlier claims on the same concept; returns the new contradictions. Claims the answer withdrew
    /// (<paramref name="retractions"/>) are recorded first, so a withdrawn claim is never checked again (GAD-T441).
    /// </summary>
    public async Task<IReadOnlyList<ClaimContradiction>> AddClaimsAsync(ClaimLedger ledger, Turn turn,
        IReadOnlyList<(string Quote, string Text)> extracted, CancellationToken ct = default,
        IReadOnlyList<(string Quote, string Text)>? retractions = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (turn.Answer is null || string.IsNullOrWhiteSpace(turn.Answer.Text)) return [];
        ApplyRetractions(ledger, turn, retractions);
        // A withdrawn claim is no longer a premise: it stays on record but is never a side of a new contradiction.
        var earlier = ledger.Claims.Where(c => c.ConceptId == turn.ConceptId && c.TurnIndex < turn.Index && !ledger.IsWithdrawn(c.Id)).ToList();
        // Each candidate pair's CONTRADICT/CONSISTENT check reads only that pair, independent of every other pair's check
        // (whether a new claim disagrees with one earlier claim never depends on its verdict against another), so a turn
        // whose claims accumulate several candidates (later in a longer exam, as the ledger fills up) no longer pays one
        // sequential panel round trip per pair: they all run together instead (GAD-T404).
        var checks = new List<(StudentClaim Claim, StudentClaim Prior, Task<DecisionResult> Vote)>();
        foreach (var (quote, text) in extracted)
        {
            var claim = ledger.Add(turn.Index, turn.ConceptId, quote, text);
            var candidates = earlier
                .Select(e => (Claim: e, Overlap: Jaccard(e.Text, claim.Text)))
                .Where(x => x.Overlap >= MinOverlap)
                .OrderByDescending(x => x.Overlap)
                .Take(MaxComparisonsPerClaim);
            foreach (var (prior, _) in candidates)
            {
                var question = ExamPrompts.Fill(prompts[ExamPrompts.ContradictionKey], new Dictionary<string, string?>
                {
                    ["first_turn"] = (prior.TurnIndex + 1).ToString(),
                    ["first_claim"] = ExamPrompts.Student(prior.Text),
                    ["second_turn"] = (claim.TurnIndex + 1).ToString(),
                    ["second_claim"] = ExamPrompts.Student(claim.Text),
                });
                checks.Add((claim, prior, panel.DecideAsync(question, [Contradict, Consistent], "", ct)));
            }
        }
        if (checks.Count == 0) return [];
        await Task.WhenAll(checks.Select(c => c.Vote));
        var found = new List<ClaimContradiction>();
        foreach (var (claim, prior, vote) in checks)
        {
            var d = await vote;
            if (string.Equals(d.Choice, Contradict, StringComparison.OrdinalIgnoreCase))
            {
                var c = new ClaimContradiction(prior.Id, claim.Id, turn.ConceptId, d.Confidence);
                ledger.AddContradiction(c);
                found.Add(c);
            }
        }
        return found;
    }

    private async Task<(IReadOnlyList<(string Quote, string Text)> Claims, IReadOnlyList<(string Quote, string Text)> Retractions)> ExtractAsync(
        Turn turn, string conceptTitle, CancellationToken ct)
    {
        var question = ExamPrompts.Fill(prompts[ExamPrompts.ClaimsKey], new Dictionary<string, string?>
        {
            ["concept"] = conceptTitle,
            ["question"] = turn.Question,
            ["answer"] = ExamPrompts.Student(turn.Answer!.Text),
        });
        var result = await panel.AskAsync(question, "", MindAttic.Legion.Quorum.Plurality, ct);

        // Each voter returns its own JSON list; keep the claims (and withdrawals) a majority of parsed voters agree on.
        var perVoter = result.IndividualVotes.Where(v => !v.IsError).Select(v => ParseItems(v.Decision))
            .Where(list => list is not null).Select(list => list!).ToList();
        if (perVoter.Count == 0) perVoter = ParseItems(result.Consensus) is { } consensus ? [consensus] : [];
        return (Majority(perVoter.Select(l => l.Where(x => !x.Withdraws).Select(x => (x.Quote, x.Text)).ToList()).ToList()),
                Majority(perVoter.Select(l => l.Where(x => x.Withdraws).Select(x => (x.Quote, x.Text)).ToList()).ToList()));
    }

    /// <summary>The items a majority of voters agree on (one voter: all of its items).</summary>
    private static IReadOnlyList<(string Quote, string Text)> Majority(IReadOnlyList<List<(string Quote, string Text)>> perVoter)
    {
        if (perVoter.Count == 0) return [];
        if (perVoter.Count == 1) return perVoter[0];
        var needed = (perVoter.Count + 1) / 2;
        var merged = new List<(string Quote, string Text, int Support)>();
        foreach (var list in perVoter)
        {
            foreach (var claim in list)
            {
                var i = merged.FindIndex(m => Jaccard(m.Text, claim.Text) >= 0.6);
                if (i < 0) merged.Add((claim.Quote, claim.Text, 1));
                else merged[i] = merged[i] with { Support = merged[i].Support + 1 };
            }
        }
        return merged.Where(m => m.Support >= needed).Select(m => (m.Quote, m.Text)).ToList();
    }

    /// <summary>Parses a claims JSON array, tolerating prose or a code fence around it. Null when unparseable.</summary>
    internal static IReadOnlyList<(string Quote, string Text)>? ParseClaims(string? raw) =>
        ParseItems(raw)?.Where(x => !x.Withdraws).Select(x => (x.Quote, x.Text)).ToList();

    /// <summary>
    /// Parses the claims prompt's JSON array: asserted claims ({"quote", "claim"}) and claims withdrawn from an earlier answer
    /// ({"quote", "withdraws"}, GAD-T441). Null when unparseable.
    /// </summary>
    internal static IReadOnlyList<(string Quote, string Text, bool Withdraws)>? ParseItems(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var list = new List<(string, string, bool)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var withdrawn = el.TryGetProperty("withdraws", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                var text = el.TryGetProperty("claim", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var quote = el.TryGetProperty("quote", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
                if (!string.IsNullOrWhiteSpace(withdrawn)) list.Add((quote ?? withdrawn, withdrawn, true));
                else if (!string.IsNullOrWhiteSpace(text)) list.Add((quote ?? text, text, false));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly Regex Word = new(@"[\p{L}\p{N}']+", RegexOptions.Compiled);
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","of","to","in","is","are","was","were","be","it","that","this","for","on","with","as","by","at","from","not","no","can","does","do",
    };

    internal static double Jaccard(string a, string b)
    {
        static HashSet<string> Tokens(string s) => Word.Matches(s.ToLowerInvariant()).Select(m => m.Value)
            .Where(w => !Stop.Contains(w)).ToHashSet();
        var x = Tokens(a);
        var y = Tokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var inter = x.Count(y.Contains);
        return (double)inter / (x.Count + y.Count - inter);
    }
}
