using Gadfly.Server.Student;
using Microsoft.AspNetCore.Components.Web;

namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The typed-answer accommodation in the exam room (GAD-T260). Exams are spoken (GAD-T239); an instructor or admin may grant one named
/// student on one assignment the right to type their answers instead, for a documented speech, hearing or anxiety need, and every
/// grant is audited (<see cref="Gadfly.Server.Exams.ExamService.SetTypedAnswerAccommodationAsync"/>). The student can't switch to it:
/// the box appears only when the assignment carries the grant. The camera, the examiner's spoken questions and the gaze check run as
/// for everyone else (<see cref="VoiceRoom.TypedAnswers"/> only drops the microphone); each answer is stored with mode "text", which
/// reviewers see as typed, and a paste into the box is an integrity note for review, never blocked.
/// </summary>
public partial class ExamRoom
{
    /// <summary>The assignment carries the typed-answer accommodation; read once when the room loads.</summary>
    private bool typedAnswers;
    /// <summary>The answer being typed for the question on screen.</summary>
    private string typedAnswer = "";
    private VoiceRoom? voiceRoom;

    private async Task LoadTypedAnswersAsync(Guid org, Guid user)
    {
        try { typedAnswers = await StudentExams.TypedAnswersAsync(Scopes, org, user, SessionId, stop.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Spoken answers, as for everyone: the accommodation is only ever an addition.
            Log.LogWarning(ex, "The typed-answer accommodation could not be read for session {SessionId}.", SessionId);
            typedAnswers = false;
        }
    }

    /// <summary>The devices a pause waits for, in the student's words.</summary>
    private string PausedDevices => typedAnswers ? "your camera and the examiner are" : "your microphone, camera and the examiner are";

    /// <summary>The devices the first question waits for, in the student's words.</summary>
    private string StartDevices => typedAnswers ? "your camera is" : "your microphone and camera are";

    private void OnTyped()
    {
        if (answerStartedAt is null && typedAnswer.Length > 0) answerStartedAt = Clock.GetUtcNow();
    }

    private async Task OnTypedKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && (e.CtrlKey || e.MetaKey) && !busy && !string.IsNullOrWhiteSpace(typedAnswer)) await SubmitTypedAsync();
    }

    /// <summary>Hands in the typed answer (mode "text": reviewers see it as typed) and adds it to the on-screen transcript.</summary>
    private async Task SubmitTypedAsync()
    {
        if (!typedAnswers || busy || string.IsNullOrWhiteSpace(typedAnswer)) return;
        var text = typedAnswer.Trim();
        voiceRoom?.NoteTypedAnswer(text);
        await SendAsync(text);
    }

    /// <summary>A typed reply to a meaning clarification.</summary>
    private async Task ClarifyTypedAsync(Gadfly.Server.Verification.PendingVerification v)
    {
        if (!typedAnswers || busy || string.IsNullOrWhiteSpace(extraText)) return;
        var text = extraText.Trim();
        voiceRoom?.NoteTypedAnswer(text);
        await ClarifyAsync(v, text);
    }
}
