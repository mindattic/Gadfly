using System.Text.Json;
using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Jobs;

/// <summary>Reports a running job's progress back to its row.</summary>
public interface IJobProgress
{
    /// <summary>Records <paramref name="fraction"/> (0..1) and an optional message.</summary>
    Task ReportAsync(double fraction, string? message = null, CancellationToken ct = default);
}

/// <summary>
/// One kind of background work (ingestion, concept extraction, rubric generation, report...).
/// Register implementations with <see cref="JobServiceCollectionExtensions.AddJobHandler{T}"/>.
/// Handlers must be idempotent: a job whose worker died is retried from the start.
/// </summary>
public interface IJobHandler
{
    /// <summary>The <see cref="Job.Kind"/> this handler runs.</summary>
    string Kind { get; }

    Task RunAsync(Job job, IJobProgress progress, CancellationToken ct);
}

/// <summary>Enqueues work on the SQL-table-backed job queue (GAD-T43).</summary>
public sealed class JobQueue(GadflyDbContext db, TimeProvider clock)
{
    public async Task<Guid> EnqueueAsync<TPayload>(string kind, TPayload payload, Guid? organizationId,
        DateTimeOffset? runAfter = null, int maxAttempts = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A job needs a kind.", nameof(kind));
        var now = clock.GetUtcNow();
        var job = new Job
        {
            Kind = kind,
            OrganizationId = organizationId,
            PayloadJson = JsonSerializer.Serialize(payload),
            State = JobState.Queued,
            MaxAttempts = Math.Max(1, maxAttempts),
            RunAfter = runAfter ?? now,
            CreatedAt = now,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    /// <summary>Asks a queued or running job to stop. A running handler sees cancellation at its next progress report.</summary>
    public async Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        var n = await db.Jobs
            .Where(j => j.Id == jobId && (j.State == JobState.Queued || j.State == JobState.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, JobState.Cancelled)
                                      .SetProperty(j => j.CompletedAt, clock.GetUtcNow()), ct);
        return n > 0;
    }

    public static TPayload ReadPayload<TPayload>(Job job) =>
        JsonSerializer.Deserialize<TPayload>(job.PayloadJson)
        ?? throw new InvalidOperationException($"Job {job.Id} ({job.Kind}) has an empty payload.");
}
