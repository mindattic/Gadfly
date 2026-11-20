using System.Security.Cryptography;
using System.Text;

namespace Gadfly.Engine;

/// <summary>
/// A named, versioned set of prompt texts a session runs on. Prompt sets are constants in code
/// (never user-editable); each session records the <see cref="Sha256"/> of the set it ran, so an
/// audit can prove exactly which wording examined a student.
/// </summary>
public sealed class PromptSet
{
    public PromptSet(string id, int version, IReadOnlyDictionary<string, string> prompts)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A prompt set needs an id.", nameof(id));
        if (version < 1) throw new ArgumentException("Prompt set versions start at 1.", nameof(version));
        if (prompts.Count == 0) throw new ArgumentException("A prompt set needs at least one prompt.", nameof(prompts));
        Id = id;
        Version = version;
        Prompts = new SortedDictionary<string, string>(prompts.ToDictionary(), StringComparer.Ordinal);
        Sha256 = ComputeHash(Id, Version, Prompts);
    }

    /// <summary>Stable name, e.g. "personal" or "exam".</summary>
    public string Id { get; }

    /// <summary>Bumped whenever any prompt's wording changes.</summary>
    public int Version { get; }

    /// <summary>Prompt texts by key, e.g. "question", "judge".</summary>
    public IReadOnlyDictionary<string, string> Prompts { get; }

    /// <summary>Lowercase hex SHA-256 over id, version and every key/text pair in ordinal key order.</summary>
    public string Sha256 { get; }

    /// <summary>The prompt for <paramref name="key"/>; throws if the set has no such prompt.</summary>
    public string this[string key] =>
        Prompts.TryGetValue(key, out var text)
            ? text
            : throw new KeyNotFoundException($"Prompt set '{Id}' v{Version} has no prompt '{key}'.");

    private static string ComputeHash(string id, int version, IReadOnlyDictionary<string, string> prompts)
    {
        // Length-prefix every field so no two different sets can serialize to the same bytes.
        var sb = new StringBuilder();
        void Field(string s) => sb.Append(s.Length).Append(':').Append(s).Append('\n');
        Field(id);
        Field(version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (key, text) in prompts)
        {
            Field(key);
            Field(text);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
