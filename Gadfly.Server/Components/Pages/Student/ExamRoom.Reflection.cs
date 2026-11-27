namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The closing reflection in the exam room (GAD-T818, server side GAD-T817): once the examiner has ended the exam, the ended step carries
/// one fixed question (<see cref="Gadfly.Server.Sessions.SessionStep.Reflection"/>). The examiner asks it aloud like any question and the
/// student answers out loud (typed only under the typed-answer accommodation, GAD-T260) or skips it. The clock has stopped: no timer, no
/// progress, no "I don't know", only a Skip; it is never graded (Elenchus at Scale 2.2.2, 2.5.1, 2.6). Answering or skipping shows the
/// ended card, and a reload while it waits comes back to it.
/// </summary>
public partial class ExamRoom
{
    /// <summary>The turn the voice room keys the reflection by (its spoken draft and the answer it sends): no question has it.</summary>
    private const int ReflectionTurn = -1;
    /// <summary>The reflection being typed under the typed-answer accommodation.</summary>
    private string typedReflection = "";

    /// <summary>The question the examiner says: the reflection while it waits, else the step's question.</summary>
    private string? VoiceQuestion => step?.Reflection ?? step?.Question;

    private int? VoiceTurn => step?.Reflection is not null ? ReflectionTurn : step?.TurnIndex;

    /// <summary>Sends the reflection (empty skips it). False when the step did not run or the server refused it.</summary>
    private Task<bool> AnswerReflectionAsync(string? text, string mode)
    {
        if (Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return Task.FromResult(false);
        return RunStepAsync(ThinkingAbout.Saving, () => Sessions.AnswerReflectionAsync(org, SessionId, user, text, mode, stop.Token));
    }

    private async Task SkipReflectionAsync()
    {
        if (busy || Tenant.OrganizationId is not { } org || Tenant.UserId is not { } user) return;
        await RunStepAsync(ThinkingAbout.Saving, () => Sessions.SkipReflectionAsync(org, SessionId, user, stop.Token));
    }

    private async Task SubmitTypedReflectionAsync()
    {
        if (!typedAnswers || busy || string.IsNullOrWhiteSpace(typedReflection)) return;
        var text = typedReflection.Trim();
        voiceRoom?.NoteTypedAnswer(text);
        await AnswerReflectionAsync(text, "text");
    }
}
