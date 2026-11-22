using Gadfly.Server.Sessions;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// "Say it another way" (GAD-T602, engine GAD-T555): beside Repeat the question, every student can ask for the open question in other
/// words. The engine rewords it through the same fail-closed leak guard as any question (<see cref="SessionStep.Rephrased"/>; the
/// question itself when no rewording passes), records it on the turn, and it is free: no hint, no time penalty, no effect on the grade.
/// The examiner says it like a question; the words already said are kept.
/// </summary>
public partial class ExamRoom
{
    [Microsoft.AspNetCore.Components.Inject] private Gadfly.Server.Localization.UiText Ui { get; set; } = default!;

    private bool rephrasing;

    private bool CanSayAnotherWay => !rephrasing && !busy && !staffHeld && phase == Phase.Answering && step is { Verification: null };

    private async Task SayAnotherWayAsync()
    {
        if (!CanSayAnotherWay || voiceRoom is null || step is not { } asked) return;
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        rephrasing = true;
        error = null;
        try
        {
            var next = await Sessions.RephraseQuestionAsync(org, SessionId, user, stop.Token);
            // Only for the question still being asked: the exam may have moved on meanwhile.
            if (next.Rephrased is { Length: > 0 } said && next.TurnIndex == step?.TurnIndex && step?.TurnIndex == asked.TurnIndex)
                await voiceRoom.SpeakAnotherWayAsync(said);
        }
        catch (SessionRefusedException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.LogWarning(ex, "The question could not be said another way for session {SessionId}.", SessionId);
            error = Ui["examRoom.sayAnotherWay.failed"];
        }
        finally
        {
            rephrasing = false;
        }
    }
}
