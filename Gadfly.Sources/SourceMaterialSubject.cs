using Gadfly.Engine;

namespace Gadfly.Sources;

/// <summary>
/// An exam subject built from instructor source material: the collection's concept map supplies
/// the concepts, the rubrics say what understanding looks like, and retrieval supplies grounding.
/// </summary>
public sealed class SourceMaterialSubject : ISubjectMatter
{
    private readonly ConceptMap _map;
    private readonly IReadOnlyDictionary<string, ConceptRubric> _rubrics;

    public SourceMaterialSubject(string title, ConceptMap map, IReadOnlyList<ConceptRubric> rubrics, GroundingRetriever retriever)
    {
        Title = title;
        _map = map;
        _rubrics = rubrics.ToDictionary(r => r.ConceptKey, StringComparer.Ordinal);
        Retriever = retriever;
    }

    public string Id => _map.CollectionId;
    public string Title { get; }
    /// <summary>
    /// The exam prompt set (<see cref="ExamPrompts.Set"/>, with the objective-driven prompts of GAD-T89..T93), in the attempt's
    /// language (GAD-T535): the English set itself for an English attempt by an English-primary student.
    /// </summary>
    public PromptSet PromptSet => ExamPromptLanguages.For(ExamPrompts.Set, Language);

    /// <summary>The attempt's languages (GAD-T534). The source material, concept map and rubric stay in their own language.</summary>
    public AttemptLanguage Language { get; init; } = AttemptLanguage.Default;

    /// <summary>This subject for an attempt in <paramref name="language"/>: the same map, rubrics and retrieval.</summary>
    public SourceMaterialSubject ForLanguage(AttemptLanguage language) =>
        language == Language ? this : new SourceMaterialSubject(Title, _map, _rubrics.Values.ToList(), Retriever)
        {
            GroundingPassages = GroundingPassages,
            Language = language,
        };
    public GroundingRetriever Retriever { get; }
    public ConceptMap Map => _map;

    /// <summary>Most passages in one grounding.</summary>
    public int GroundingPassages { get; init; } = 6;

    public ConceptEntry Concept(string key) =>
        _map.Find(key) ?? throw new KeyNotFoundException($"Concept '{key}' is not in collection '{Id}'.");

    public ConceptRubric Rubric(string key) =>
        _rubrics.TryGetValue(key, out var r) ? r : new ConceptRubric { ConceptKey = key };

    public Task<IReadOnlyList<PlannerConcept>> GetConceptsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PlannerConcept> concepts = _map.Concepts.Select(c => new PlannerConcept(c.Key,
            _map.Relations.Where(r => r.From == c.Key).Select(r => r.To)
                .Concat(_map.Relations.Where(r => r.To == c.Key).Select(r => r.From))
                .Distinct().ToList())).ToList();
        return Task.FromResult(concepts);
    }

    /// <summary>The passages for a concept (its cited ones first, then retrieval hits, focused by the answer when given).</summary>
    public async Task<IReadOnlyList<SourcePassage>> PassagesAsync(string conceptId, string? answer, CancellationToken ct = default)
    {
        var concept = Concept(conceptId);
        var passages = Retriever.ById(concept.PassageIds).Take(GroundingPassages / 2).ToList();
        var query = answer is { Length: > 0 } ? $"{concept.Title}. {answer}" : $"{concept.Title}. {concept.Definition}";
        foreach (var hit in await Retriever.SearchAsync(query, GroundingPassages, ct))
            if (passages.Count < GroundingPassages && passages.All(p => p.Id != hit.Passage.Id)) passages.Add(hit.Passage);
        return passages;
    }

    public async Task<GroundingContext> GetGroundingAsync(string conceptId, string? answer = null, CancellationToken ct = default)
    {
        var passages = await PassagesAsync(conceptId, answer, ct);
        return new GroundingContext(PanelJson.Number(passages), passages.Select(p => new Citation(p.Id)).ToList());
    }
}
