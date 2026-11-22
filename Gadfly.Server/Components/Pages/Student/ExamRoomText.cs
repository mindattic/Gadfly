namespace Gadfly.Server.Components.Pages.Student;

/// <summary>
/// The exam room's own new sentences (GAD-T511, GAD-T510, GAD-T512, GAD-T507 findings), kept in one place so they can move to the
/// UI string catalog (Localization/Strings, GAD-T543) as keys under "examRoom." without hunting through the markup.
/// </summary>
internal static class ExamRoomText
{
    // ---- leaving the exam window (GAD-T511): neutral, never a strike count or a threat ----
    public const string WindowAway = "The exam is paused while this window is in the background. Come back to this window to carry on.";
    public const string WindowBack = "The exam paused while the window was in the background. The examiner will repeat the end of the question.";
    public const string ReviewTitle = "A person will look at this attempt";
    public const string ReviewBody = "The exam window was left several times, or for a long time. That can happen for innocent reasons, so nothing has been cancelled and your answers are all kept: your instructor will look at what happened before your result stands. If you like, you can explain here.";
    public const string ReviewNoteLabel = "What happened (optional; only your instructor sees this)";
    public const string ReviewNoteSend = "Send my explanation";
    public const string ReviewNoteLater = "Not now";
    public const string ReviewNoteThanks = "Thank you. Your explanation is with your instructor.";

    // ---- the camera during the exam (GAD-T510): no self-view, a camera-on indicator and an on-demand preview ----
    public const string CameraOn = "Camera on";
    public const string CameraStarting = "Camera starting";
    public const string CameraShow = "Show my camera";
    public const string CameraHide = "Hide my camera";
    public const string CameraPreviewLabel = "Your camera (only you see this; it closes by itself)";

    // ---- the transcript (GAD-T512) ----
    public const string TranscriptQuiet = "The conversation is tucked away while you answer. It comes back when you finish.";

    // ---- accessibility (GAD-T507 findings) ----
    public const string PauseLengthLabel = "Wait before sending my answer";
    public const string PauseStandard = "Standard pause";
    public const string PauseLonger = "Longer pause";
    public const string PauseLongest = "Much longer pause";
    public const string PauseDone = "Only when I press Done answering";
}
