using System.Text.Json;
using System.Text.Json.Serialization;
using Gadfly.Ingestion;

namespace Gadfly.Corpus;

/// <summary>
/// Appends normalized <see cref="CorpusMessage"/>s to a flat JSONL file under
/// <c>%APPDATA%\MindAttic\Gadfly\corpus\messages.jsonl</c>, deduping by message
/// <see cref="CorpusMessage.Id"/> via a <c>manifest.json</c> so re-running
/// <c>ingest</c>/<c>import</c> is idempotent. Flat files over SQLite: the corpus
/// is personal-scale (thousands of messages), and this matches the rest of the
/// family's plain-JSON-file conventions (<c>legion.json</c>, <c>providers.json</c>).
/// </summary>
public class CorpusStore
{
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "corpus");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string root;
    private readonly string messagesPath;
    private readonly string manifestPath;

    public CorpusStore(string? root = null)
    {
        this.root = root ?? DefaultRoot;
        messagesPath = Path.Combine(this.root, "messages.jsonl");
        manifestPath = Path.Combine(this.root, "manifest.json");
    }

    public CorpusManifest LoadManifest()
    {
        if (!File.Exists(manifestPath)) return new CorpusManifest();
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<CorpusManifest>(json, JsonOptions) ?? new CorpusManifest();
    }

    private void SaveManifest(CorpusManifest manifest)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    /// <summary>Appends every message whose <see cref="CorpusMessage.Id"/> isn't already in the
    /// manifest, then persists the updated manifest. Returns the count actually appended.
    /// A torn last line (a write interrupted mid-record) is first set aside via
    /// <see cref="RepairTornTail"/> so the new records start on a clean line and the torn
    /// message is re-ingested rather than lost behind its manifest entry.</summary>
    public int AppendNew(IEnumerable<CorpusMessage> messages)
    {
        Directory.CreateDirectory(root);
        var manifest = LoadManifest();
        if (RepairTornTail())
        {
            // The torn record's id is in the manifest but its line is gone: rebuild the id set
            // from the records that actually survive, so that message is appended again.
            manifest.IngestedMessageIds = new HashSet<string>(ReadAll().Select(m => m.Id));
        }
        var appended = 0;

        using (var writer = new StreamWriter(messagesPath, append: true))
        {
            foreach (var message in messages)
            {
                if (!manifest.IngestedMessageIds.Add(message.Id)) continue;
                writer.WriteLine(JsonSerializer.Serialize(message, JsonOptions));
                appended++;
            }
        }

        SaveManifest(manifest);
        return appended;
    }

    /// <summary>
    /// Every complete record ends with a newline (<see cref="AppendNew"/> uses WriteLine), so bytes
    /// after the last newline are a torn record. Moves them to <c>messages.torn.txt</c> (kept, not
    /// deleted) and truncates <c>messages.jsonl</c> back to its last complete line. Returns true if
    /// anything was set aside.
    /// </summary>
    private bool RepairTornTail()
    {
        if (!File.Exists(messagesPath)) return false;
        using var stream = new FileStream(messagesPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length == 0) return false;

        // Find the last newline by scanning backwards in chunks.
        var buffer = new byte[4096];
        var end = stream.Length;
        long lastNewline = -1;
        while (end > 0 && lastNewline < 0)
        {
            var start = Math.Max(0, end - buffer.Length);
            var count = (int)(end - start);
            stream.Position = start;
            stream.ReadExactly(buffer, 0, count);
            for (var i = count - 1; i >= 0; i--)
                if (buffer[i] == (byte)'\n') { lastNewline = start + i; break; }
            end = start;
        }

        var keep = lastNewline + 1; // 0 when the file holds no complete line at all
        if (keep == stream.Length) return false;

        var tail = new byte[stream.Length - keep];
        stream.Position = keep;
        stream.ReadExactly(tail, 0, tail.Length);
        if (tail.All(b => b is (byte)' ' or (byte)'\t' or (byte)'\r')) return false; // whitespace only

        // A complete record that only lost its trailing newline is not torn: finish the line.
        try
        {
            if (JsonSerializer.Deserialize<CorpusMessage>(tail, JsonOptions) is not null)
            {
                stream.Position = stream.Length;
                stream.WriteByte((byte)'\n');
                return false;
            }
        }
        catch (JsonException) { }

        using (var torn = new FileStream(Path.Combine(root, "messages.torn.txt"), FileMode.Append, FileAccess.Write))
        {
            torn.Write(tail);
            torn.WriteByte((byte)'\n');
        }
        stream.SetLength(keep);
        Console.Error.WriteLine($"corpus: set aside a torn last line of messages.jsonl ({tail.Length} bytes) in messages.torn.txt; that message will be re-ingested.");
        return true;
    }

    /// <summary>Reads every record. A torn LAST line (an interrupted append) is skipped with a
    /// warning instead of throwing, so one crashed write can't brick every command; the next
    /// <see cref="AppendNew"/> repairs it. A malformed line anywhere else still throws.</summary>
    public IEnumerable<CorpusMessage> ReadAll()
    {
        if (!File.Exists(messagesPath)) yield break;

        string? pending = null;
        var pendingLineNumber = 0;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(messagesPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (pending is not null)
            {
                var message = JsonSerializer.Deserialize<CorpusMessage>(pending, JsonOptions);
                if (message is not null) yield return message;
            }
            pending = line;
            pendingLineNumber = lineNumber;
        }

        if (pending is null) yield break;
        CorpusMessage? last;
        try
        {
            last = JsonSerializer.Deserialize<CorpusMessage>(pending, JsonOptions);
        }
        catch (JsonException)
        {
            Console.Error.WriteLine($"corpus: skipping a torn last line ({pendingLineNumber}) of messages.jsonl; the next ingest/import will repair it.");
            yield break;
        }
        if (last is not null) yield return last;
    }
}

public class CorpusManifest
{
    public HashSet<string> IngestedMessageIds { get; set; } = new();
}
