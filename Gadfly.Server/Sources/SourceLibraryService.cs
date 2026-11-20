using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Sources;

/// <summary>A processing job's state, as the source library shows it.</summary>
public sealed record SourceJobStatus(Guid Id, JobState State, double Progress, string? Message, string? Error,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt)
{
    public bool IsActive => State is JobState.Queued or JobState.Running;
}

/// <summary>One row of the source library's collection list.</summary>
public sealed record SourceCollectionSummary(Guid Id, string Title, ProcessingStatus Status, int DocumentCount,
    int ConceptCount, int ConceptMapVersion, DateTimeOffset CreatedAt, SourceJobStatus? LatestJob);

/// <summary>A document of a collection: uploaded (on disk), ingested (in SQL), or both.</summary>
public sealed record SourceLibraryDocument(string FileName, long? SizeBytes, bool Uploaded, bool Ingested,
    bool ChangedSinceIngest, int PassageCount, DateTimeOffset? UploadedAt, DateTimeOffset? IngestedAt, string? Error);

/// <summary>A cited passage, opened from a concept or rubric citation.</summary>
public sealed record SourcePassageView(string Id, string FileName, int Ordinal, string? Location, string Text);

/// <summary>What an upload did.</summary>
public sealed record SourceUploadResult(bool Ok, string Message, string? FileName = null);

/// <summary>
/// The instructor source library (GAD-T49): collections, uploads, processing jobs, and instructor edits
/// to the concept map and rubrics. Every query names the organization explicitly (on top of the
/// context's tenant filter) because the job table is not tenant-filtered. Edits go through
/// <see cref="SqlConceptMapStore"/>, so the same keep-the-instructor's-edit rules apply as in
/// re-extraction, and every edit bumps <see cref="SourceCollection.ConceptMapVersion"/> so running
/// sessions keep their snapshot and new sessions pick the edit up.
/// </summary>
public sealed class SourceLibraryService(GadflyDbContext db, JobQueue jobs, TimeProvider clock)
{
    /// <summary>Per-file upload cap.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>Extensions the server can read today; PDF, DOCX, EPUB and HTML arrive with Tutor.Ingestion (TUT-T64).</summary>
    public static readonly IReadOnlyList<string> AcceptedExtensions = [".txt", ".text", ".md", ".markdown"];

    public static string AcceptAttribute => string.Join(",", AcceptedExtensions);

    // ------------------------------------------------------------------ collections

    public async Task<IReadOnlyList<SourceCollectionSummary>> ListCollectionsAsync(Guid organizationId, CancellationToken ct = default)
    {
        var rows = await db.SourceCollections.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.Title, c.Status, c.ConceptMapVersion, c.CreatedAt,
                Ingested = db.SourceDocuments.Where(d => d.CollectionId == c.Id && d.OrganizationId == organizationId).Select(d => d.FileName).ToList(),
                Concepts = db.Concepts.Count(x => x.CollectionId == c.Id && x.OrganizationId == organizationId && !x.IsArchived),
            })
            .ToListAsync(ct);
        var latest = await LatestJobsAsync(organizationId, ct);
        return rows.Select(r =>
        {
            var names = r.Ingested.Concat(UploadedFiles(organizationId, r.Id).Select(f => f.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return new SourceCollectionSummary(r.Id, r.Title, r.Status, names, r.Concepts, r.ConceptMapVersion, r.CreatedAt,
                latest.GetValueOrDefault(r.Id));
        }).ToList();
    }

    public Task<SourceCollection?> GetCollectionAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default) =>
        db.SourceCollections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == collectionId && c.OrganizationId == organizationId, ct);

    public async Task<Guid> CreateCollectionAsync(Guid organizationId, Guid userId, string title, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("A collection needs a title.", nameof(title));
        if (title.Length > 200) title = title[..200];
        var collection = new SourceCollection
        {
            OrganizationId = organizationId,
            Title = title,
            CreatedByUserId = userId,
            CreatedAt = clock.GetUtcNow(),
            Status = ProcessingStatus.Pending,
        };
        db.SourceCollections.Add(collection);
        await db.SaveChangesAsync(ct);
        db.Entry(collection).State = EntityState.Detached;
        return collection.Id;
    }

    // ------------------------------------------------------------------ documents and uploads

    public async Task<IReadOnlyList<SourceLibraryDocument>> ListDocumentsAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default)
    {
        var ingested = await db.SourceDocuments.AsNoTracking()
            .Where(d => d.CollectionId == collectionId && d.OrganizationId == organizationId)
            .Select(d => new
            {
                d.FileName, d.Sha256, d.Status, d.Error, d.IngestedAt,
                Passages = db.SourceChunks.Count(ch => ch.DocumentId == d.Id && ch.OrganizationId == organizationId),
            })
            .ToListAsync(ct);
        var uploaded = UploadedFiles(organizationId, collectionId).ToList();
        var result = new List<SourceLibraryDocument>();
        foreach (var file in uploaded)
        {
            var row = ingested.FirstOrDefault(d => string.Equals(d.FileName, file.Name, StringComparison.OrdinalIgnoreCase));
            var changed = row is not null && !string.Equals(row.Sha256, await HashAsync(file.FullName, ct), StringComparison.OrdinalIgnoreCase);
            result.Add(new SourceLibraryDocument(file.Name, file.Length, true, row is { Status: ProcessingStatus.Ready }, changed,
                row?.Passages ?? 0, file.LastWriteTimeUtc, row?.IngestedAt, row?.Error));
        }
        foreach (var row in ingested.Where(d => uploaded.All(f => !string.Equals(f.Name, d.FileName, StringComparison.OrdinalIgnoreCase))))
            result.Add(new SourceLibraryDocument(row.FileName, null, false, row.Status == ProcessingStatus.Ready, false,
                row.Passages, null, row.IngestedAt, row.Error));
        return result.OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Saves one uploaded file into the collection's upload folder under a sanitized name (a file of the
    /// same name is replaced; processing then re-reads it because its content hash changed).
    /// </summary>
    public async Task<SourceUploadResult> SaveUploadAsync(Guid organizationId, Guid collectionId, string originalName,
        long declaredSize, Stream content, CancellationToken ct = default)
    {
        if (await GetCollectionAsync(organizationId, collectionId, ct) is null)
            return new(false, "That collection doesn't exist in this organization.");
        var name = SanitizeFileName(originalName);
        if (name is null)
            return new(false, $"\"{originalName}\": only {string.Join(", ", AcceptedExtensions)} files can be read for now.");
        if (declaredSize > MaxFileBytes)
            return new(false, $"\"{originalName}\" is larger than the {MaxFileBytes / (1024 * 1024)} MB limit.");

        var folder = SourceUploads.FolderFor(organizationId, collectionId);
        Directory.CreateDirectory(folder);
        // Written outside the folder first: a processing run reads every file in it.
        var temp = Path.Combine(Path.GetTempPath(), $"gadfly-upload-{Guid.NewGuid():N}.part");
        try
        {
            long written = 0;
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await content.ReadAsync(buffer, ct)) > 0)
                {
                    written += n;
                    if (written > MaxFileBytes)
                        return new(false, $"\"{originalName}\" is larger than the {MaxFileBytes / (1024 * 1024)} MB limit.");
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (written == 0) return new(false, $"\"{originalName}\" is empty.");
            File.Move(temp, Path.Combine(folder, name), overwrite: true);
            return new(true, name == originalName ? $"Uploaded {name}." : $"Uploaded {originalName} as {name}.", name);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// A safe file name for the upload folder, or null when the extension can't be read: no path parts,
    /// only letters, digits, spaces, dots, dashes and underscores, no reserved device names, at most 120 characters.
    /// </summary>
    public static string? SanitizeFileName(string? original)
    {
        var name = Path.GetFileName((original ?? "").Replace('\\', '/').Split('/').Last()).Trim();
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!AcceptedExtensions.Contains(ext)) return null;
        var stem = Path.GetFileNameWithoutExtension(name);
        var sb = new StringBuilder(stem.Length);
        foreach (var ch in stem)
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' ? ch : '_');
        stem = sb.ToString().Trim(' ', '.');
        while (stem.Contains("..")) stem = stem.Replace("..", ".");
        if (stem.Length == 0) stem = "document";
        if (stem.Length > 120 - ext.Length) stem = stem[..(120 - ext.Length)].TrimEnd(' ', '.');
        var device = stem.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" || (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && char.IsDigit(device[3])))
            stem = "_" + stem;
        return stem + ext;
    }

    private static IEnumerable<FileInfo> UploadedFiles(Guid organizationId, Guid collectionId)
    {
        var folder = new DirectoryInfo(SourceUploads.FolderFor(organizationId, collectionId));
        return folder.Exists ? folder.EnumerateFiles().Where(f => AcceptedExtensions.Contains(f.Extension.ToLowerInvariant())) : [];
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct));
    }

    // ------------------------------------------------------------------ processing jobs

    /// <summary>Queues "source.process" for the collection, unless one is already queued or running.</summary>
    public async Task<(bool Ok, string Message)> EnqueueProcessingAsync(Guid organizationId, Guid userId, Guid collectionId,
        bool rebuildConceptMap, CancellationToken ct = default)
    {
        var collection = await GetCollectionAsync(organizationId, collectionId, ct);
        if (collection is null) return (false, "That collection doesn't exist in this organization.");
        if (await LatestJobAsync(organizationId, collectionId, ct) is { IsActive: true })
            return (false, "This collection is already being processed.");
        if (!UploadedFiles(organizationId, collectionId).Any())
            return (false, "Upload at least one document first.");
        var request = new SourceProcessingRequest(organizationId, collectionId, userId, collection.Title,
            SourceUploads.FolderFor(organizationId, collectionId), rebuildConceptMap);
        await jobs.EnqueueAsync(SourceProcessingJob.JobKind, request, organizationId, ct: ct);
        return (true, "Processing queued.");
    }

    /// <summary>
    /// The collection's most recent processing job. Found by kind and the payload's collection id with
    /// a LIKE over the payload (no index): fine while an organization has hundreds of jobs, not millions.
    /// </summary>
    public async Task<SourceJobStatus?> LatestJobAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default)
    {
        var needle = collectionId.ToString();
        var candidates = await db.Jobs.AsNoTracking()
            .Where(j => j.Kind == SourceProcessingJob.JobKind && j.OrganizationId == organizationId && j.PayloadJson.Contains(needle))
            .OrderByDescending(j => j.CreatedAt).Take(5)
            .ToListAsync(ct);
        var job = candidates.FirstOrDefault(j => PayloadCollection(j) == collectionId);
        return job is null ? null : ToStatus(job);
    }

    private async Task<Dictionary<Guid, SourceJobStatus>> LatestJobsAsync(Guid organizationId, CancellationToken ct)
    {
        var recent = await db.Jobs.AsNoTracking()
            .Where(j => j.Kind == SourceProcessingJob.JobKind && j.OrganizationId == organizationId)
            .OrderByDescending(j => j.CreatedAt).Take(500)
            .ToListAsync(ct);
        var result = new Dictionary<Guid, SourceJobStatus>();
        foreach (var job in recent)
            if (PayloadCollection(job) is { } cid && !result.ContainsKey(cid))
                result[cid] = ToStatus(job);
        return result;
    }

    private static Guid? PayloadCollection(Job job)
    {
        try { return JsonSerializer.Deserialize<SourceProcessingRequest>(job.PayloadJson)?.CollectionId; }
        catch (JsonException) { return null; }
    }

    private static SourceJobStatus ToStatus(Job j) =>
        new(j.Id, j.State, j.Progress, j.ProgressMessage, j.Error, j.CreatedAt, j.CompletedAt);

    // ------------------------------------------------------------------ concept map and rubrics

    public Task<ConceptMap?> LoadMapAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default) =>
        Maps(organizationId).LoadMapAsync(collectionId.ToString(), ct);

    public Task<IReadOnlyList<ConceptRubric>> LoadRubricsAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default) =>
        Maps(organizationId).LoadRubricsAsync(collectionId.ToString(), ct);

    public async Task<IReadOnlyList<(string Key, string Title)>> ArchivedConceptsAsync(Guid organizationId, Guid collectionId, CancellationToken ct = default) =>
        (await db.Concepts.AsNoTracking()
            .Where(c => c.CollectionId == collectionId && c.OrganizationId == organizationId && c.IsArchived)
            .OrderBy(c => c.Title).Select(c => new { c.Key, c.Title }).ToListAsync(ct))
        .Select(c => (c.Key, c.Title)).ToList();

    /// <summary>The cited passages, in the order asked for; ids are chunk Guids ("N").</summary>
    public async Task<IReadOnlyList<SourcePassageView>> GetPassagesAsync(Guid organizationId, Guid collectionId,
        IEnumerable<string> passageIds, CancellationToken ct = default)
    {
        var ids = passageIds.Select(p => Guid.TryParse(p, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.SourceChunks.AsNoTracking()
            .Where(ch => ids.Contains(ch.Id) && ch.CollectionId == collectionId && ch.OrganizationId == organizationId)
            .Join(db.SourceDocuments.AsNoTracking(), ch => ch.DocumentId, d => d.Id,
                (ch, d) => new { ch.Id, d.FileName, ch.Ordinal, ch.Location, ch.Text })
            .ToListAsync(ct);
        return ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r is not null)
            .Select(r => new SourcePassageView(r!.Id.ToString("N"), r.FileName, r.Ordinal, r.Location, r.Text)).ToList();
    }

    /// <summary>
    /// Saves an instructor's edit of a concept (marked <see cref="ConceptEntry.EditedByInstructor"/>, so
    /// re-extraction leaves it alone). <see cref="SqlConceptMapStore.SaveMapAsync"/> bumps the map version.
    /// </summary>
    public async Task<(bool Ok, string Message)> UpdateConceptAsync(Guid organizationId, Guid collectionId, string key,
        string title, string definition, IReadOnlyList<string> keyFacts, IReadOnlyList<string> misconceptions, CancellationToken ct = default)
    {
        if (await ProcessingBlocksEditsAsync(organizationId, collectionId, ct) is { } busy) return (false, busy);
        title = (title ?? "").Trim();
        if (title.Length == 0) return (false, "A concept needs a title.");
        var maps = Maps(organizationId);
        var map = await maps.LoadMapAsync(collectionId.ToString(), ct);
        var concept = map?.Find(key);
        if (map is null || concept is null) return (false, "That concept no longer exists.");
        var edited = concept with
        {
            Title = title,
            Definition = (definition ?? "").Trim(),
            KeyFacts = Clean(keyFacts),
            Misconceptions = Clean(misconceptions),
            EditedByInstructor = true,
        };
        await maps.SaveMapAsync(map with { Concepts = map.Concepts.Select(c => c.Key == key ? edited : c).ToList() }, ct);
        return (true, $"Saved \"{title}\".");
    }

    /// <summary>
    /// Saves an instructor's rubric as a new rubric version marked <see cref="ConceptRubric.EditedByInstructor"/>
    /// (earlier versions stay for the sessions they judged), then bumps the collection's map version.
    /// Citations of items whose text is unchanged are kept by the caller passing them through.
    /// </summary>
    public async Task<(bool Ok, string Message)> SaveRubricAsync(Guid organizationId, Guid collectionId, string conceptKey,
        IReadOnlyList<RubricItem> understanding, IReadOnlyList<RubricItem> variants, IReadOnlyList<RubricItem> misconceptions,
        CancellationToken ct = default)
    {
        if (await ProcessingBlocksEditsAsync(organizationId, collectionId, ct) is { } busy) return (false, busy);
        var points = understanding.Where(i => !string.IsNullOrWhiteSpace(i.Text)).ToList();
        if (points.Count == 0) return (false, "A rubric needs at least one understanding point.");
        if (points.Any(p => p.Weight <= 0 || double.IsNaN(p.Weight) || p.Weight > 100)) return (false, "Weights must be between 0 and 100.");
        var maps = Maps(organizationId);
        var map = await maps.LoadMapAsync(collectionId.ToString(), ct);
        if (map?.Find(conceptKey) is null) return (false, "That concept no longer exists.");
        var prior = (await maps.LoadRubricsAsync(collectionId.ToString(), ct)).FirstOrDefault(r => r.ConceptKey == conceptKey);
        static List<RubricItem> Trim(IEnumerable<RubricItem> items, bool weighted) => items
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => i with { Text = i.Text.Trim(), Weight = weighted ? i.Weight : 1.0, PassageIds = i.PassageIds ?? [] })
            .ToList();
        var rubric = new ConceptRubric
        {
            ConceptKey = conceptKey,
            Version = (prior?.Version ?? 0) + 1,
            Understanding = Trim(points, true),
            AcceptableVariants = Trim(variants, false),
            Misconceptions = Trim(misconceptions, false),
            EditedByInstructor = true,
        };
        await maps.SaveRubricsAsync(collectionId.ToString(), [rubric], ct);
        await BumpMapVersionAsync(organizationId, collectionId, ct);
        return (true, $"Saved rubric version {rubric.Version}.");
    }

    /// <summary>
    /// Archives (never deletes) a concept. It is marked as the instructor's, so re-extraction neither
    /// overwrites nor revives it; sessions that already used it keep their snapshot.
    /// </summary>
    public Task<(bool Ok, string Message)> ArchiveConceptAsync(Guid organizationId, Guid collectionId, string key, CancellationToken ct = default) =>
        SetArchivedAsync(organizationId, collectionId, key, true, ct);

    public Task<(bool Ok, string Message)> RestoreConceptAsync(Guid organizationId, Guid collectionId, string key, CancellationToken ct = default) =>
        SetArchivedAsync(organizationId, collectionId, key, false, ct);

    private async Task<(bool Ok, string Message)> SetArchivedAsync(Guid organizationId, Guid collectionId, string key, bool archived, CancellationToken ct)
    {
        if (await ProcessingBlocksEditsAsync(organizationId, collectionId, ct) is { } busy) return (false, busy);
        db.ChangeTracker.Clear();
        var concept = await db.Concepts.FirstOrDefaultAsync(c => c.CollectionId == collectionId && c.OrganizationId == organizationId && c.Key == key, ct);
        var collection = await db.SourceCollections.FirstOrDefaultAsync(c => c.Id == collectionId && c.OrganizationId == organizationId, ct);
        if (concept is null || collection is null) return (false, "That concept no longer exists.");
        concept.IsArchived = archived;
        concept.EditedByInstructor = true;
        collection.ConceptMapVersion++;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (true, archived ? $"Archived \"{concept.Title}\"." : $"Restored \"{concept.Title}\".");
    }

    private async Task BumpMapVersionAsync(Guid organizationId, Guid collectionId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var collection = await db.SourceCollections.FirstAsync(c => c.Id == collectionId && c.OrganizationId == organizationId, ct);
        collection.ConceptMapVersion++;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>Edits wait while a processing run is rewriting the same map and rubrics.</summary>
    private async Task<string?> ProcessingBlocksEditsAsync(Guid organizationId, Guid collectionId, CancellationToken ct) =>
        await LatestJobAsync(organizationId, collectionId, ct) is { IsActive: true }
            ? "This collection is being processed; edit it once processing finishes."
            : null;

    private SqlConceptMapStore Maps(Guid organizationId) => new(db, organizationId);

    private static List<string> Clean(IEnumerable<string>? lines) =>
        (lines ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}
