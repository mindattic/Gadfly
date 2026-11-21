using System.Security.Cryptography;
using System.Text;

namespace Gadfly.Sources;

/// <summary>What one ingest run did.</summary>
public sealed record IngestResult(string CollectionId, int DocumentsIngested, int DocumentsUnchanged,
    IReadOnlyList<string> Skipped, int PassagesWritten, IReadOnlyList<string>? Failed = null)
{
    /// <summary>One message per file that was recognised but could not be read (damaged, over a limit, no text).</summary>
    public IReadOnlyList<string> Failures { get; init; } = Failed ?? [];
}

/// <summary>
/// Ingests files into a collection: extract text, chunk it into passages of roughly
/// <see cref="TargetPassageChars"/> characters along paragraph boundaries (headings start a new
/// passage), optionally embed, and save. A file whose content hash is unchanged is skipped, so
/// re-running over the same folder is cheap.
/// </summary>
public sealed class SourceIngestor(ISourceStore store, IReadOnlyList<IDocumentTextExtractor> extractors,
    IEmbeddingProvider? embeddings = null, TimeProvider? clock = null)
{
    public int TargetPassageChars { get; init; } = 1200;
    public int MaxPassageChars { get; init; } = 2400;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Ingests a file, or every readable file under a folder, into <paramref name="collectionId"/>.</summary>
    public async Task<IngestResult> IngestAsync(string path, string collectionId, string? title = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : File.Exists(path) ? [path] : throw new FileNotFoundException($"No file or folder at {path}.", path);

        var collection = await store.EnsureCollectionAsync(collectionId, title ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), ct);
        var known = collection.Documents.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var root = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path))!;

        int ingested = 0, unchanged = 0, passagesWritten = 0;
        var skipped = new List<string>();
        var failures = new List<string>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var extractor = extractors.FirstOrDefault(e => e.CanRead(file));
            if (extractor is null)
            {
                skipped.Add(file);
                continue;
            }
            var relative = Path.GetRelativePath(root, Path.GetFullPath(file)).Replace('\\', '/');
            var docId = DocumentId(relative);
            var sha = await HashAsync(file, ct);
            if (known.TryGetValue(docId, out var existing) && existing.Sha256 == sha)
            {
                unchanged++;
                continue;
            }

            progress?.Report($"Reading {relative}");
            IReadOnlyList<ExtractedBlock> blocks;
            try
            {
                blocks = await extractor.ExtractAsync(file, ct);
            }
            catch (DocumentReadException ex)
            {
                // One bad file never stops the others; the message is already user-safe.
                failures.Add(ex.Message);
                progress?.Report(ex.Message);
                continue;
            }
            var texts = Chunk(blocks);
            float[][]? vectors = null;
            if (embeddings is not null && texts.Count > 0)
            {
                progress?.Report($"Embedding {texts.Count} passages from {relative}");
                vectors = [.. await embeddings.EmbedAsync(texts.Select(t => t.Text).ToList(), ct)];
                if (vectors.Length != texts.Count)
                {
                    // Indexing by position would misattribute or overrun: record it and leave the document unsaved so a rerun retries it (GAD-T196).
                    var msg = $"\"{relative}\": the embedding provider returned {vectors.Length} vectors for {texts.Count} passages, so the document was not ingested.";
                    failures.Add(msg);
                    progress?.Report(msg);
                    continue;
                }
            }
            var passages = texts.Select((t, i) => new SourcePassage
            {
                Id = $"{docId}:{i}",
                DocumentId = docId,
                Ordinal = i,
                Text = t.Text,
                Location = t.Location is null ? relative : $"{relative} > {t.Location}",
                Embedding = vectors is null ? null : Normalize(vectors[i]),
            }).ToList();

            var info = new SourceDocumentInfo
            {
                Id = docId, FileName = relative, Sha256 = sha, PassageCount = passages.Count, IngestedAt = _clock.GetUtcNow(),
            };
            await store.SaveDocumentAsync(collectionId, info, passages, ct);
            ingested++;
            passagesWritten += passages.Count;
        }
        return new IngestResult(collectionId, ingested, unchanged, skipped, passagesWritten) { Failures = failures };
    }

    /// <summary>Groups blocks into passages near the target size; a heading always starts a new passage.</summary>
    internal List<(string Text, string? Location)> Chunk(IReadOnlyList<ExtractedBlock> blocks)
    {
        var result = new List<(string, string?)>();
        var sb = new StringBuilder();
        string? location = null;

        void Flush()
        {
            var t = sb.ToString().Trim();
            if (t.Length > 0) result.Add((t, location));
            sb.Clear();
        }

        foreach (var block in blocks)
        {
            if (block.IsHeading)
            {
                Flush();
                location = block.Location;
                continue;
            }
            if (sb.Length > 0 && (sb.Length + block.Text.Length > TargetPassageChars || block.Location != location))
                Flush();
            location = block.Location;
            if (block.Text.Length > MaxPassageChars)
            {
                Flush();
                foreach (var piece in SplitLong(block.Text, TargetPassageChars))
                    result.Add((piece, location));
                continue;
            }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(block.Text);
        }
        Flush();
        return result;
    }

    /// <summary>Splits an over-long paragraph at sentence ends near <paramref name="size"/>.</summary>
    internal static IEnumerable<string> SplitLong(string text, int size)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + size);
            if (end < text.Length)
            {
                var cut = text.LastIndexOfAny(['.', '?', '!', ';'], end - 1, end - start);
                if (cut > start + size / 2) end = cut + 1;
            }
            var piece = text[start..end].Trim();
            if (piece.Length > 0) yield return piece;
            start = end;
        }
    }

    /// <summary>The id a document gets from its path relative to the ingested root (stable across runs).</summary>
    public static string DocumentId(string relativePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return "d" + Convert.ToHexStringLower(hash)[..12];
    }

    private static async Task<string> HashAsync(string file, CancellationToken ct)
    {
        await using var s = File.OpenRead(file);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
    }

    private static float[] Normalize(float[] v)
    {
        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }
}
