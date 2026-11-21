using Tutor.Ingestion.Ocr;
using Tutor.Ingestion.Parsers;

namespace Gadfly.Sources;

/// <summary>A source file could not be read; the message is safe to show the instructor (no paths or stack).</summary>
public sealed class DocumentReadException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>Ceilings on one untrusted document (GAD-T155). The defaults suit the 20 MB upload cap.</summary>
/// <param name="MaxPages">Most PDF pages read.</param>
/// <param name="MaxChars">Most characters of text taken from one file.</param>
/// <param name="MaxUncompressedBytes">Most bytes a DOCX or EPUB may expand to (zip-bomb guard).</param>
/// <param name="Timeout">Longest one file may take to read.</param>
public sealed record DocumentReadLimits(
    int MaxPages = 500,
    long MaxChars = 5_000_000,
    long MaxUncompressedBytes = 200L * 1024 * 1024,
    TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromMinutes(2);
}

/// <summary>
/// Reads PDF, DOCX, EPUB and HTML through the Tutor.Ingestion parsers (TUT-T64) into located blocks: PDF
/// passages are located by page ("Page 12"), the others by chapter or heading. The files are untrusted
/// uploads, so every read is capped (pages, characters, expanded size, time), the file's signature must
/// match its extension, a damaged file becomes a <see cref="DocumentReadException"/> instead of crashing the
/// job, and nothing is run outside the process (no OCR, no LibreOffice or Calibre).
/// </summary>
public sealed class TutorIngestionExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".epub", ".html", ".htm", ".xhtml",
    };

    private readonly ParserRegistry _registry;
    private readonly DocumentReadLimits _limits;

    public TutorIngestionExtractor(DocumentReadLimits? limits = null)
    {
        _limits = limits ?? new DocumentReadLimits();
        var parserLimits = new ParserLimits(_limits.MaxPages, _limits.MaxChars, _limits.MaxUncompressedBytes);
        _registry = new ParserRegistry(
        [
            // No OCR: it would run native code over untrusted images. A scanned PDF reports "no text" instead.
            new PdfBookParser(new NoOpPdfOcrService(), parserLimits),
            new DocxBookParser(parserLimits),
            new EpubBookParser(parserLimits),
            new HtmlBookParser(parserLimits),
        ]);
    }

    public bool CanRead(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<IReadOnlyList<ExtractedBlock>> ExtractAsync(string path, CancellationToken ct = default)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var parser = _registry.Resolve(path) ?? throw new DocumentReadException($"\"{name}\": this file type is not supported.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_limits.EffectiveTimeout);
        ExtractedBook book;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (!SignatureMatches(stream, ext))
                throw new DocumentReadException($"\"{name}\" is not a valid {ext.TrimStart('.').ToUpperInvariant()} file (its content does not match its extension).");
            book = await parser.ParseAsync(stream, name, timeout.Token);
        }
        catch (DocumentReadException) { throw; }
        catch (DocumentLimitException ex) { throw new DocumentReadException($"\"{name}\": {ex.Message}", ex); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DocumentReadException($"\"{name}\" took longer than {_limits.EffectiveTimeout.TotalSeconds:0} seconds to read and was skipped.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Parser exceptions (bad xref, truncated zip, bad XML...) say nothing a user can act on.
            throw new DocumentReadException($"\"{name}\" is damaged or not a valid {ext.TrimStart('.').ToUpperInvariant()} file and could not be read.", ex);
        }

        var blocks = ToBlocks(book, ext == ".pdf");
        if (blocks.Count == 0)
        {
            var hint = ext == ".pdf" ? " It may be a scanned (image-only) PDF; scanned pages are not read." : "";
            throw new DocumentReadException($"\"{name}\" contains no readable text.{hint}");
        }
        return blocks;
    }

    /// <summary>Blocks from the parser's located sections: one per paragraph, a heading block per titled section.</summary>
    internal static List<ExtractedBlock> ToBlocks(ExtractedBook book, bool pagesAreLocations)
    {
        var blocks = new List<ExtractedBlock>();
        if (book.Sections.Count == 0)
        {
            AddParagraphs(blocks, book.PlainText, null, joinLines: false);
            return blocks;
        }
        foreach (var section in book.Sections)
        {
            if (!pagesAreLocations)
                blocks.Add(new ExtractedBlock(section.Label, section.Label, IsHeading: true));
            AddParagraphs(blocks, section.Text, section.Label, joinLines: pagesAreLocations);
        }
        return blocks;
    }

    private static void AddParagraphs(List<ExtractedBlock> blocks, string text, string? location, bool joinLines)
    {
        foreach (var para in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var t = joinLines
                ? string.Join(' ', para.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : para.Trim();
            if (t.Length > 0) blocks.Add(new ExtractedBlock(t, location));
        }
    }

    /// <summary>PDF files begin "%PDF-" (within the first KB), DOCX and EPUB are zips ("PK"); HTML has no signature.</summary>
    private static bool SignatureMatches(Stream s, string ext)
    {
        if (ext is ".html" or ".htm" or ".xhtml") return true;
        var buf = new byte[1024];
        var n = s.Read(buf, 0, buf.Length);
        s.Position = 0;
        return ext == ".pdf"
            ? System.Text.Encoding.ASCII.GetString(buf, 0, n).Contains("%PDF-", StringComparison.Ordinal)
            : n >= 4 && buf[0] == 0x50 && buf[1] == 0x4B;
    }
}
