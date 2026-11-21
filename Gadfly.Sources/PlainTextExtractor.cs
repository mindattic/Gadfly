using System.Text;

namespace Gadfly.Sources;

/// <summary>
/// Reads .txt and Markdown files into blocks: one per paragraph, with Markdown headings tracked
/// as the location ("Chapter 1 > Cells") so citations say where a passage came from.
/// </summary>
public sealed class PlainTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown",
    };

    public bool CanRead(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<IReadOnlyList<ExtractedBlock>> ExtractAsync(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        var isMarkdown = Path.GetExtension(path).StartsWith(".m", StringComparison.OrdinalIgnoreCase);
        return Split(text, isMarkdown);
    }

    internal static IReadOnlyList<ExtractedBlock> Split(string text, bool isMarkdown)
    {
        var blocks = new List<ExtractedBlock>();
        var headings = new List<(int Level, string Title)>();
        var para = new StringBuilder();

        string? Location() => headings.Count == 0 ? null : string.Join(" > ", headings.Select(h => h.Title));

        void Flush()
        {
            var t = para.ToString().Trim();
            if (t.Length > 0) blocks.Add(new ExtractedBlock(t, Location()));
            para.Clear();
        }

        var inFence = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (isMarkdown && line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            if (isMarkdown && !inFence && line.StartsWith('#'))
            {
                var level = line.TakeWhile(c => c == '#').Count();
                var title = line[level..].Trim();
                if (level <= 6 && title.Length > 0 && line[level] is ' ' or '\t')
                {
                    Flush();
                    headings.RemoveAll(h => h.Level >= level);
                    headings.Add((level, title));
                    blocks.Add(new ExtractedBlock(title, Location(), IsHeading: true));
                    continue;
                }
            }
            if (line.Trim().Length == 0)
            {
                Flush();
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(line.Trim());
        }
        Flush();
        return blocks;
    }
}
