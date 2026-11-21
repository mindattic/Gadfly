namespace Gadfly.Sources;

/// <summary>One passage of source material: the unit retrieval returns and judgments cite.</summary>
public sealed record SourcePassage
{
    /// <summary>Stable id: "{documentId}:{ordinal}".</summary>
    public required string Id { get; init; }
    public required string DocumentId { get; init; }
    public required int Ordinal { get; init; }
    public required string Text { get; init; }
    /// <summary>Human-readable location, e.g. "chapter 3 > Mitosis" or "page 12".</summary>
    public string? Location { get; init; }
    /// <summary>Optional embedding (unit-normalized) when an embedding provider was configured.</summary>
    public float[]? Embedding { get; init; }
}

/// <summary>A document in a collection.</summary>
public sealed record SourceDocumentInfo
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string Sha256 { get; init; }
    public required int PassageCount { get; init; }
    public required DateTimeOffset IngestedAt { get; init; }
}

/// <summary>A named body of source material an exam is grounded in.</summary>
public sealed record SourceCollectionInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<SourceDocumentInfo> Documents { get; init; } = [];
}

/// <summary>A block of extracted text with where it came from, before chunking.</summary>
public sealed record ExtractedBlock(string Text, string? Location, bool IsHeading = false);

/// <summary>
/// Turns a file into text blocks. <see cref="PlainTextExtractor"/> covers .txt and Markdown; PDF,
/// DOCX, EPUB and HTML are read by <see cref="TutorIngestionExtractor"/> over Tutor.Ingestion (TUT-T64).
/// </summary>
public interface IDocumentTextExtractor
{
    bool CanRead(string path);
    Task<IReadOnlyList<ExtractedBlock>> ExtractAsync(string path, CancellationToken ct = default);
}

/// <summary>Optional: turns passage text into vectors for semantic retrieval.</summary>
public interface IEmbeddingProvider
{
    string Model { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>
/// Where collections live: a file store for the CLI, the SQL store (Gadfly.Server's SourceChunk
/// table) for the platform.
/// </summary>
public interface ISourceStore
{
    Task<SourceCollectionInfo?> GetCollectionAsync(string collectionId, CancellationToken ct = default);
    Task<SourceCollectionInfo> EnsureCollectionAsync(string collectionId, string title, CancellationToken ct = default);
    /// <summary>Replaces any earlier version of the document (same id) with these passages.</summary>
    Task SaveDocumentAsync(string collectionId, SourceDocumentInfo document, IReadOnlyList<SourcePassage> passages, CancellationToken ct = default);
    Task<IReadOnlyList<SourcePassage>> GetPassagesAsync(string collectionId, CancellationToken ct = default);
}
