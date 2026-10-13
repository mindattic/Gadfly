namespace Gadfly.Ingestion;

/// <summary>
/// Enumerates Claude Code's local transcript store under
/// <c>%USERPROFILE%\.claude\projects\&lt;hyphenated-cwd&gt;\*.jsonl</c>.
/// Some project folders only contain a <c>memory\</c> subfolder (Claude Code's own
/// auto-memory <c>.md</c> files, unrelated to raw transcripts) with no top-level
/// <c>.jsonl</c> — those are skipped, not treated as an error.
/// </summary>
public static class ClaudeCodeProjectScanner
{
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public readonly record struct ProjectTranscripts(string ProjectFolder, IReadOnlyList<string> JsonlPaths);

    /// <summary>Scans every project folder under <paramref name="root"/> (default: the real Claude Code store)
    /// and yields one entry per folder that has at least one top-level <c>.jsonl</c> file.</summary>
    public static IEnumerable<ProjectTranscripts> ScanAll(string? root = null)
    {
        var dir = root ?? DefaultRoot;
        if (!Directory.Exists(dir)) yield break;

        foreach (var projectDir in Directory.EnumerateDirectories(dir))
        {
            var jsonlPaths = Directory
                .EnumerateFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly)
                .ToList();

            if (jsonlPaths.Count == 0) continue; // memory-only folder, or empty — nothing to ingest

            yield return new ProjectTranscripts(Path.GetFileName(projectDir), jsonlPaths);
        }
    }

    /// <summary>
    /// Resolves the hyphenated folder name Claude Code uses for a given project working
    /// directory, e.g. <c>D:\Projects\MindAttic\Gadfly</c> → <c>D--Projects-MindAttic-Gadfly</c>
    /// (drive colon, path separators, and dots each become a hyphen). Used to implement
    /// <c>gadfly ingest --project &lt;cwd&gt;</c>.
    /// </summary>
    public static string FolderNameFor(string projectPath) =>
        string.Concat(projectPath.Select(c => c is ':' or '\\' or '/' or '.' ? '-' : c));
}
