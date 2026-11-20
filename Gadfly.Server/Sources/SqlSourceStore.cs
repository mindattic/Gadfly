using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sources;

/// <summary>
/// The platform's <see cref="ISourceStore"/> over the SourceCollection / SourceDocument / SourceChunk
/// tables, scoped to one organization. Collection ids are the SourceCollection's Guid. Passage ids
/// read back are chunk Guids ("N" format), which is what concept citations and judgments cite.
/// </summary>
public sealed class SqlSourceStore(GadflyDbContext db, Guid organizationId, Guid actingUserId, TimeProvider? clock = null) : ISourceStore
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<SourceCollectionInfo?> GetCollectionAsync(string collectionId, CancellationToken ct = default)
    {
        var id = ParseId(collectionId);
        var c = await db.SourceCollections.AsNoTracking()
            .Where(x => x.Id == id && x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.Title })
            .FirstOrDefaultAsync(ct);
        if (c is null) return null;
        var docs = await db.SourceDocuments.AsNoTracking()
            .Where(d => d.CollectionId == id && d.OrganizationId == organizationId && d.Status == ProcessingStatus.Ready)
            .OrderBy(d => d.FileName)
            .Select(d => new { d.FileName, d.Sha256, d.IngestedAt, Count = db.SourceChunks.Count(ch => ch.DocumentId == d.Id) })
            .ToListAsync(ct);
        return new SourceCollectionInfo
        {
            Id = c.Id.ToString(),
            Title = c.Title,
            Documents = docs.Select(d => new SourceDocumentInfo
            {
                Id = SourceIngestor.DocumentId(d.FileName),
                FileName = d.FileName,
                Sha256 = d.Sha256,
                PassageCount = d.Count,
                IngestedAt = d.IngestedAt ?? default,
            }).ToList(),
        };
    }

    public async Task<SourceCollectionInfo> EnsureCollectionAsync(string collectionId, string title, CancellationToken ct = default)
    {
        var existing = await GetCollectionAsync(collectionId, ct);
        if (existing is not null) return existing;
        db.SourceCollections.Add(new SourceCollection
        {
            Id = ParseId(collectionId),
            OrganizationId = organizationId,
            Title = title,
            CreatedByUserId = actingUserId,
            CreatedAt = _clock.GetUtcNow(),
            Status = ProcessingStatus.Processing,
        });
        await db.SaveChangesAsync(ct);
        return new SourceCollectionInfo { Id = collectionId, Title = title };
    }

    public async Task SaveDocumentAsync(string collectionId, SourceDocumentInfo document, IReadOnlyList<SourcePassage> passages, CancellationToken ct = default)
    {
        var cid = ParseId(collectionId);
        var docGuid = StableGuid($"{cid:N}/{document.Id}");
        // SQL Server's retrying strategy replays the whole unit, so the transaction lives inside it.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.SourceChunks.Where(ch => ch.DocumentId == docGuid && ch.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
        var doc = await db.SourceDocuments.FirstOrDefaultAsync(d => d.Id == docGuid && d.OrganizationId == organizationId, ct);
        if (doc is null)
        {
            doc = new SourceDocument
            {
                Id = docGuid,
                OrganizationId = organizationId,
                CollectionId = cid,
                UploadedAt = _clock.GetUtcNow(),
                ContentType = ContentTypeFor(document.FileName),
            };
            db.SourceDocuments.Add(doc);
        }
        doc.FileName = document.FileName;
        doc.Sha256 = document.Sha256;
        doc.Status = ProcessingStatus.Ready;
        doc.Error = null;
        doc.IngestedAt = document.IngestedAt;

        foreach (var p in passages)
        {
            db.SourceChunks.Add(new SourceChunk
            {
                Id = StableGuid($"{docGuid:N}/{p.Ordinal}"),
                OrganizationId = organizationId,
                CollectionId = cid,
                DocumentId = docGuid,
                Ordinal = p.Ordinal,
                Text = p.Text,
                Location = p.Location is { Length: > 300 } l ? l[..300] : p.Location,
                TokenCount = p.Text.Length / 4,
                Embedding = p.Embedding is null ? null : ToBytes(p.Embedding),
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        });
    }

    public async Task<IReadOnlyList<SourcePassage>> GetPassagesAsync(string collectionId, CancellationToken ct = default)
    {
        var cid = ParseId(collectionId);
        var rows = await db.SourceChunks.AsNoTracking()
            .Where(ch => ch.CollectionId == cid && ch.OrganizationId == organizationId)
            .OrderBy(ch => ch.DocumentId).ThenBy(ch => ch.Ordinal)
            .ToListAsync(ct);
        return rows.Select(ch => new SourcePassage
        {
            Id = ch.Id.ToString("N"),
            DocumentId = ch.DocumentId.ToString("N"),
            Ordinal = ch.Ordinal,
            Text = ch.Text,
            Location = ch.Location,
            Embedding = ch.Embedding is null ? null : FromBytes(ch.Embedding),
        }).ToList();
    }

    internal static Guid ParseId(string collectionId) =>
        Guid.TryParse(collectionId, out var g) ? g : throw new ArgumentException($"'{collectionId}' is not a collection id.", nameof(collectionId));

    /// <summary>A Guid derived from text, so re-ingesting the same document or chunk reuses its id.</summary>
    internal static Guid StableGuid(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".md" or ".markdown" => "text/markdown",
        ".txt" or ".text" => "text/plain",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".epub" => "application/epub+zip",
        ".html" or ".htm" => "text/html",
        _ => "application/octet-stream",
    };

    private static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] b)
    {
        var v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, v.Length * sizeof(float));
        return v;
    }
}

/// <summary>
/// The platform's <see cref="IConceptMapStore"/> over the Concept, ConceptRelation, ConceptCitation
/// and RubricPoint tables. Concepts are matched by key; ones that disappear from a new map are
/// archived (never deleted) unless an instructor edited them; rubric versions accumulate so every
/// session can still show the rubric version that judged it.
/// </summary>
public sealed class SqlConceptMapStore(GadflyDbContext db, Guid organizationId) : IConceptMapStore
{
    public async Task<ConceptMap?> LoadMapAsync(string collectionId, CancellationToken ct = default)
    {
        var cid = SqlSourceStore.ParseId(collectionId);
        var collection = await db.SourceCollections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cid && c.OrganizationId == organizationId, ct);
        if (collection is null) return null;
        var concepts = await db.Concepts.AsNoTracking().Include(c => c.Citations)
            .Where(c => c.CollectionId == cid && c.OrganizationId == organizationId && !c.IsArchived)
            .OrderBy(c => c.Ordinal).ToListAsync(ct);
        if (concepts.Count == 0 && collection.ConceptMapVersion == 0) return null;
        var byId = concepts.ToDictionary(c => c.Id, c => c.Key);
        var relations = await db.ConceptRelations.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && byId.Keys.Contains(r.FromConceptId))
            .ToListAsync(ct);
        return new ConceptMap
        {
            CollectionId = collectionId,
            Version = collection.ConceptMapVersion,
            Concepts = concepts.Select(c => new ConceptEntry
            {
                Key = c.Key,
                Title = c.Title,
                Definition = c.Definition,
                KeyFacts = JsonSerializer.Deserialize<List<string>>(c.KeyFactsJson) ?? [],
                Misconceptions = JsonSerializer.Deserialize<List<string>>(c.MisconceptionsJson) ?? [],
                PassageIds = c.Citations.Select(x => x.ChunkId.ToString("N")).ToList(),
                EditedByInstructor = c.EditedByInstructor,
            }).ToList(),
            Relations = relations.Where(r => byId.ContainsKey(r.ToConceptId))
                .Select(r => new ConceptRelationEntry(byId[r.FromConceptId], byId[r.ToConceptId], r.Kind)).ToList(),
        };
    }

    public async Task SaveMapAsync(ConceptMap map, CancellationToken ct = default)
    {
        var cid = SqlSourceStore.ParseId(map.CollectionId);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var collection = await db.SourceCollections.FirstAsync(c => c.Id == cid && c.OrganizationId == organizationId, ct);
        var existing = await db.Concepts.Include(c => c.Citations)
            .Where(c => c.CollectionId == cid && c.OrganizationId == organizationId).ToListAsync(ct);
        var validChunks = (await db.SourceChunks.AsNoTracking().Where(ch => ch.CollectionId == cid && ch.OrganizationId == organizationId)
            .Select(ch => ch.Id).ToListAsync(ct)).ToHashSet();

        for (var i = 0; i < map.Concepts.Count; i++)
        {
            var entry = map.Concepts[i];
            var row = existing.FirstOrDefault(c => c.Key == entry.Key);
            if (row is null)
            {
                row = new Concept { OrganizationId = organizationId, CollectionId = cid, Key = entry.Key };
                db.Concepts.Add(row);
                existing.Add(row);
            }
            row.Ordinal = i;
            // Never overwrite an instructor's edit, and never revive a concept the instructor archived.
            if (row.EditedByInstructor && !entry.EditedByInstructor) continue;
            row.IsArchived = false;
            row.Title = entry.Title;
            row.Definition = entry.Definition;
            row.KeyFactsJson = JsonSerializer.Serialize(entry.KeyFacts);
            row.MisconceptionsJson = JsonSerializer.Serialize(entry.Misconceptions);
            row.EditedByInstructor = entry.EditedByInstructor;
            db.ConceptCitations.RemoveRange(row.Citations);
            row.Citations = entry.PassageIds
                .Select(p => Guid.TryParse(p, out var g) ? g : Guid.Empty)
                .Where(validChunks.Contains).Distinct()
                .Select(g => new ConceptCitation { OrganizationId = organizationId, ConceptId = row.Id, ChunkId = g })
                .ToList();
        }
        foreach (var gone in existing.Where(c => map.Concepts.All(e => e.Key != c.Key) && !c.EditedByInstructor))
            gone.IsArchived = true;

        var ids = existing.ToDictionary(c => c.Key, c => c.Id);
        var oldRelations = await db.ConceptRelations
            .Where(r => r.OrganizationId == organizationId && ids.Values.Contains(r.FromConceptId)).ToListAsync(ct);
        db.ConceptRelations.RemoveRange(oldRelations);
        foreach (var r in map.Relations.Where(r => ids.ContainsKey(r.From) && ids.ContainsKey(r.To)).DistinctBy(r => (r.From, r.To, r.Kind)))
            db.ConceptRelations.Add(new ConceptRelation { OrganizationId = organizationId, FromConceptId = ids[r.From], ToConceptId = ids[r.To], Kind = r.Kind });

        collection.ConceptMapVersion = Math.Max(collection.ConceptMapVersion + 1, map.Version);
        collection.Status = ProcessingStatus.Ready;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        });
    }

    public async Task<IReadOnlyList<ConceptRubric>> LoadRubricsAsync(string collectionId, CancellationToken ct = default)
    {
        var cid = SqlSourceStore.ParseId(collectionId);
        var points = await db.RubricPoints.AsNoTracking()
            .Join(db.Concepts.AsNoTracking().Where(c => c.CollectionId == cid && c.OrganizationId == organizationId && !c.IsArchived),
                p => p.ConceptId, c => c.Id, (p, c) => new { Point = p, c.Key })
            .ToListAsync(ct);
        return points.GroupBy(x => x.Key).Select(g =>
        {
            var latest = g.Max(x => x.Point.RubricVersion);
            var rows = g.Where(x => x.Point.RubricVersion == latest).Select(x => x.Point).ToList();
            RubricItem Item(RubricPoint p) => new(p.Text, p.Weight, JsonSerializer.Deserialize<List<string>>(p.CitationsJson) ?? []);
            return new ConceptRubric
            {
                ConceptKey = g.Key,
                Version = latest,
                Understanding = rows.Where(r => r.Kind == RubricPointKind.Understanding).Select(Item).ToList(),
                AcceptableVariants = rows.Where(r => r.Kind == RubricPointKind.AcceptableVariant).Select(Item).ToList(),
                Misconceptions = rows.Where(r => r.Kind == RubricPointKind.Misconception).Select(Item).ToList(),
                EditedByInstructor = rows.Any(r => r.EditedByInstructor),
            };
        }).ToList();
    }

    public async Task SaveRubricsAsync(string collectionId, IReadOnlyList<ConceptRubric> rubrics, CancellationToken ct = default)
    {
        var cid = SqlSourceStore.ParseId(collectionId);
        var concepts = await db.Concepts.AsNoTracking()
            .Where(c => c.CollectionId == cid && c.OrganizationId == organizationId)
            .Select(c => new { c.Id, c.Key }).ToListAsync(ct);
        var current = await LoadRubricsAsync(collectionId, ct);
        foreach (var rubric in rubrics)
        {
            var concept = concepts.FirstOrDefault(c => c.Key == rubric.ConceptKey);
            if (concept is null) continue;
            var prior = current.FirstOrDefault(r => r.ConceptKey == rubric.ConceptKey);
            if (prior is not null && prior.Version >= rubric.Version) continue; // that version is already stored
            void Add(IEnumerable<RubricItem> items, RubricPointKind kind)
            {
                foreach (var i in items)
                    db.RubricPoints.Add(new RubricPoint
                    {
                        OrganizationId = organizationId,
                        ConceptId = concept.Id,
                        RubricVersion = rubric.Version,
                        Kind = kind,
                        Text = i.Text,
                        Weight = i.Weight,
                        CitationsJson = JsonSerializer.Serialize(i.PassageIds),
                        EditedByInstructor = rubric.EditedByInstructor,
                    });
            }
            Add(rubric.Understanding, RubricPointKind.Understanding);
            Add(rubric.AcceptableVariants, RubricPointKind.AcceptableVariant);
            Add(rubric.Misconceptions, RubricPointKind.Misconception);
        }
        await db.SaveChangesAsync(ct);
    }
}
