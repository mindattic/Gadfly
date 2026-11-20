using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>
/// Writes each concept's rubric with the Legion panel (GAD-T31): the understanding points, the
/// acceptable variants and the misconceptions, each citing passages. Grounding is the concept's
/// own cited passages plus the best retrieval hits for its title and definition. Rubric items are
/// kept when a majority of answering providers proposed them (merged by similarity); weights are
/// averaged. Instructor-edited rubrics are never overwritten; a changed rubric gets a new version.
/// </summary>
public sealed class RubricBuilder(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    /// <summary>Most passages sent per concept.</summary>
    public int MaxPassages { get; init; } = 10;

    public async Task<IReadOnlyList<ConceptRubric>> BuildAsync(ConceptMap map, GroundingRetriever retriever,
        IReadOnlyList<ConceptRubric>? previous = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new List<ConceptRubric>();
        var i = 0;
        foreach (var concept in map.Concepts)
        {
            i++;
            var prior = previous?.FirstOrDefault(r => r.ConceptKey == concept.Key);
            if (prior is { EditedByInstructor: true })
            {
                result.Add(prior);
                continue;
            }
            progress?.Report($"Writing rubric {i} of {map.Concepts.Count}: {concept.Title}");
            var rubric = await BuildOneAsync(concept, retriever, ct);
            if (rubric is null)
            {
                if (prior is not null) result.Add(prior);
                continue;
            }
            var version = prior is null ? 1 : SameContent(prior, rubric) ? prior.Version : prior.Version + 1;
            result.Add(rubric with { Version = version });
        }
        return result;
    }

    public async Task<ConceptRubric?> BuildOneAsync(ConceptEntry concept, GroundingRetriever retriever, CancellationToken ct = default)
    {
        var passages = retriever.ById(concept.PassageIds).ToList();
        foreach (var hit in await retriever.SearchAsync($"{concept.Title}. {concept.Definition}", MaxPassages, ct))
            if (passages.Count < MaxPassages && passages.All(p => p.Id != hit.Passage.Id)) passages.Add(hit.Passage);
        if (passages.Count > MaxPassages) passages = passages.Take(MaxPassages).ToList();
        if (passages.Count == 0) return null;

        var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.RubricKey], new Dictionary<string, string?>
        {
            ["concept"] = concept.Title,
            ["definition"] = concept.Definition,
            ["passages"] = PanelJson.Number(passages),
        });
        var replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 3000, ct: ct);
        var docs = replies.Where(r => !r.IsError).Select(r => PanelJson.ParseObject(r.Text)).Where(d => d is not null).Select(d => d!).ToList();
        if (docs.Count == 0) return null;
        try
        {
            var needed = docs.Count <= 2 ? 1 : (docs.Count + 1) / 2;
            return new ConceptRubric
            {
                ConceptKey = concept.Key,
                Understanding = Merge(docs, "understanding", passages, needed, withWeight: true),
                AcceptableVariants = Merge(docs, "acceptableVariants", passages, needed, withWeight: false),
                Misconceptions = Merge(docs, "misconceptions", passages, needed, withWeight: false)
                    .Concat(concept.Misconceptions.Select(m => new RubricItem(m, 1, [])))
                    .DistinctBy(m => m.Text.ToLowerInvariant()).ToList(),
            };
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    private static List<RubricItem> Merge(IReadOnlyList<JsonDocument> docs, string property, IReadOnlyList<SourcePassage> passages,
        int needed, bool withWeight)
    {
        var merged = new List<(string Text, List<double> Weights, HashSet<string> Passages, int Support)>();
        foreach (var doc in docs)
        {
            foreach (var item in PanelJson.Arr(doc.RootElement, property))
            {
                var text = PanelJson.Str(item, "text");
                if (text.Length == 0) continue;
                var weight = Math.Clamp(PanelJson.Num(item, "weight", 1), 1, 3);
                var refs = PanelJson.PassageRefs(item, passages);
                var i = merged.FindIndex(m => PanelJson.Similarity(m.Text, text) >= 0.5);
                if (i < 0)
                {
                    merged.Add((text, [weight], [.. refs], 1));
                }
                else
                {
                    var m = merged[i];
                    m.Weights.Add(weight);
                    m.Passages.UnionWith(refs);
                    merged[i] = (m.Text.Length >= text.Length ? m.Text : text, m.Weights, m.Passages, m.Support + 1);
                }
            }
        }
        return merged.Where(m => m.Support >= needed)
            .Select(m => new RubricItem(m.Text, withWeight ? Math.Round(m.Weights.Average(), 1) : 1, m.Passages.ToList()))
            .ToList();
    }

    private static bool SameContent(ConceptRubric a, ConceptRubric b) =>
        a.Understanding.Select(x => x.Text).SequenceEqual(b.Understanding.Select(x => x.Text))
        && a.AcceptableVariants.Select(x => x.Text).SequenceEqual(b.AcceptableVariants.Select(x => x.Text))
        && a.Misconceptions.Select(x => x.Text).SequenceEqual(b.Misconceptions.Select(x => x.Text));
}
