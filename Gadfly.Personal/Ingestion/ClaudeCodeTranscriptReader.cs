using System.Text.Json;

namespace Gadfly.Ingestion;

/// <summary>
/// Streams one Claude Code session transcript (<c>.jsonl</c>) and extracts
/// human- and assistant-authored text as <see cref="CorpusMessage"/>s.
///
/// Uses an allow-list on the top-level <c>type</c> field (<c>user</c>/<c>assistant</c>)
/// rather than a deny-list, since harness line types (<c>mode</c>, <c>file-history-snapshot</c>,
/// <c>bridge-session</c>, ...) keep growing and a deny-list would silently start admitting
/// unknown future line shapes.
/// </summary>
public static class ClaudeCodeTranscriptReader
{
    public static IEnumerable<CorpusMessage> ReadFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { /* tolerate a truncated last line mid-write */ }
            if (doc is null) continue;

            using (doc)
            {
                foreach (var extracted in ReadLine(doc.RootElement))
                    yield return extracted;
            }
        }
    }

    internal static IEnumerable<CorpusMessage> ReadLine(JsonElement root)
    {
        if (!TryGetString(root, "type", out var type) || type is not ("user" or "assistant"))
            yield break;

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            yield break;

        TryGetString(root, "sessionId", out var sessionId);
        TryGetString(root, "cwd", out var cwd);
        TryGetString(root, "uuid", out var lineUuid);
        var messageId = lineUuid ?? Guid.NewGuid().ToString("N");

        var timestamp = TryGetString(root, "timestamp", out var ts) && DateTimeOffset.TryParse(ts, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        if (type == "user")
        {
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                yield break; // array content means a tool_result block, not something the human typed

            var originIsHuman = root.TryGetProperty("origin", out var origin)
                && TryGetString(origin, "kind", out var kind) && kind == "human";
            var promptSourceTyped = TryGetString(root, "promptSource", out var promptSource) && promptSource == "typed";
            if (!originIsHuman && !promptSourceTyped) yield break;

            var text = content.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(text)) yield break;

            yield return new CorpusMessage
            {
                Id = messageId,
                Source = CorpusSource.ClaudeCodeCli,
                Role = CorpusRole.Human,
                SessionId = sessionId ?? "",
                Text = text,
                TimestampUtc = timestamp,
                ProjectPath = cwd,
            };
        }
        else // assistant
        {
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                yield break;

            var blockIndex = 0;
            foreach (var block in content.EnumerateArray())
            {
                blockIndex++;
                if (!TryGetString(block, "type", out var blockType) || blockType != "text") continue;
                if (!TryGetString(block, "text", out var text) || string.IsNullOrWhiteSpace(text)) continue;

                yield return new CorpusMessage
                {
                    Id = $"{messageId}:{blockIndex}",
                    Source = CorpusSource.ClaudeCodeCli,
                    Role = CorpusRole.Assistant,
                    SessionId = sessionId ?? "",
                    Text = text,
                    TimestampUtc = timestamp,
                    ProjectPath = cwd,
                };
            }
        }
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString() ?? "";
        return true;
    }
}
