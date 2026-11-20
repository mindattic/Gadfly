using System.Text.Json;

namespace Gadfly.Sources;

/// <summary>
/// The CLI's source store: one folder per collection under
/// <c>%APPDATA%\MindAttic\Gadfly\sources\&lt;collection&gt;\</c> holding <c>collection.json</c>
/// and one <c>&lt;documentId&gt;.passages.json</c> per document. Writes go to a temp file first,
/// so an interrupted ingest never leaves a half-written document.
/// </summary>
public sealed class FileSourceStore(string? root = null) : ISourceStore
{
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "sources");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _root = root ?? DefaultRoot;

    public string CollectionDirectory(string collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId) || collectionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || collectionId is "." or "..")
            throw new ArgumentException($"'{collectionId}' is not a usable collection id.", nameof(collectionId));
        return Path.Combine(_root, collectionId);
    }

    public async Task<SourceCollectionInfo?> GetCollectionAsync(string collectionId, CancellationToken ct = default)
    {
        var file = Path.Combine(CollectionDirectory(collectionId), "collection.json");
        if (!File.Exists(file)) return null;
        await using var s = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<SourceCollectionInfo>(s, Json, ct);
    }

    public async Task<SourceCollectionInfo> EnsureCollectionAsync(string collectionId, string title, CancellationToken ct = default)
    {
        var existing = await GetCollectionAsync(collectionId, ct);
        if (existing is not null) return existing;
        var created = new SourceCollectionInfo { Id = collectionId, Title = title };
        await WriteAsync(Path.Combine(CollectionDirectory(collectionId), "collection.json"), created, ct);
        return created;
    }

    public async Task SaveDocumentAsync(string collectionId, SourceDocumentInfo document, IReadOnlyList<SourcePassage> passages, CancellationToken ct = default)
    {
        var dir = CollectionDirectory(collectionId);
        await WriteAsync(Path.Combine(dir, $"{document.Id}.passages.json"), passages, ct);
        var collection = await GetCollectionAsync(collectionId, ct)
                         ?? throw new InvalidOperationException($"Collection '{collectionId}' does not exist.");
        var docs = collection.Documents.Where(d => d.Id != document.Id).Append(document).OrderBy(d => d.FileName, StringComparer.Ordinal).ToList();
        await WriteAsync(Path.Combine(dir, "collection.json"), collection with { Documents = docs }, ct);
    }

    public async Task<IReadOnlyList<SourcePassage>> GetPassagesAsync(string collectionId, CancellationToken ct = default)
    {
        var collection = await GetCollectionAsync(collectionId, ct);
        if (collection is null) return [];
        var all = new List<SourcePassage>();
        foreach (var doc in collection.Documents)
        {
            var file = Path.Combine(CollectionDirectory(collectionId), $"{doc.Id}.passages.json");
            if (!File.Exists(file)) continue;
            await using var s = File.OpenRead(file);
            var passages = await JsonSerializer.DeserializeAsync<List<SourcePassage>>(s, Json, ct);
            if (passages is not null) all.AddRange(passages);
        }
        return all;
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
