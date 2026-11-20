using System.Text.Json;
using Gadfly.Engine;
using Gadfly.Socrates;

namespace Gadfly.Sources;

/// <summary>
/// Builds a collection's concept map with the Legion panel (GAD-T30). Map-reduce so long books
/// work: passages go to the panel in batches; within a batch, a concept is kept when a majority of
/// the providers that answered found it; across batches, same-key or similarly-titled concepts
/// merge. Re-extraction keeps instructor-edited concepts untouched and reuses earlier keys for
/// concepts it finds again, so exams and edits keep pointing at the same concepts.
/// </summary>
public sealed class ConceptMapBuilder(ISocraticVotingClient panel, PromptSet? prompts = null)
{
    private readonly PromptSet _prompts = prompts ?? ExamPrompts.Set;

    /// <summary>Characters of passage text per panel call.</summary>
    public int BatchChars { get; init; } = 14000;

    /// <summary>Cap on concepts kept (most-supported first); 0 = no cap.</summary>
    public int MaxConcepts { get; init; } = 40;

    public async Task<ConceptMap> BuildAsync(string collectionId, IReadOnlyList<SourcePassage> passages, ConceptMap? previous = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var batches = Batch(passages);
        var found = new List<(ConceptEntry Concept, int Support)>();
        var relations = new List<(string FromTitle, string ToTitle, string Kind)>();

        for (var b = 0; b < batches.Count; b++)
        {
            progress?.Report($"Extracting concepts: batch {b + 1} of {batches.Count}");
            var batch = batches[b];
            var prompt = ExamPrompts.Fill(_prompts[ExamPrompts.ConceptMapKey],
                new Dictionary<string, string?> { ["passages"] = PanelJson.Number(batch) });
            var replies = await panel.FanOutAsync(PanelJson.JsonOnlySystem, prompt, maxTokens: 6000, ct: ct);
            var parsed = replies.Where(r => !r.IsError).Select(r => PanelJson.ParseObject(r.Text)).Where(d => d is not null).ToList();
            if (parsed.Count == 0) continue;
            var needed = parsed.Count <= 2 ? 1 : (parsed.Count + 1) / 2;

            var batchConcepts = new List<(ConceptEntry Concept, int Support)>();
            foreach (var doc in parsed)
            {
                using var _ = doc;
                var root = doc!.RootElement;
                var keyByLocal = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var c in PanelJson.Arr(root, "concepts"))
                {
                    var title = PanelJson.Str(c, "title");
                    if (title.Length == 0) continue;
                    var entry = new ConceptEntry
                    {
                        Key = PanelJson.Slug(PanelJson.Str(c, "key") is { Length: > 0 } k ? k : title),
                        Title = title,
                        Definition = PanelJson.Str(c, "definition"),
                        KeyFacts = PanelJson.Strings(c, "keyFacts"),
                        Misconceptions = PanelJson.Strings(c, "misconceptions"),
                        PassageIds = PanelJson.PassageRefs(c, batch),
                    };
                    keyByLocal[PanelJson.Str(c, "key")] = title;
                    MergeInto(batchConcepts, entry);
                }
                foreach (var r in PanelJson.Arr(root, "relations"))
                {
                    var from = keyByLocal.GetValueOrDefault(PanelJson.Str(r, "from"));
                    var to = keyByLocal.GetValueOrDefault(PanelJson.Str(r, "to"));
                    var kind = PanelJson.Str(r, "kind");
                    if (from is not null && to is not null && kind.Length > 0) relations.Add((from, to, kind));
                }
            }
            foreach (var (concept, support) in batchConcepts.Where(x => x.Support >= needed))
                MergeInto(found, concept, support);
        }

        var ranked = found.OrderByDescending(x => x.Support).ThenByDescending(x => x.Concept.PassageIds.Count).Select(x => x.Concept);
        if (MaxConcepts > 0) ranked = ranked.Take(MaxConcepts);
        var concepts = Reconcile(ranked.ToList(), previous);

        var rel = relations
            .Select(r => (From: Resolve(concepts, r.FromTitle), To: Resolve(concepts, r.ToTitle), r.Kind))
            .Where(r => r.From is not null && r.To is not null && r.From != r.To)
            .Select(r => new ConceptRelationEntry(r.From!, r.To!, r.Kind))
            .DistinctBy(r => (r.From, r.To, r.Kind))
            .ToList();
        if (previous is not null)
            rel.AddRange(previous.Relations.Where(r => concepts.Any(c => c.Key == r.From) && concepts.Any(c => c.Key == r.To)
                                                    && !rel.Any(x => x.From == r.From && x.To == r.To && x.Kind == r.Kind)));

        return new ConceptMap
        {
            CollectionId = collectionId,
            Version = (previous?.Version ?? 0) + 1,
            Concepts = concepts,
            Relations = rel,
        };
    }

    private List<List<SourcePassage>> Batch(IReadOnlyList<SourcePassage> passages)
    {
        var batches = new List<List<SourcePassage>>();
        var current = new List<SourcePassage>();
        var size = 0;
        foreach (var p in passages)
        {
            if (current.Count > 0 && size + p.Text.Length > BatchChars)
            {
                batches.Add(current);
                current = [];
                size = 0;
            }
            current.Add(p);
            size += p.Text.Length;
        }
        if (current.Count > 0) batches.Add(current);
        return batches;
    }

    private static void MergeInto(List<(ConceptEntry Concept, int Support)> list, ConceptEntry entry, int support = 1)
    {
        var i = list.FindIndex(x => x.Concept.Key == entry.Key || PanelJson.Similarity(x.Concept.Title, entry.Title) >= 0.6);
        if (i < 0)
        {
            list.Add((entry, support));
            return;
        }
        var existing = list[i].Concept;
        list[i] = (existing with
        {
            Definition = existing.Definition.Length >= entry.Definition.Length ? existing.Definition : entry.Definition,
            KeyFacts = PanelJson.MergeDistinct(existing.KeyFacts.Concat(entry.KeyFacts)),
            Misconceptions = PanelJson.MergeDistinct(existing.Misconceptions.Concat(entry.Misconceptions)),
            PassageIds = existing.PassageIds.Concat(entry.PassageIds).Distinct().ToList(),
        }, list[i].Support + support);
    }

    /// <summary>Keeps instructor edits, reuses earlier keys, and makes keys unique.</summary>
    private static List<ConceptEntry> Reconcile(List<ConceptEntry> fresh, ConceptMap? previous)
    {
        var result = new List<ConceptEntry>();
        var edited = previous?.Concepts.Where(c => c.EditedByInstructor).ToList() ?? [];
        result.AddRange(edited);
        foreach (var c in fresh)
        {
            if (edited.Any(e => e.Key == c.Key || PanelJson.Similarity(e.Title, c.Title) >= 0.6)) continue;
            var prior = previous?.Concepts.FirstOrDefault(p => p.Key == c.Key || PanelJson.Similarity(p.Title, c.Title) >= 0.6);
            var key = prior?.Key ?? c.Key;
            var unique = key;
            for (var n = 2; result.Any(r => r.Key == unique); n++) unique = $"{key}-{n}";
            result.Add(c with { Key = unique });
        }
        return result;
    }

    private static string? Resolve(IReadOnlyList<ConceptEntry> concepts, string title) =>
        concepts.FirstOrDefault(c => PanelJson.Similarity(c.Title, title) >= 0.6)?.Key;
}
