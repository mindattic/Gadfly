using System.Threading.Channels;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gadfly.Server.Evidence;

/// <summary>One session step's evidence to sync (GAD-T404).</summary>
public sealed record EvidenceSyncRequest(Guid OrganizationId, Guid SessionId, Guid ExamId,
    IReadOnlyList<CollectedEvidence> Evidence, DateTimeOffset Now);

/// <summary>
/// Queues <see cref="EvidenceWriter.SyncAsync"/> work off the session-step critical path (GAD-T404): the same
/// bounded-channel, background-writer shape GAD-T413 used for call timings. <see cref="EvidenceWriter.SyncAsync"/>
/// re-scans the whole session's turns, responses and evidence every time it runs, so running it synchronously inside
/// every step (as it used to) got slower turn by turn as a session grew — exactly the kind of per-turn tax a student
/// should never wait on. It is idempotent (an item not written now is written by the next queued sync), so queuing it
/// instead changes nothing about what gets stored, only when. Bounded and drops the oldest under a flood: a dropped,
/// stale request changes nothing a later one (which re-covers the whole session) won't also write.
/// </summary>
public sealed class EvidenceSyncQueue
{
    private readonly Channel<EvidenceSyncRequest> _queue = Channel.CreateBounded<EvidenceSyncRequest>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
        dropped => Console.Error.WriteLine($"Evidence sync for session {dropped.SessionId} dropped: the queue is full."));

    internal ChannelReader<EvidenceSyncRequest> Reader => _queue.Reader;

    public void Enqueue(EvidenceSyncRequest request) => _queue.Writer.TryWrite(request);
}

/// <summary>
/// Writes queued evidence syncs (GAD-T404). A failed sync is retried a few times with a short pause, because the last
/// step of a session has no later step to retry it; queued syncs still waiting at shutdown are written before the
/// host stops. A sync that still fails is logged and dropped.
/// </summary>
public sealed class EvidenceSyncWriter(EvidenceSyncQueue queue, IServiceScopeFactory scopes, ILogger<EvidenceSyncWriter> log) : BackgroundService
{
    private const int Attempts = 3;
    private static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var request)) await WriteAsync(request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: write what is still queued, within a short budget.
            using var drain = new CancellationTokenSource(DrainBudget);
            try
            {
                while (reader.TryRead(out var request)) await WriteAsync(request, drain.Token, retry: false);
            }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                log.LogWarning("Queued evidence syncs were left unwritten at shutdown.");
            }
        }
    }

    private async Task WriteAsync(EvidenceSyncRequest request, CancellationToken ct, bool retry = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScopeFor(TenantSnapshot.ForOrganization(request.OrganizationId));
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                await EvidenceWriter.SyncAsync(db, request.OrganizationId, request.SessionId, request.ExamId, request.Evidence,
                    request.Now, ct);
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!retry || attempt >= Attempts)
                {
                    log.LogWarning(ex, "Evidence for session {SessionId} was not synced after {Attempts} attempt(s); a later step's queued sync, if any, retries.",
                        request.SessionId, attempt);
                    return;
                }
                await Task.Delay(RetryPause, ct);
            }
        }
    }
}

public static class EvidenceSyncServices
{
    /// <summary>Registers the evidence sync queue and its background writer. Call once at startup.</summary>
    public static IServiceCollection AddEvidenceSyncQueue(this IServiceCollection services)
    {
        services.AddSingleton<EvidenceSyncQueue>();
        services.AddHostedService<EvidenceSyncWriter>();
        return services;
    }
}
