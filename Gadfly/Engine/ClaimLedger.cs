using System.Text.Json;
using System.Text.RegularExpressions;
using Gadfly.Socrates;

namespace Gadfly.Engine;

/// <summary>One thing the student asserted, in their words and restated plainly.</summary>
public sealed record StudentClaim(string Id, int TurnIndex, string ConceptId, string Quote, string Text);

/// <summary>Two of the student's claims the panel judged incompatible.</summary>
public sealed record ClaimContradiction(string FirstClaimId, string SecondClaimId, string ConceptId, double Confidence);

/// <summary>
/// The elenchus's memory: every claim the student has made in the session, and every pair of
/// claims found to contradict. The planner turns unconfronted contradictions into Contradiction moves.
/// </summary>
public sealed class ClaimLedger
{
    private readonly List<StudentClaim> _claims = [];
    private readonly List<ClaimContradiction> _contradictions = [];

    public IReadOnlyList<StudentClaim> Claims => _claims;
    public IReadOnlyList<ClaimContradiction> Contradictions => _contradictions;

    public StudentClaim Add(int turnIndex, string conceptId, string quote, string text)
    {
        var claim = new StudentClaim($"c{_claims.Count + 1}", turnIndex, conceptId, quote.Trim(), text.Trim());
        _claims.Add(claim);
        return claim;
    }

    public void AddContradiction(ClaimContradiction c)
    {
        if (_contradictions.Any(x => (x.FirstClaimId == c.FirstClaimId && x.SecondClaimId == c.SecondClaimId)
                                  || (x.FirstClaimId == c.SecondClaimId && x.SecondClaimId == c.FirstClaimId)))
            return;
        _contradictions.Add(c);
    }

    public StudentClaim? Find(string id) => _claims.FirstOrDefault(c => c.Id == id);

    /// <summary>The ledger's contradictions in the shape the <see cref="QuestionPlanner"/> takes.</summary>
    public IReadOnlyList<ContradictionCandidate> ForPlanner() =>
        _contradictions.Select(c => new ContradictionCandidate(c.ConceptId, c.FirstClaimId, c.SecondClaimId)).ToList();

    /// <summary>Earlier claims as a prompt-ready list ("[turn 3] ...").</summary>
    public string Describe(string? conceptId = null, int max = 30) =>
        string.Join("\n", _claims.Where(c => conceptId is null || c.ConceptId == conceptId)
            .TakeLast(max).Select(c => $"[turn {c.TurnIndex + 1}] {c.Text}"));
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

        var earlier = ledger.Claims.Where(c => c.ConceptId == turn.ConceptId && c.TurnIndex < turn.Index).ToList();
        var extracted = await ExtractAsync(turn, conceptTitle, ct);
        var found = new List<ClaimContradiction>();
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
                    ["first_claim"] = prior.Text,
                    ["second_turn"] = (claim.TurnIndex + 1).ToString(),
                    ["second_claim"] = claim.Text,
                });
                var d = await panel.DecideAsync(question, [Contradict, Consistent], "", ct);
                if (string.Equals(d.Choice, Contradict, StringComparison.OrdinalIgnoreCase))
                {
                    var c = new ClaimContradiction(prior.Id, claim.Id, turn.ConceptId, d.Confidence);
                    ledger.AddContradiction(c);
                    found.Add(c);
                }
            }
        }
        return found;
    }

    private async Task<IReadOnlyList<(string Quote, string Text)>> ExtractAsync(Turn turn, string conceptTitle, CancellationToken ct)
    {
        var question = ExamPrompts.Fill(prompts[ExamPrompts.ClaimsKey], new Dictionary<string, string?>
        {
            ["concept"] = conceptTitle,
            ["question"] = turn.Question,
            ["answer"] = turn.Answer!.Text,
        });
        var result = await panel.AskAsync(question, "", MindAttic.Legion.Quorum.Plurality, ct);

        // Each voter returns its own JSON list; keep the claims a majority of parsed voters agree on.
        var perVoter = result.IndividualVotes.Where(v => !v.IsError).Select(v => ParseClaims(v.Decision))
            .Where(list => list is not null).Select(list => list!).ToList();
        if (perVoter.Count == 0)
        {
            var consensus = ParseClaims(result.Consensus);
            return consensus ?? [];
        }
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
    internal static IReadOnlyList<(string Quote, string Text)>? ParseClaims(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var list = new List<(string, string)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var text = el.TryGetProperty("claim", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var quote = el.TryGetProperty("quote", out var q) ? q.GetString() : null;
                list.Add((quote ?? text, text));
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
