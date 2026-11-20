using System.IO.Compression;
using System.Text.Json;

namespace Gadfly.Ingestion;

/// <summary>
/// Imports Anthropic's claude.ai account data export (Settings → Privacy →
/// Export data → an emailed zip containing <c>conversations.json</c>). There is
/// no live API for reading claude.ai conversation history — this file-based
/// export is the only ingestion path.
///
/// <para>
/// The exact internal schema of <c>conversations.json</c> has <b>not</b> been
/// verified against a real export as of writing this importer. It is written
/// defensively against a reasonable assumed shape — a JSON array of conversation
/// objects, each with a <c>chat_messages</c> (or <c>messages</c>) array of
/// <c>{ sender, text }</c>-shaped entries — and throws
/// <see cref="ExportSchemaException"/> with a concrete diagnosis when that shape
/// doesn't hold, rather than silently ingesting partial or garbage records.
/// Expect to correct this against the first real export.
/// </para>
/// </summary>
public static class ClaudeAiExportImporter
{
    public static IReadOnlyList<CorpusMessage> Import(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Export zip not found: {zipPath}", zipPath);

        var extractDir = Path.Combine(Path.GetTempPath(), "gadfly-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var conversationsPath = Directory
                .EnumerateFiles(extractDir, "conversations.json", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (conversationsPath is null)
                throw new ExportSchemaException(
                    "No conversations.json found inside the export zip. Confirm this is an Anthropic " +
                    "account-data export (Settings → Privacy → Export data).");

            return ParseConversations(File.ReadAllText(conversationsPath));
        }
        finally
        {
            try { Directory.Delete(extractDir, recursive: true); } catch { /* best-effort cleanup of our own temp dir */ }
        }
    }

    internal static IReadOnlyList<CorpusMessage> ParseConversations(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new ExportSchemaException($"conversations.json is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new ExportSchemaException(
                    $"Expected conversations.json to be a top-level JSON array; found {doc.RootElement.ValueKind}.");

            var result = new List<CorpusMessage>();
            var conversationCount = 0;
            var conversationsWithMessages = 0;

            foreach (var conversation in doc.RootElement.EnumerateArray())
            {
                conversationCount++;

                var sessionId = TryGetString(conversation, "uuid") ?? TryGetString(conversation, "id") ?? Guid.NewGuid().ToString("N");

                var messagesProp = TryGetProperty(conversation, "chat_messages") ?? TryGetProperty(conversation, "messages");
                if (messagesProp is not { ValueKind: JsonValueKind.Array }) continue;

                conversationsWithMessages++;

                var index = 0;
                foreach (var message in messagesProp.Value.EnumerateArray())
                {
                    index++;
                    var sender = TryGetString(message, "sender") ?? TryGetString(message, "role");
                    var role = sender?.ToLowerInvariant() switch
                    {
                        "human" or "user" => CorpusRole.Human,
                        "assistant" or "model" => CorpusRole.Assistant,
                        _ => (CorpusRole?)null,
                    };
                    if (role is null) continue;

                    var text = ExtractText(message);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var timestamp = TryGetDate(message, "created_at") ?? TryGetDate(conversation, "created_at") ?? DateTimeOffset.MinValue;

                    result.Add(new CorpusMessage
                    {
                        Id = $"claudeai:{sessionId}:{index}",
                        Source = CorpusSource.ClaudeAiExport,
                        Role = role.Value,
                        SessionId = sessionId,
                        Text = text,
                        TimestampUtc = timestamp,
                        ProjectPath = null,
                    });
                }
            }

            if (conversationCount > 0 && conversationsWithMessages == 0)
            {
                throw new ExportSchemaException(
                    $"Found {conversationCount} conversation object(s) in conversations.json, but none had a " +
                    "recognizable messages array (expected a \"chat_messages\" or \"messages\" field). The export " +
                    $"schema may have changed — inspect a sample conversation object and update {nameof(ClaudeAiExportImporter)}.");
            }

            return result;
        }
    }

    private static string? ExtractText(JsonElement message)
    {
        var text = TryGetString(message, "text");
        if (!string.IsNullOrWhiteSpace(text)) return text;

        var content = TryGetProperty(message, "content");
        if (content is null) return null;

        if (content.Value.ValueKind == JsonValueKind.String)
            return content.Value.GetString();

        if (content.Value.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in content.Value.EnumerateArray())
            {
                var blockText = TryGetString(block, "text");
                if (!string.IsNullOrWhiteSpace(blockText)) parts.Add(blockText!);
            }
            if (parts.Count > 0) return string.Join("\n", parts);
        }

        return null;
    }

    private static JsonElement? TryGetProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value
            : null;

    private static string? TryGetString(JsonElement element, string property)
    {
        var value = TryGetProperty(element, property);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    private static DateTimeOffset? TryGetDate(JsonElement element, string property)
    {
        var s = TryGetString(element, property);
        return s is not null && DateTimeOffset.TryParse(s, out var parsed) ? parsed : null;
    }
}
