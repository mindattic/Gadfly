using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gadfly.Sources;

/// <summary>One concept a student must understand, grounded in cited passages.</summary>
public sealed record ConceptEntry
{
    /// <summary>Stable kebab-case key; survives re-extraction so sessions and edits keep pointing at it.</summary>
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Definition { get; init; } = "";
    public IReadOnlyList<string> KeyFacts { get; init; } = [];
    public IReadOnlyList<string> Misconceptions { get; init; } = [];
    public IReadOnlyList<string> PassageIds { get; init; } = [];
    /// <summary>True once an instructor changed it; re-extraction then leaves it alone.</summary>
    public bool EditedByInstructor { get; init; }
}

public sealed record ConceptRelationEntry(string From, string To, string Kind);

/// <summary>A collection's concept map: what an exam on it can ask about.</summary>
public sealed record ConceptMap
{
    public required string CollectionId { get; init; }
    public int Version { get; init; } = 1;
    public IReadOnlyList<ConceptEntry> Concepts { get; init; } = [];
    public IReadOnlyList<ConceptRelationEntry> Relations { get; init; } = [];

    public ConceptEntry? Find(string key) => Concepts.FirstOrDefault(c => c.Key == key);
}

/// <summary>One rubric line, with the passages it rests on.</summary>
public sealed record RubricItem(string Text, double Weight, IReadOnlyList<string> PassageIds);

/// <summary>How an answer about one concept is judged.</summary>
public sealed record ConceptRubric
{
    public required string ConceptKey { get; init; }
    public int Version { get; init; } = 1;
    public IReadOnlyList<RubricItem> Understanding { get; init; } = [];
    public IReadOnlyList<RubricItem> AcceptableVariants { get; init; } = [];
    public IReadOnlyList<RubricItem> Misconceptions { get; init; } = [];
    public bool EditedByInstructor { get; init; }

    /// <summary>Prompt-ready text of the rubric.</summary>
    public string Describe()
    {
        var lines = new List<string>();
        lines.AddRange(Understanding.Select(u => $"- (weight {u.Weight:0.#}) {u.Text}"));
        if (AcceptableVariants.Count > 0)
        {
            lines.Add("Also acceptable:");
            lines.AddRange(AcceptableVariants.Select(v => $"- {v.Text}"));
        }
        if (Misconceptions.Count > 0)
        {
            lines.Add("Misconceptions to watch for:");
            lines.AddRange(Misconceptions.Select(m => $"- {m.Text}"));
        }
        return string.Join("\n", lines);
    }
}

/// <summary>Where a collection's concept map and rubrics are kept: files for the CLI, SQL for the platform.</summary>
public interface IConceptMapStore
{
    Task<ConceptMap?> LoadMapAsync(string collectionId, CancellationToken ct = default);
    Task SaveMapAsync(ConceptMap map, CancellationToken ct = default);
    Task<IReadOnlyList<ConceptRubric>> LoadRubricsAsync(string collectionId, CancellationToken ct = default);
    Task SaveRubricsAsync(string collectionId, IReadOnlyList<ConceptRubric> rubrics, CancellationToken ct = default);
}

/// <summary>Reads and writes a collection's concept map and rubrics beside its passages (file store).</summary>
public sealed class ConceptMapFiles(FileSourceStore store) : IConceptMapStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string MapPath(string id) => Path.Combine(store.CollectionDirectory(id), "concept-map.json");
    private string RubricPath(string id) => Path.Combine(store.CollectionDirectory(id), "rubrics.json");

    public Task<ConceptMap?> LoadMapAsync(string collectionId, CancellationToken ct = default) => ReadAsync<ConceptMap>(MapPath(collectionId), ct);
    public Task SaveMapAsync(ConceptMap map, CancellationToken ct = default) => WriteAsync(MapPath(map.CollectionId), map, ct);

    public async Task<IReadOnlyList<ConceptRubric>> LoadRubricsAsync(string collectionId, CancellationToken ct = default) =>
        await ReadAsync<List<ConceptRubric>>(RubricPath(collectionId), ct) ?? [];

    public Task SaveRubricsAsync(string collectionId, IReadOnlyList<ConceptRubric> rubrics, CancellationToken ct = default) =>
        WriteAsync(RubricPath(collectionId), rubrics, ct);

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(s, Json, ct);
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var s = File.Create(tmp))
            await JsonSerializer.SerializeAsync(s, value, Json, ct);
        File.Move(tmp, path, overwrite: true);
    }
}
