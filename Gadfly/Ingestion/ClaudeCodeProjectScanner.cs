namespace Gadfly.Ingestion;

/// <summary>
/// Enumerates Claude Code's local transcript store under
/// <c>&lt;config-dir&gt;\projects\&lt;hyphenated-cwd&gt;\*.jsonl</c>, where the config dir is
/// <c>CLAUDE_CONFIG_DIR</c> (the current login) or <c>%USERPROFILE%\.claude</c>.
/// Some project folders only contain a <c>memory\</c> subfolder (Claude Code's own
/// auto-memory <c>.md</c> files, unrelated to raw transcripts) with no top-level
/// <c>.jsonl</c> — those are skipped, not treated as an error.
/// </summary>
public static class ClaudeCodeProjectScanner
{
    /// <summary>
    /// The current login's transcript store: <c>%CLAUDE_CONFIG_DIR%\projects</c> when Claude Code
    /// runs under a per-account config dir (the MindAttic multi-account setup), else
    /// <c>%USERPROFILE%\.claude\projects</c>.
    /// </summary>
    public static string DefaultRoot => RootFor(CurrentConfigDir());

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string CurrentConfigDir()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configDir) ? Path.Combine(UserProfile, ".claude") : configDir.Trim();
    }

    private static string RootFor(string configDir) => Path.Combine(configDir, "projects");

    /// <summary>
    /// Every login's transcript store on this machine, for <c>ingest --all-accounts</c>: the current
    /// login (<see cref="DefaultRoot"/>), the plain <c>~\.claude</c> login, and every per-account
    /// config dir under <c>~\.claude-accounts\*</c> (plus the siblings of <c>CLAUDE_CONFIG_DIR</c>
    /// when it lives in some other accounts folder). Deduped by full path; only existing roots.
    /// </summary>
    public static IReadOnlyList<string> AllAccountRoots()
    {
        var configDirs = new List<string> { CurrentConfigDir(), Path.Combine(UserProfile, ".claude") };

        var accountParents = new List<string> { Path.Combine(UserProfile, ".claude-accounts") };
        var envDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(envDir.Trim())));
            // Only treat the parent as an accounts folder when it isn't the user profile itself
            // (CLAUDE_CONFIG_DIR=~\.claude-foo would otherwise scan every dot-folder in the profile).
            if (parent is not null && !string.Equals(Path.TrimEndingDirectorySeparator(parent),
                    Path.TrimEndingDirectorySeparator(UserProfile), StringComparison.OrdinalIgnoreCase))
                accountParents.Add(parent);
        }

        foreach (var parent in accountParents)
            if (Directory.Exists(parent))
                configDirs.AddRange(Directory.EnumerateDirectories(parent));

        return configDirs
            .Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootFor(d))))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
    /// (every character that is not an ASCII letter or digit — drive colon, path separators,
    /// dots, spaces, underscores, ... — becomes a hyphen, as Claude Code itself does). Used to
    /// implement <c>gadfly ingest --project &lt;cwd&gt;</c>.
    /// </summary>
    public static string FolderNameFor(string projectPath) =>
        string.Concat(projectPath.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
}
