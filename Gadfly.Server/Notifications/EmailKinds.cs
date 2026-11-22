namespace Gadfly.Server.Notifications;

/// <summary>The notification emails Gadfly sends itself (GAD-T310). Invitations are MindAttic.Authentication's own and are not listed here.</summary>
public static class EmailKinds
{
    public const string ExamAssigned = "exam-assigned";
    public const string Reminder = "reminder";
    public const string ResultReleased = "result-released";
    public const string AppealReceived = "appeal-received";
    public const string AppealDecided = "appeal-decided";
    public const string AttemptReset = "attempt-reset";
    /// <summary>A platform-admin or org-admin test send from the preview page; never gated by the org switch or opt-out.</summary>
    public const string Test = "test";

    public static readonly IReadOnlyList<string> All = [ExamAssigned, Reminder, ResultReleased, AppealReceived, AppealDecided, AttemptReset];

    /// <summary>A short, plain-English name for the admin preview page and the sent-log.</summary>
    public static string Describe(string kind) => kind switch
    {
        ExamAssigned => "Exam assigned",
        Reminder => "Reminder before the due date",
        ResultReleased => "Result released",
        AppealReceived => "Appeal received",
        AppealDecided => "Appeal decided",
        AttemptReset => "Attempt reset",
        Test => "Test send",
        _ => kind,
    };
}
