using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>Everything the judge needs for one answer.</summary>
public sealed record JudgeInput
{
    public required Turn Turn { get; init; }
    public required ConceptEntry Concept { get; init; }
    public required ConceptRubric Rubric { get; init; }
    public required IReadOnlyList<SourcePassage> Passages { get; init; }
    /// <summary>The student's earlier claims on this concept, prompt-ready.</summary>
    public string EarlierClaims { get; init; } = "";
}

/// <summary>
/// Judges an answer with the Legion panel (GAD-T36): every provider grades the four criteria
/// against the numbered source passages and the rubric, citing passages; Gadfly averages the
/// scores, keeps each voter's verdict for the reviewer, and measures agreement. Low agreement
/// marks the answer for human review instead of letting a split panel decide.
/// </summary>
public sealed class PanelAnswerJudge(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    public static readonly IReadOnlyDictionary<string, double> CriterionWeights = new Dictionary<string, double>
    {
        ["accuracy"] = 0.35,
        ["reasoning"] = 0.25,
        ["completeness"] = 0.25,
        ["consistency"] = 0.15,
    };

    /// <summary>Agreement below which the answer needs a human look.</summary>
    public double ReviewBelowAgreement { get; init; } = 0.6;

    public async Task<AnswerJudgment> JudgeAsync(JudgeInput input, int rubricVersion, CancellationToken ct = default)
    {
        var prompt = Prompt(input);
        var replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 1500, temperature: 0.1, ct: ct);
        return Aggregate(input, rubricVersion, replies.Where(r => !r.IsError).Select(r => (r.ProviderId, r.Model, r.Text)).ToList());
    }

    /// <summary>What the provisional judge adds to the judge prompt (GAD-T508), outside the prompt set: an aporia flag in the same reply.</summary>
    public const string ProvisionalAporiaInstruction =
        "Also include a top-level boolean field \"aporia\": true only when the student's answer is an honest statement that they do not know, with no attempt at an answer; otherwise false.";

    /// <summary>
    /// The live turn's provisional judgment (GAD-T508): the same judge prompt and rubric, answered by one fast model instead of the
    /// panel, aggregated exactly like a one-member panel. It steers the next move only; the panel's <see cref="JudgeAsync"/> still
    /// grades. Returns null when the model fails or gives no usable verdict, so the caller falls back to the panel on the turn.
    /// </summary>
    public async Task<(AnswerJudgment Judgment, bool Aporia)?> JudgeWithAsync(ILiveTurnModel model, JudgeInput input, int rubricVersion,
        CancellationToken ct = default)
    {
        string reply;
        try
        {
            using var kind = PanelUsage.UseLiveKind("live-judge");
            reply = await model.CompleteAsync(PanelJson.JsonOnlySystem, Prompt(input) + "\n\n" + ProvisionalAporiaInstruction,
                maxTokens: 1500, temperature: 0.1, ct: ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        var judgment = Aggregate(input, rubricVersion, [(model.ProviderId, model.Model, reply)]);
        if (judgment.Votes.Count == 0) return null;
        using var doc = PanelJson.ParseObject(reply);
        var aporia = doc is not null && doc.RootElement.TryGetProperty("aporia", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True;
        return (judgment, aporia);
    }

    private string Prompt(JudgeInput input)
    {
        var turn = input.Turn;
        if (turn.Answer is null) throw new ArgumentException("The turn has no answer to judge.", nameof(input));
        return ExamPrompts.Fill(_prompts[ExamPrompts.JudgeKey], new Dictionary<string, string?>
        {
            ["move"] = turn.Move.ToString(),
            ["concept"] = $"{input.Concept.Title}: {input.Concept.Definition}",
            ["rubric"] = input.Rubric.Describe(),
            ["claims"] = string.IsNullOrWhiteSpace(input.EarlierClaims) ? "(none yet)" : ExamPrompts.Student(input.EarlierClaims),
            ["passages"] = PanelJson.Number(input.Passages),
            ["question"] = turn.Question,
            ["answer"] = ExamPrompts.Student(turn.Answer.Text),
        });
    }

    /// <summary>Scores, agreement and the reviewer's summary from the judges' replies (each a JSON verdict; unusable ones are skipped).</summary>
    private AnswerJudgment Aggregate(JudgeInput input, int rubricVersion, IReadOnlyList<(string ProviderId, string Model, string Text)> replies)
    {
        var turn = input.Turn;
        var votes = new List<(string Provider, Dictionary<string, (double Score, string Rationale, List<string> Refs)> Criteria, string Verdict, string Model)>();
        foreach (var reply in replies)
        {
            using var doc = PanelJson.ParseObject(reply.Text);
            if (doc is null) continue;
            var parsed = new Dictionary<string, (double, string, List<string>)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in PanelJson.Arr(doc.RootElement, "criteria"))
            {
                var name = PanelJson.Str(c, "criterion").ToLowerInvariant();
                if (!CriterionWeights.ContainsKey(name)) continue;
                var score = Math.Clamp(PanelJson.Num(c, "score", double.NaN), 0, 10);
                if (double.IsNaN(score)) continue;
                parsed[name] = (score / 10.0, PanelJson.Str(c, "rationale"), PanelJson.PassageRefs(c, input.Passages));
            }
            if (parsed.Count == 0) continue;
            votes.Add((reply.ProviderId, parsed, PanelJson.Str(doc.RootElement, "verdict"), reply.Model));
        }

        if (votes.Count == 0)
        {
            return new AnswerJudgment
            {
                Criteria = [],
                OverallScore = 0,
                Agreement = 0,
                NeedsHumanReview = true,
                RubricVersion = rubricVersion,
                Summary = "No judge returned a usable verdict; this answer needs a human grade.",
            };
        }

        double Overall(Dictionary<string, (double Score, string, List<string>)> crit)
        {
            double sum = 0, weight = 0;
            foreach (var (name, w) in CriterionWeights)
                if (crit.TryGetValue(name, out var v)) { sum += v.Score * w; weight += w; }
            return weight == 0 ? 0 : sum / weight;
        }

        var overalls = votes.Select(v => Overall(v.Criteria)).ToList();
        var mean = overalls.Average();
        var spread = Math.Sqrt(overalls.Sum(o => (o - mean) * (o - mean)) / overalls.Count);
        var agreement = votes.Count == 1 ? 1.0 : Math.Clamp(1 - spread / 0.35, 0, 1);

        var criteria = CriterionWeights.Keys.Select(name =>
        {
            var given = votes.Where(v => v.Criteria.ContainsKey(name)).Select(v => v.Criteria[name]).ToList();
            if (given.Count == 0) return null;
            var refs = given.SelectMany(g => g.Refs).GroupBy(r => r).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            var meanScore = given.Average(g => g.Score);
            var rationale = given.OrderBy(g => Math.Abs(g.Score - meanScore)).First().Rationale;
            return new CriterionScore
            {
                Criterion = name,
                Score = Math.Round(meanScore, 3),
                Rationale = rationale,
                Citations = refs.Select(r => new Citation(r, Quote(input.Passages, r))).ToList(),
            };
        }).Where(c => c is not null).Select(c => c!).ToList();

        var representative = votes.Zip(overalls).OrderBy(x => Math.Abs(x.Second - mean)).First().First;
        // An answer written at the grader (asking for marks, "ignore the rubric") never stands on the panel's
        // word alone, however the judges scored it (GAD-T77).
        var addressesGrader = ExamPrompts.AddressesGrader(turn.Answer!.Text);
        return new AnswerJudgment
        {
            Criteria = criteria,
            OverallScore = Math.Round(mean, 3),
            Votes = votes.Zip(overalls).Select(x => new JudgeVote(x.First.Provider, x.First.Model, Math.Round(x.Second, 3), x.First.Verdict)).ToList(),
            Agreement = Math.Round(agreement, 3),
            NeedsHumanReview = agreement < ReviewBelowAgreement || addressesGrader,
            RubricVersion = rubricVersion,
            Summary = addressesGrader
                ? "This answer contains text addressed to the examiner or judges; check it by hand. " + representative.Verdict
                : representative.Verdict,
        };
    }

    private static string? Quote(IReadOnlyList<SourcePassage> passages, string id)
    {
        var text = passages.FirstOrDefault(p => p.Id == id)?.Text;
        return text is null ? null : text.Length <= 240 ? text : text[..240] + "...";
    }
}
