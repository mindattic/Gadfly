using System.Text.Json;
using Gadfly.Mining;
using Gadfly.Prompts;
using MindAttic.Legion;

namespace Gadfly.Socrates;

public class InterrogationTurn
{
    /// <summary>"Socrates" or "You".</summary>
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Drives Prompt 1 as a terminal REPL. Each turn is a real Legion vote across the
/// trusted four + judge (<c>SynthesizeNarrative = true</c>) — four LLMs each draft a
/// probing question against the mined corpus + growing dialogue, and the judge
/// fuses/picks the sharpest one as the next question. This is what makes "the logic
/// in Legion" literally true for the interrogation itself, not just Prompt 3. Ends
/// when the human types "Aporia" (case-insensitive) or <c>maxTurns</c> is hit.
/// </summary>
public class InterrogationSession
{
    public static string DefaultSessionsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "sessions");

    private readonly ISocraticVotingClient client;
    private readonly Quorum quorum;
    private readonly int maxTurns;

    public List<InterrogationTurn> Transcript { get; } = new();

    public InterrogationSession(ISocraticVotingClient client, Quorum quorum = Quorum.Plurality, int maxTurns = 20)
    {
        this.client = client;
        this.quorum = quorum;
        this.maxTurns = maxTurns;
    }

    public static bool IsAporia(string input) =>
        input.Trim().Equals("aporia", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the loop. <paramref name="writeQuestion"/> is called with each generated
    /// question; <paramref name="readHumanReply"/> is then called to get the human's
    /// next reply — a console ReadLine in production, a canned queue in tests.
    /// Returns the full transcript (also left in <see cref="Transcript"/>).
    /// </summary>
    public async Task<IReadOnlyList<InterrogationTurn>> RunAsync(
        string corpusDigest,
        Func<string, Task> writeQuestion,
        Func<Task<string>> readHumanReply,
        CancellationToken ct = default)
    {
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var context = ContextBuilder.BuildInterrogationContext(
                corpusDigest,
                Transcript.Select(t => (t.Speaker, t.Text)).ToList());

            var result = await client.AskAsync(CanonicalPrompts.Interrogation, context, quorum, ct);

            var question = !string.IsNullOrWhiteSpace(result.Consensus) ? result.Consensus : result.NarrativeSummary;
            if (string.IsNullOrWhiteSpace(question))
                question = "I have nothing further to ask — the panel returned no usable question this turn.";

            Transcript.Add(new InterrogationTurn { Speaker = "Socrates", Text = question });
            await writeQuestion(question);

            var reply = await readHumanReply();
            Transcript.Add(new InterrogationTurn { Speaker = "You", Text = reply });

            if (IsAporia(reply))
                break;
        }

        return Transcript;
    }

    /// <summary>Persists <see cref="Transcript"/> to a timestamped JSONL file so a later
    /// <c>gadfly reflect --last</c> can read it back. Returns the file path.</summary>
    public string SaveTranscript(string? sessionsDir = null)
    {
        var dir = sessionsDir ?? DefaultSessionsDir;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        using var writer = new StreamWriter(path);
        foreach (var turn in Transcript)
            writer.WriteLine(JsonSerializer.Serialize(turn));
        return path;
    }

    /// <summary>Loads the most recently saved transcript, or an empty list if none exists.</summary>
    public static IReadOnlyList<InterrogationTurn> LoadLastTranscript(string? sessionsDir = null)
    {
        var dir = sessionsDir ?? DefaultSessionsDir;
        if (!Directory.Exists(dir)) return Array.Empty<InterrogationTurn>();

        var last = Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (last is null) return Array.Empty<InterrogationTurn>();

        return File.ReadLines(last)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<InterrogationTurn>(l))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }
}
