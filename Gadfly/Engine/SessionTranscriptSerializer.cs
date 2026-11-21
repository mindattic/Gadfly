using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gadfly.Engine;

/// <summary>
/// Reads and writes a whole <see cref="SocraticSession"/> as JSON: the CLI's on-disk transcript and
/// the archive format the server exports. Enum values are written as names so files stay readable.
/// </summary>
public static class SessionTranscriptSerializer
{
    /// <summary>Bumped when the JSON shape changes incompatibly.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(SocraticSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var dto = new Dto
        {
            FormatVersion = FormatVersion,
            Id = session.Id,
            SubjectId = session.SubjectId,
            Policy = session.Policy,
            PromptSetId = session.PromptSetId,
            PromptSetVersion = session.PromptSetVersion,
            PromptSetSha256 = session.PromptSetSha256,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            EndReason = session.EndReason,
            ClockHeld = session.ClockHeld ? true : null,
            Turns = [.. session.Turns],
            Mastery = [.. session.Mastery.Values.OrderBy(m => m.ConceptId, StringComparer.Ordinal)],
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static SocraticSession Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, Options)
                  ?? throw new JsonException("The transcript is empty.");
        if (dto.FormatVersion != FormatVersion)
            throw new JsonException($"Transcript format {dto.FormatVersion} is not supported (expected {FormatVersion}).");
        if (string.IsNullOrWhiteSpace(dto.SubjectId) || dto.Policy is null || string.IsNullOrWhiteSpace(dto.PromptSetSha256))
            throw new JsonException("The transcript is missing its subject, policy or prompt-set hash.");
        for (var i = 0; i < dto.Turns.Count; i++)
            if (dto.Turns[i].Index != i)
                throw new JsonException($"Turn {i} is recorded with index {dto.Turns[i].Index}.");
        return new SocraticSession(dto.Id, dto.SubjectId, dto.Policy, dto.PromptSetId, dto.PromptSetVersion,
            dto.PromptSetSha256, dto.StartedAt, dto.EndedAt, dto.EndReason, dto.Turns, dto.Mastery, dto.ClockHeld == true);
    }

    private sealed class Dto
    {
        public int FormatVersion { get; set; }
        public Guid Id { get; set; }
        public string SubjectId { get; set; } = "";
        public SessionPolicy? Policy { get; set; }
        public string PromptSetId { get; set; } = "";
        public int PromptSetVersion { get; set; }
        public string PromptSetSha256 { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public EndReason EndReason { get; set; }
        /// <summary>Written only while the clock waits for the first question (GAD-T239); absent means it is running.</summary>
        public bool? ClockHeld { get; set; }
        public List<Turn> Turns { get; set; } = [];
        public List<MasterySnapshot> Mastery { get; set; } = [];
    }
}
