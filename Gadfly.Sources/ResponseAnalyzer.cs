using System.Text.Json;
using System.Text.RegularExpressions;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>Everything the response analysis needs for one answer.</summary>
public sealed record AnalysisInput
{
    public required Turn Turn { get; init; }
    /// <summary>The objective the question examined.</summary>
    public required ExamObjective Objective { get; init; }
    /// <summary>Every objective of the exam: one answer can bear on several.</summary>
    public required IReadOnlyList<ExamObjective> Objectives { get; init; }
    public required ConceptRubric Rubric { get; init; }
    public required IReadOnlyList<SourcePassage> Passages { get; init; }
    /// <summary>The student's earlier claims, prompt-ready.</summary>
    public string EarlierClaims { get; init; } = "";
}

/// <summary>
/// Student Response Analysis Engine (GAD-T91, Component B). Every panel provider analyses the answer
/// against the objectives; each reply is validated strictly against the schema (exact keys, types and
/// enum values, known objective keys) and rejected whole when malformed. Items whose quote is not a
/// verbatim span of the student's answer are dropped: evidence is never fabricated. The surviving votes
/// are merged by majority, so a finding stands only when most valid voters made it.
/// </summary>
public sealed class ResponseAnalyzer(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    /// <summary>Longest quote kept (characters).</summary>
    public const int MaxQuote = 400;

    private static readonly HashSet<string> RootKeys = ["claims", "misconceptions", "uncertainties", "evidence", "questionRevealedAnswer"];
    /// <summary>Root keys a reply may add (GAD-T441); a voter that leaves them out is still valid.</summary>
    private static readonly HashSet<string> OptionalRootKeys = ["retractions"];

    public async Task<ResponseAnalysis> AnalyzeAsync(AnalysisInput input, CancellationToken ct = default)
    {
        var turn = input.Turn;
        if (turn.Answer is null) throw new ArgumentException("The turn has no answer to analyse.", nameof(input));
        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.AnalysisKey], new Dictionary<string, string?>
        {
            ["objectives"] = string.Join("\n", input.Objectives.Select(o =>
                $"- {o.Key}: {o.Title} [{string.Join(", ", o.Dimensions.Select(DimensionName))}]")),
            ["current_objective"] = $"{input.Objective.Key} ({input.Objective.Title})",
            ["move"] = turn.Move.ToString(),
            ["rubric"] = input.Rubric.Describe() is { Length: > 0 } r ? r : "(none)",
            ["claims"] = string.IsNullOrWhiteSpace(input.EarlierClaims) ? "(none yet)" : ExamPrompts.Student(input.EarlierClaims),
            ["passages"] = PanelJson.Number(input.Passages),
            ["question"] = turn.Question,
            ["answer"] = ExamPrompts.Student(turn.Answer.Text),
        });
        var replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 4000, temperature: 0.1, ct: ct);
        var keys = input.Objectives.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var votes = new List<ResponseAnalysis>();
        var rejected = 0;
        var rejections = new List<string>();
        foreach (var reply in replies.Where(r => !r.IsError).OrderBy(r => r.ProviderId, StringComparer.Ordinal))
        {
            var parsed = Parse(reply.Text, turn.Answer.Text, keys, out var why);
            if (parsed is null)
            {
                rejected++;
                rejections.Add($"{reply.ProviderId}: {why}");
            }
            else votes.Add(parsed);
        }
        var merged = Merge(votes, rejected);
        // Knowledge transfer is credited only from Apply, a situation new to the student (GAD-T268): a voter that calls a
        // ScenarioVariation what-if "transfer" is recorded as application (extrapolation).
        return merged with
        {
            Rejections = rejections,
            Evidence = merged.Evidence.Select(e => e with { Dimension = MoveProfiles.CreditedDimension(turn, e.Dimension) }).ToList(),
        };
    }

    /// <summary>
    /// The deterministic analysis of an honest "I don't know": one uncertain metacognition item quoting the
    /// student, no claims. No panel call.
    /// </summary>
    public static ResponseAnalysis ForAporia(Turn turn, string objectiveKey)
    {
        var text = turn.Answer?.Text.Trim() ?? "";
        var quote = text.Length > MaxQuote ? text[..MaxQuote] : text;
        return new ResponseAnalysis
        {
            Deterministic = true,
            Evidence = quote.Length == 0 ? [] :
                [new ObjectiveEvidence(objectiveKey, EvidencePolarity.Uncertain, AssessmentDimension.Metacognition, quote,
                    "The student said they do not know (aporia).")],
        };
    }

    /// <summary>
    /// Parses one voter's reply strictly. Null when it is not exactly the schema (missing or extra keys, wrong
    /// types, unknown stance, dimension or objective). Quotes that are not the student's own words are dropped
    /// and counted in <see cref="ResponseAnalysis.DroppedItems"/>.
    /// </summary>
    public static ResponseAnalysis? Parse(string? raw, string answer, IReadOnlySet<string> objectiveKeys) => Parse(raw, answer, objectiveKeys, out _);

    /// <summary>As <see cref="Parse(string?, string, IReadOnlySet{string})"/>, with why a reply was rejected.</summary>
    public static ResponseAnalysis? Parse(string? raw, string answer, IReadOnlySet<string> objectiveKeys, out string? rejection)
    {
        string? why = null;
        ResponseAnalysis? Fail(string reason) { why = reason; return null; }
        var result = ParseCore();
        rejection = why;
        return result;

        ResponseAnalysis? ParseCore()
        {
        var json = StripFence(raw);
        if (json is null) return Fail("not a single JSON object: " + Preview(raw));
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return Fail("invalid JSON: " + ex.Message.Split('.')[0]); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("root is not an object");
            var names = root.EnumerateObject().Select(p => p.Name).ToList();
            var required = names.Where(n => !OptionalRootKeys.Contains(n)).ToList();
            if (required.Count != RootKeys.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Count || !required.All(RootKeys.Contains))
                return Fail("root keys are not exactly " + string.Join(", ", RootKeys) + " (got " + string.Join(", ", names) + ")");

            var dropped = 0;
            var claims = new List<AnalysisClaim>();
            if (!Objects(root, "claims", ["quote", "claim"], out var claimItems)) return Fail("claims do not match the schema");
            foreach (var c in claimItems)
            {
                var quote = c["quote"];
                if (IsVerbatim(quote, answer)) claims.Add(new AnalysisClaim(Clip(quote), c["claim"]));
                else dropped++;
            }

            // Withdrawn earlier claims (GAD-T441): the quote must be the student's own words withdrawing it.
            var retractions = new List<AnalysisClaim>();
            if (root.TryGetProperty("retractions", out _))
            {
                if (!Objects(root, "retractions", ["quote", "claim"], out var retractionItems)) return Fail("retractions do not match the schema");
                foreach (var r in retractionItems)
                {
                    if (IsVerbatim(r["quote"], answer)) retractions.Add(new AnalysisClaim(Clip(r["quote"]), r["claim"]));
                    else dropped++;
                }
            }

            var misconceptions = new List<AnalysisMisconception>();
            if (!Objects(root, "misconceptions", ["objective", "misconception", "quote"], out var misItems)) return Fail("misconceptions do not match the schema");
            foreach (var m in misItems)
            {
                if (!objectiveKeys.Contains(m["objective"])) return Fail($"unknown objective '{m["objective"]}'");
                if (IsVerbatim(m["quote"], answer)) misconceptions.Add(new AnalysisMisconception(m["objective"], m["misconception"], Clip(m["quote"])));
                else dropped++;
            }

            var uncertainties = new List<string>();
            var u = root.GetProperty("uncertainties");
            if (u.ValueKind != JsonValueKind.Array) return Fail("uncertainties is not an array");
            foreach (var x in u.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String) return Fail("an uncertainty is not a string");
                var text = x.GetString()!.Trim();
                if (text.Length == 0) return Fail("an uncertainty is empty");
                uncertainties.Add(text);
            }

            var evidence = new List<ObjectiveEvidence>();
            if (!Objects(root, "evidence", ["objective", "stance", "dimension", "quote", "note"], out var evItems)) return Fail("evidence does not match the schema");
            foreach (var e in evItems)
            {
                if (!objectiveKeys.Contains(e["objective"])) return Fail($"unknown objective '{e["objective"]}'");
                if (ParseStance(e["stance"]) is not { } stance) return Fail($"unknown stance '{e["stance"]}'");
                if (ParseDimension(e["dimension"]) is not { } dimension) return Fail($"unknown dimension '{e["dimension"]}'");
                if (IsVerbatim(e["quote"], answer)) evidence.Add(new ObjectiveEvidence(e["objective"], stance, dimension, Clip(e["quote"]), e["note"]));
                else dropped++;
            }

            var revealed = root.GetProperty("questionRevealedAnswer");
            if (revealed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Fail("questionRevealedAnswer is not a boolean");

            return new ResponseAnalysis
            {
                Claims = claims, Retractions = retractions, Misconceptions = misconceptions, Uncertainties = uncertainties, Evidence = evidence,
                QuestionRevealedAnswer = revealed.GetBoolean(), Voters = 1, DroppedItems = dropped,
            };
        }
    }
    }

    /// <summary>
    /// Majority merge of valid votes: a finding stands when more than half the valid voters made it (one voter
    /// alone suffices only when it is the only valid voter). Deterministic for the same votes in the same order.
    /// </summary>
    public static ResponseAnalysis Merge(IReadOnlyList<ResponseAnalysis> votes, int rejected)
    {
        if (votes.Count == 0) return new ResponseAnalysis { RejectedVotes = rejected };
        var needed = votes.Count == 1 ? 1 : votes.Count / 2 + 1;

        var claims = Cluster(votes, v => v.Claims, (a, b) => QuotesOverlap(a.Quote, b.Quote) || PanelJson.Similarity(a.Text, b.Text) >= 0.5, needed).Select(c => c.First).ToList();
        var retractions = Cluster(votes, v => v.Retractions, (a, b) => QuotesOverlap(a.Quote, b.Quote) || PanelJson.Similarity(a.Text, b.Text) >= 0.5, needed)
            .Select(c => c.First).ToList();
        var misconceptions = Cluster(votes, v => v.Misconceptions,
            (a, b) => a.ObjectiveKey == b.ObjectiveKey && PanelJson.Similarity(a.Text, b.Text) >= 0.4, needed).Select(c => c.First).ToList();
        var uncertainties = Cluster(votes, v => v.Uncertainties, (a, b) => PanelJson.Similarity(a, b) >= 0.35, needed).Select(c => c.First).ToList();
        var evidence = Cluster(votes, v => v.Evidence,
                (a, b) => a.ObjectiveKey == b.ObjectiveKey && a.Polarity == b.Polarity && QuotesOverlap(a.Quote, b.Quote), needed)
            .Select(c =>
            {
                // The dimension most voters gave (ties: the first named).
                var dimension = c.Members.GroupBy(m => m.Dimension).Select((g, i) => (g.Key, Count: g.Count(), i))
                    .OrderByDescending(x => x.Count).ThenBy(x => x.i).First().Key;
                return c.First with { Dimension = dimension, Support = c.Voters };
            }).ToList();

        return new ResponseAnalysis
        {
            Claims = claims,
            Retractions = retractions,
            Misconceptions = misconceptions,
            Uncertainties = uncertainties,
            Evidence = evidence,
            QuestionRevealedAnswer = votes.Count(v => v.QuestionRevealedAnswer) >= needed,
            Voters = votes.Count,
            RejectedVotes = rejected,
            DroppedItems = votes.Sum(v => v.DroppedItems),
        };
    }

    private sealed record Group<T>(T First, List<T> Members, HashSet<int> VoterSet)
    {
        public int Voters => VoterSet.Count;
    }

    private static List<Group<T>> Cluster<T>(IReadOnlyList<ResponseAnalysis> votes, Func<ResponseAnalysis, IReadOnlyList<T>> items,
        Func<T, T, bool> same, int needed)
    {
        var groups = new List<Group<T>>();
        for (var v = 0; v < votes.Count; v++)
        {
            foreach (var item in items(votes[v]))
            {
                var g = groups.FirstOrDefault(g => same(g.First, item));
                if (g is null) groups.Add(new Group<T>(item, [item], [v]));
                else
                {
                    g.Members.Add(item);
                    g.VoterSet.Add(v);
                }
            }
        }
        return groups.Where(g => g.Voters >= needed).ToList();
    }

    /// <summary>True when <paramref name="quote"/> is a span of <paramref name="answer"/>, ignoring case, punctuation and spacing.</summary>
    public static bool IsVerbatim(string? quote, string? answer)
    {
        var q = Normalize(quote);
        if (q.Length < 2) return false;
        return Normalize(answer).Contains(q, StringComparison.Ordinal);
    }

    private static bool QuotesOverlap(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        return x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal) || PanelJson.Similarity(a, b) >= 0.5;
    }

    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    private static string Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? "" : " " + NonWord.Replace(s.ToLowerInvariant(), " ").Trim() + " ";

    private static string Clip(string quote)
    {
        var q = quote.Trim();
        return q.Length <= MaxQuote ? q : q[..MaxQuote];
    }

    private static string Preview(string? raw)
    {
        var t = (raw ?? "").Trim().Replace('\n', ' ');
        return t.Length == 0 ? "(empty)" : t.Length <= 80 ? $"\"{t}\"" : $"\"{t[..40]}...{t[^40..]}\"";
    }

    /// <summary>Strips one surrounding code fence; null when what remains is not a single JSON object text.</summary>
    private static string? StripFence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = t.IndexOf('\n');
            if (firstLine < 0 || !t.EndsWith("```", StringComparison.Ordinal)) return null;
            t = t[(firstLine + 1)..^3].Trim();
        }
        return t.StartsWith('{') && t.EndsWith('}') ? t : null;
    }

    /// <summary>The array <paramref name="name"/> of objects having exactly <paramref name="keys"/>, all non-empty strings.</summary>
    private static bool Objects(JsonElement root, string name, string[] keys, out List<Dictionary<string, string>> items)
    {
        items = [];
        var arr = root.GetProperty(name);
        if (arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) return false;
            var props = el.EnumerateObject().ToList();
            if (props.Count != keys.Length) return false;
            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in props)
            {
                if (!keys.Contains(p.Name) || item.ContainsKey(p.Name) || p.Value.ValueKind != JsonValueKind.String) return false;
                var value = p.Value.GetString()!.Trim();
                if (value.Length == 0) return false;
                item[p.Name] = value;
            }
            items.Add(item);
        }
        return true;
    }

    public static EvidencePolarity? ParseStance(string s) => s.Trim().ToLowerInvariant() switch
    {
        "supports" => EvidencePolarity.Supports,
        "contradicts" => EvidencePolarity.Contradicts,
        "misconception" => EvidencePolarity.Misconception,
        "uncertain" => EvidencePolarity.Uncertain,
        _ => null,
    };

    public static AssessmentDimension? ParseDimension(string s)
    {
        var key = s.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var d in Enum.GetValues<AssessmentDimension>())
            if (d != AssessmentDimension.Custom && string.Equals(d.ToString(), key, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    public static string DimensionName(AssessmentDimension d) =>
        Regex.Replace(d.ToString(), "(?<=[a-z])([A-Z])", "-$1").ToLowerInvariant();
}
