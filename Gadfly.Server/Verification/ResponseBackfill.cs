using Gadfly.Assessment;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Verification;

/// <summary>
/// Existing answers become Captured originals (GAD-T86 "existing turns migrate as Captured originals"):
/// every answered <see cref="SessionTurn"/> recorded before response versioning existed gets a
/// <see cref="StudentResponse"/> (OriginalText = VerifiedText = the stored answer) with transcript
/// version 1, plus one audit event per session. Idempotent: turns that already have a response are skipped.
/// </summary>
public static class ResponseBackfill
{
    public static async Task<int> RunAsync(GadflyDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var total = 0;
        var conflicts = 0;
        while (true)
        {
            var turns = await db.SessionTurns.IgnoreQueryFilters()
                .Where(t => t.AnswerText != null && !db.StudentResponses.IgnoreQueryFilters().Any(r => r.TurnId == t.Id))
                .OrderBy(t => t.SessionId).ThenBy(t => t.Index).Take(500).ToListAsync(ct);
            if (turns.Count == 0) return total;
            foreach (var session in turns.GroupBy(t => (t.SessionId, t.OrganizationId)))
            {
                var sequence = await db.StudentResponses.IgnoreQueryFilters().Where(r => r.SessionId == session.Key.SessionId)
                    .MaxAsync(r => (int?)r.Sequence, ct) ?? 0;
                foreach (var t in session)
                {
                    var voice = t.AnswerMode == "voice";
                    var r = new StudentResponse
                    {
                        OrganizationId = t.OrganizationId, SessionId = t.SessionId, TurnId = t.Id, Sequence = ++sequence,
                        Kind = ResponseKind.Original, Modality = voice ? "voice" : "text",
                        OriginalText = t.AnswerText!, VerifiedText = t.AnswerText!, SpeechRecognitionText = voice ? t.AnswerText : null,
                        VerificationState = VerificationState.Captured, SubmittedAt = t.AnswerCompletedAt ?? t.AskedAt,
                    };
                    r.Versions.Add(new TranscriptVersion
                    {
                        OrganizationId = t.OrganizationId, ResponseId = r.Id, Version = 1, Text = t.AnswerText!,
                        Source = voice ? "speech-recognition" : "student", CreatedAt = r.SubmittedAt,
                    });
                    db.StudentResponses.Add(r);
                }
                db.AuditEvents.Add(new AuditEvent
                {
                    OrganizationId = session.Key.OrganizationId, ActorRole = "System", Action = "response.migrated",
                    EntityType = nameof(ExamSession), EntityId = session.Key.SessionId.ToString(), SessionId = session.Key.SessionId,
                    AfterJson = $"{{\"responses\":{session.Count()}}}",
                    Reason = "Answers recorded before response versioning became Captured originals.", At = now,
                });
                total += session.Count();
            }
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (++conflicts <= 3)
            {
                // A live answer took one of these sequence numbers (unique SessionId+Sequence): drop this batch and
                // re-read; turns that now have a response are skipped, the rest get fresh numbers.
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                total = Math.Max(0, total - turns.Count);
            }
        }
    }
}

/// <summary>Runs <see cref="ResponseBackfill"/> once at startup, in a system scope, without delaying the app.</summary>
public sealed class ResponseBackfillService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ResponseBackfillService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken);
            using var scope = scopes.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            var n = await ResponseBackfill.RunAsync(db, clock, stoppingToken);
            if (n > 0) log.LogInformation("Response versioning: {Count} earlier answers recorded as Captured originals.", n);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Response versioning backfill did not finish; it runs again at the next start.");
        }
    }
}

public static class VerificationServiceCollectionExtensions
{
    /// <summary>Response verification services (GAD-T86..T88): the one-time backfill of earlier answers.</summary>
    public static IServiceCollection AddVerification(this IServiceCollection services)
    {
        services.AddHostedService<ResponseBackfillService>();
        return services;
    }
}
