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
    /// manifest, then persists the updated manifest. Returns the count actually appended.</summary>
    public int AppendNew(IEnumerable<CorpusMessage> messages)
    {
        Directory.CreateDirectory(root);
        var manifest = LoadManifest();
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

    public IEnumerable<CorpusMessage> ReadAll()
    {
        if (!File.Exists(messagesPath)) yield break;
        foreach (var line in File.ReadLines(messagesPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var message = JsonSerializer.Deserialize<CorpusMessage>(line, JsonOptions);
            if (message is not null) yield return message;
        }
    }
}

public class CorpusManifest
{
    public HashSet<string> IngestedMessageIds { get; set; } = new();
}
