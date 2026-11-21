using Gadfly.Server.Data;
using Gadfly.Server.Jobs;
using Gadfly.Socrates;
using Gadfly.Sources;

namespace Gadfly.Server.Sources;

/// <summary>Payload of a <see cref="SourceProcessingJob"/>.</summary>
public sealed record SourceProcessingRequest(Guid OrganizationId, Guid CollectionId, Guid RequestedByUserId, string Title,
    string UploadFolder, bool RebuildConceptMap = false);

/// <summary>Where uploaded source files are kept on the server until they move to MindAttic.Media storage.</summary>
public static class SourceUploads
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "uploads");

    public static string FolderFor(Guid organizationId, Guid collectionId) =>
        Path.Combine(Root, organizationId.ToString("N"), collectionId.ToString("N"));
}

/// <summary>
/// Background job "source.process" (GAD-T29): ingests a collection's upload folder into SQL, then has
/// the Legion panel (re)build the concept map and rubrics. Idempotent, as the queue requires:
/// unchanged files are skipped by content hash, and the map/rubric builders reconcile with what is
/// stored (instructor edits are kept).
/// </summary>
public sealed class SourceProcessingJob(GadflyDbContext db, ISocraticVotingClient panel, IEnumerable<IDocumentTextExtractor> extractors,
    Platform.UsageMeter meter) : IJobHandler
{
    public const string JobKind = "source.process";
    public string Kind => JobKind;

    public async Task RunAsync(Job job, IJobProgress progress, CancellationToken ct)
    {
        var request = JobQueue.ReadPayload<SourceProcessingRequest>(job);
        using var usage = PanelUsage.Track(out var calls);
        try
        {
            await ProcessAsync(request, progress, ct);
        }
        finally
        {
            // GAD-T47: the panel calls of this run, metered to the organization (estimated).
            db.ChangeTracker.Clear();
            meter.Stage(db, request.OrganizationId, null, calls, "source-processing");
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ProcessAsync(SourceProcessingRequest request, IJobProgress progress, CancellationToken ct)
    {
        var collectionId = request.CollectionId.ToString();
        var store = new SqlSourceStore(db, request.OrganizationId, request.RequestedByUserId);
        var maps = new SqlConceptMapStore(db, request.OrganizationId);

        await progress.ReportAsync(0.02, "Reading documents", ct);
        var ingest = await new SourceIngestor(store, extractors.ToList())
            .IngestAsync(request.UploadFolder, collectionId, request.Title, ct: ct);
        var passages = await store.GetPassagesAsync(collectionId, ct);
        if (passages.Count == 0)
            throw new InvalidOperationException(ingest.Skipped.Count > 0
                ? $"None of the uploaded files could be read ({ingest.Skipped.Count} unsupported)."
                : "The uploaded files contain no readable text.");

        var map = await maps.LoadMapAsync(collectionId, ct);
        if (map is null || request.RebuildConceptMap || ingest.DocumentsIngested > 0)
        {
            await progress.ReportAsync(0.15, $"Finding concepts in {passages.Count} passages", ct);
            map = await new ConceptMapBuilder(panel).BuildAsync(collectionId, passages, map,
                new Progress<string>(_ => { }), ct);
            if (map.Concepts.Count == 0)
                throw new InvalidOperationException("The panel found no concepts; check the LLM provider keys and the material.");
            await maps.SaveMapAsync(map, ct);
            map = await maps.LoadMapAsync(collectionId, ct) ?? map;
        }

        var rubrics = await maps.LoadRubricsAsync(collectionId, ct);
        var retriever = new GroundingRetriever(passages);
        var builder = new RubricBuilder(panel);
        var done = 0;
        var updated = new List<ConceptRubric>();
        // Only concepts that need it get a (new) rubric: no rubric yet, the material changed, or a rebuild was asked for.
        var rebuildAll = request.RebuildConceptMap || ingest.DocumentsIngested > 0;
        foreach (var concept in map.Concepts)
        {
            var prior = rubrics.FirstOrDefault(r => r.ConceptKey == concept.Key);
            if (prior is not null && !rebuildAll)
            {
                done++;
                continue;
            }
            await progress.ReportAsync(0.5 + 0.5 * done / Math.Max(1, map.Concepts.Count), $"Writing rubric: {concept.Title}", ct);
            var single = new ConceptMap { CollectionId = collectionId, Version = map.Version, Concepts = [concept] };
            var rebuilt = await builder.BuildAsync(single, retriever, prior is null ? null : [prior], ct: ct);
            updated.AddRange(rebuilt);
            done++;
        }
        await maps.SaveRubricsAsync(collectionId, updated, ct);
        await progress.ReportAsync(1, $"{map.Concepts.Count} concepts ready", ct);
    }
}
