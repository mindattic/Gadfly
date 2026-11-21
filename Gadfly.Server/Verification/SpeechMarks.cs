using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Sources.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Verification;

/// <summary>
/// What speech recognition left uncertain in one voice response, and whether the student checked it (GAD-T265).
/// </summary>
/// <param name="Segments">Runs of words the recognizer was unsure of, in the recognizer's own transcript.</param>
/// <param name="TranscriptChecked">The student confirmed or corrected the transcript, so its words are theirs, not the recognizer's guess.</param>
/// <param name="Corrected">A transcription correction changed the text.</param>
/// <param name="ChangedWords">Words changed between the recognizer's transcript and the verified text (0 when not corrected).</param>
/// <param name="Words">Recognized words with confidence, when the capture kept them.</param>
public sealed record SpeechMark(IReadOnlyList<UncertainSegment> Segments, bool TranscriptChecked, bool Corrected, int ChangedWords,
    IReadOnlyList<TimedWord> Words)
{
    /// <summary>The uncertain words graders must be told about: only while the student has not checked the transcript.</summary>
    public IReadOnlyList<UncertainSegment> Unchecked => TranscriptChecked ? [] : Segments;
}

/// <summary>
/// Loads <see cref="SpeechMark"/>s for voice responses: word confidences from the capture audit event
/// (<see cref="ResponseLedger.CaptureOfAsync"/>), the student's transcript checks from answered transcription
/// requests, and corrections from transcript versions. Used by AI grading (the panel is told which words may be
/// mis-heard), the grading workspace (the human is told, and sees what the student corrected) and the accuracy audit.
/// </summary>
public static class SpeechMarks
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Marks for the voice responses among <paramref name="responses"/>, by response id.</summary>
    public static async Task<Dictionary<Guid, SpeechMark>> LoadAsync(GadflyDbContext db, Guid organizationId, IReadOnlyCollection<StudentResponse> responses,
        CancellationToken ct = default)
    {
        var voice = responses.Where(r => r.Modality == "voice").ToList();
        var result = new Dictionary<Guid, SpeechMark>();
        if (voice.Count == 0) return result;
        var ids = voice.Select(r => r.Id).ToList();
        var idText = ids.Select(i => i.ToString()).ToList();

        var captures = (await db.AuditEvents.AsNoTracking()
                .Where(a => a.OrganizationId == organizationId && a.EntityType == nameof(StudentResponse) && a.EntityId != null && idText.Contains(a.EntityId)
                    && (a.Action == "response.captured" || a.Action == "response.revised"))
                .OrderBy(a => a.Id).Select(a => new { a.EntityId, a.AfterJson }).ToListAsync(ct))
            .GroupBy(a => a.EntityId!).ToDictionary(g => g.Key, g => g.First().AfterJson);
        var checkedByStudent = (await db.ClarificationEvents.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && ids.Contains(c.ResponseId) && c.Type == ClarificationType.Transcription && c.AnsweredAt != null)
                .Select(c => c.ResponseId).ToListAsync(ct)).ToHashSet();
        var transcriptionFixes = (await db.TranscriptVersions.AsNoTracking()
                .Where(v => v.OrganizationId == organizationId && ids.Contains(v.ResponseId) && v.CorrectionKind == CorrectionKind.Transcription)
                .Select(v => v.ResponseId).ToListAsync(ct)).ToHashSet();

        foreach (var r in voice)
        {
            var words = Words(captures.GetValueOrDefault(r.Id.ToString()));
            var recognized = r.SpeechRecognitionText ?? r.OriginalText;
            var corrected = transcriptionFixes.Contains(r.Id) && !string.Equals(recognized, r.VerifiedText, StringComparison.Ordinal);
            result[r.Id] = new SpeechMark(SpeechAccuracy.UncertainSegments(words), checkedByStudent.Contains(r.Id) || transcriptionFixes.Contains(r.Id),
                corrected, corrected ? SpeechAccuracy.WordErrors(r.VerifiedText, recognized).Errors : 0, words);
        }
        return result;
    }

    /// <summary>The recognized words in a capture audit event's JSON (empty when absent, redacted or unreadable).</summary>
    internal static IReadOnlyList<TimedWord> Words(string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(afterJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(afterJson);
            if (!doc.RootElement.TryGetProperty("capture", out var c) || c.ValueKind != JsonValueKind.Object) return [];
            return c.Deserialize<ResponseLedger.CapturedAnswer>(Json)?.Words?.Where(w => w is not null && double.IsFinite(w.Confidence)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { return []; }
    }
}
