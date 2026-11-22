using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Voice;

/// <summary>
/// Takes the exam room's cut-off marker (GAD-T462, <see cref="AnswerCutOff"/>) and stores it with the answer's speech metrics.
/// The browser reports only the delay in milliseconds, never audio; it is range-checked here, dropped when voice measurements are
/// off for the session (GAD-T259, GAD-T283, the same decision <see cref="VoiceStrain.EnabledForSessionAsync"/> makes for the
/// voice measurements themselves), and recorded through the session's own step so it lands after the answer was judged.
/// </summary>
public static class AnswerCutOffRecorder
{
    /// <summary>True when the marker was accepted for recording (it may still be skipped, e.g. once the exam has ended).</summary>
    public static async Task<bool> RecordAsync(IServiceScopeFactory scopes, SessionOrchestrator sessions, Guid organizationId, Guid studentUserId,
        Guid sessionId, int turnIndex, double delayMs, CancellationToken ct)
    {
        if (!double.IsFinite(delayMs) || turnIndex < 0) return false;
        var seconds = delayMs / 1000;
        if (!AnswerCutOff.InRange(seconds)) return false;
        await using (var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.ForOrganization(organizationId)))
        {
            var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
            if (!await VoiceStrain.EnabledForSessionAsync(db, sessionId, ct)) return false;
        }
        await sessions.NoteAnswerCutOffAsync(organizationId, sessionId, studentUserId, turnIndex, seconds, ct);
        return true;
    }
}
