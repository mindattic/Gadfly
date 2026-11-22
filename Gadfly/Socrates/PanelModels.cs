namespace Gadfly.Socrates;

/// <summary>
/// The provider model behind each panel member, read from configuration without calling any provider (GAD-T429).
/// A grading-panel calibration records these ids, and it expires when any of them changes: a provider can swap the model
/// behind a name, and agreement measured on one model says nothing about the next (ElenchusAtScale Section 6.6, Appendix A.8).
/// </summary>
public interface IPanelModels
{
    /// <summary>Provider id → model id of every member the panel would call now (case-insensitive keys).</summary>
    IReadOnlyDictionary<string, string> ConfiguredModels { get; }
}

public static class PanelModelSet
{
    /// <summary>The models as one readable line, e.g. "claude: claude-sonnet-5, openai: gpt-5", sorted by provider.</summary>
    public static string Describe(IReadOnlyDictionary<string, string>? models) =>
        models is null || models.Count == 0 ? "(models not recorded)"
            : string.Join(", ", models.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).Select(m => $"{m.Key}: {(m.Value.Length == 0 ? "(default)" : m.Value)}"));

    /// <summary>
    /// What differs between the models a calibration covered and the panel's models now: members added, removed or running
    /// a different model id. Empty when they are the same set.
    /// </summary>
    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string>? calibrated, IReadOnlyDictionary<string, string>? current)
    {
        var before = Normalize(calibrated);
        var now = Normalize(current);
        var changes = new List<string>();
        foreach (var (provider, model) in now.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(provider, out var old)) changes.Add($"{provider} ({model}) joined the panel");
            else if (!string.Equals(old, model, StringComparison.OrdinalIgnoreCase)) changes.Add($"{provider} now runs {model} instead of {old}");
        }
        foreach (var (provider, model) in before.OrderBy(m => m.Key, StringComparer.Ordinal))
            if (!now.ContainsKey(provider)) changes.Add($"{provider} ({model}) left the panel");
        return changes;
    }

    private static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? models) =>
        (models ?? new Dictionary<string, string>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Key))
            .GroupBy(m => m.Key.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => (g.First().Value ?? "").Trim(), StringComparer.Ordinal);
}
