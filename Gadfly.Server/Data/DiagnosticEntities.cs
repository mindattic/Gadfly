namespace Gadfly.Server.Data;

/// <summary>
/// How long one outside call on a live exam took (GAD-T413): a panel call (judge, analysis, claim extraction, question
/// planning or generation, verification), the examiner's ElevenLabs voice, or a speech-recognition hub call. One small row
/// per call, written off the student's critical path, so the owner can see live which call is slow without relying on the
/// file log. Platform diagnostics, not tenant-owned: it holds names and durations only, never answer or question text.
/// </summary>
public sealed class ApiCallTiming
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? SessionId { get; set; }
    /// <summary>The session's turn index that was open when the step began (null outside a session step, e.g. voice).</summary>
    public int? TurnIndex { get; set; }
    /// <summary>e.g. panel.judge, panel.analysis, tts.elevenlabs, stt.stopSession.</summary>
    public string CallName { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public int DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorSummary { get; set; }
}
