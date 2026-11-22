using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Gadfly.Server.Transcripts;

/// <summary>
/// The Word download (GAD-T295): a .docx written directly as Office Open XML (a zip of XML parts), with no third-party
/// library. Named paragraph styles (Title, Heading 1/2, Quote and Gadfly's own), the unsure words highlighted, a footer
/// carrying the copy, the verification code and the page number, and the title and subject in the file's properties.
/// Opens in Word, LibreOffice, Google Docs and Pages, and keeps every character (unlike the PDF's standard fonts).
/// </summary>
public static class TranscriptDocx
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Render(TranscriptDocument d, TranscriptStamp stamp)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRels);
            Add(zip, "word/_rels/document.xml.rels", DocumentRels);
            Add(zip, "word/styles.xml", Styles);
            Add(zip, "word/settings.xml", $"<w:settings xmlns:w=\"{W}\"><w:defaultTabStop w:val=\"720\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
            Add(zip, "word/footer1.xml", Footer(d, stamp));
            Add(zip, "word/document.xml", Document(d, stamp));
            Add(zip, "docProps/core.xml", Core(d, stamp));
            Add(zip, "docProps/app.xml", "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Gadfly</Application></Properties>");
        }
        return ms.ToArray();
    }

    private static string Document(TranscriptDocument d, TranscriptStamp stamp)
    {
        var sb = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>");
        foreach (var block in TranscriptLayout.Blocks(d, stamp))
        {
            var style = block.Kind switch
            {
                BlockKind.Title => "Title",
                BlockKind.Heading => "Heading1",
                BlockKind.Subheading => "Heading2",
                BlockKind.Meta => "GadflyMeta",
                BlockKind.Speaker => "GadflySpeaker",
                BlockKind.Quote => "Quote",
                BlockKind.Small => "GadflySmall",
                BlockKind.Notice => "GadflyNotice",
                BlockKind.Mono => "GadflyMono",
                _ => "Normal",
            };
            sb.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(style).Append("\"/>");
            if (block.Indent > 0) sb.Append("<w:ind w:left=\"").Append(block.Indent * 360 + (block.Kind == BlockKind.Quote ? 360 : 0)).Append("\"/>");
            sb.Append("</w:pPr>");
            foreach (var run in block.Runs) AppendRun(sb, run);
            sb.Append("</w:p>");
        }
        sb.Append("<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rIdFooter\"/>")
          .Append("<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"567\" w:footer=\"567\" w:gutter=\"0\"/>")
          .Append("</w:sectPr></w:body></w:document>");
        return sb.ToString();
    }

    private static void AppendRun(StringBuilder sb, Run run)
    {
        var text = run.Mark ? "[" + run.Text + "?]" : run.Text;
        if (text.Length == 0) return;
        sb.Append("<w:r>");
        if (run.Bold || run.Italic || run.Mark || run.Mono)
        {
            sb.Append("<w:rPr>");
            if (run.Mono) sb.Append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/>");
            if (run.Bold) sb.Append("<w:b/>");
            if (run.Italic) sb.Append("<w:i/>");
            if (run.Mark) sb.Append("<w:highlight w:val=\"yellow\"/><w:u w:val=\"dotted\"/>");
            sb.Append("</w:rPr>");
        }
        // Line breaks inside an answer become Word line breaks.
        var parts = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("<w:br/>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(X(parts[i])).Append("</w:t>");
        }
        sb.Append("</w:r>");
    }

    private static string Footer(TranscriptDocument d, TranscriptStamp stamp) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:ftr xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:p><w:pPr><w:pStyle w:val=\"GadflyFooter\"/></w:pPr>"
        + $"<w:r><w:t xml:space=\"preserve\">{X($"Gadfly transcript · {TranscriptLayout.CopyLabel(d.Audience)} · verification code {stamp.Code} · page ")}</w:t></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        + "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> NUMPAGES </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
        + "</w:p></w:ftr>";

    private static string Core(TranscriptDocument d, TranscriptStamp stamp)
    {
        var created = stamp.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" "
            + "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + $"<dc:title>{X($"Transcript: {d.ExamTitle}")}</dc:title>"
            + $"<dc:subject>{X($"{d.StudentName}, attempt {d.Attempt} ({TranscriptLayout.CopyLabel(d.Audience)})")}</dc:subject>"
            + "<dc:creator>Gadfly</dc:creator>"
            + $"<dc:identifier>{X(stamp.Code)}</dc:identifier>"
            + $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:created>"
            + "</cp:coreProperties>";
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(new UTF8Encoding(false).GetBytes(xml));
    }

    /// <summary>XML-escaped text with the characters XML 1.0 forbids removed.</summary>
    private static string X(string s) => SecurityElement.Escape(new string(s.Where(c => c is '\t' or '\n' or '\r' || c >= 0x20 && c != '￾' && c != '￿').ToArray())) ?? "";

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
          <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
          <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
          <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
        </Types>
        """;

    private const string RootRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
        </Relationships>
        """;

    private const string DocumentRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
          <Relationship Id="rIdSettings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
          <Relationship Id="rIdFooter" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
        </Relationships>
        """;

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults>
            <w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Calibri" w:cs="Calibri"/><w:sz w:val="21"/><w:szCs w:val="21"/><w:lang w:val="en-US"/></w:rPr></w:rPrDefault>
            <w:pPrDefault><w:pPr><w:spacing w:after="60" w:line="264" w:lineRule="auto"/></w:pPr></w:pPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="4" w:color="BFBAAD"/></w:pBdr><w:spacing w:after="160"/></w:pPr><w:rPr><w:b/><w:sz w:val="40"/><w:szCs w:val="40"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:keepNext/><w:spacing w:before="320" w:after="60"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:keepNext/><w:spacing w:before="100" w:after="20"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:color w:val="444444"/><w:sz w:val="19"/><w:szCs w:val="19"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Quote"><w:name w:val="Quote"/><w:basedOn w:val="Normal"/><w:qFormat/>
            <w:pPr><w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="C9C4B5"/></w:pBdr><w:ind w:left="360"/><w:spacing w:after="80"/></w:pPr><w:rPr><w:rFonts w:ascii="Georgia" w:hAnsi="Georgia" w:cs="Georgia"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflyMeta"><w:name w:val="Gadfly Meta"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="20"/></w:pPr><w:rPr><w:sz w:val="20"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflySpeaker"><w:name w:val="Gadfly Speaker"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="120" w:after="20"/></w:pPr><w:rPr><w:color w:val="333333"/><w:sz w:val="19"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflySmall"><w:name w:val="Gadfly Small"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="20"/></w:pPr><w:rPr><w:color w:val="555555"/><w:sz w:val="17"/><w:szCs w:val="17"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflyNotice"><w:name w:val="Gadfly Notice"/><w:basedOn w:val="Normal"/>
            <w:pPr><w:pBdr><w:top w:val="single" w:sz="4" w:space="4" w:color="E3DCC5"/><w:left w:val="single" w:sz="4" w:space="4" w:color="E3DCC5"/><w:bottom w:val="single" w:sz="4" w:space="4" w:color="E3DCC5"/><w:right w:val="single" w:sz="4" w:space="4" w:color="E3DCC5"/></w:pBdr><w:shd w:val="clear" w:color="auto" w:fill="F7F4EA"/><w:spacing w:before="80" w:after="80"/></w:pPr><w:rPr><w:sz w:val="18"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflyMono"><w:name w:val="Gadfly Mono"/><w:basedOn w:val="Normal"/><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:cs="Consolas"/><w:color w:val="444444"/><w:sz w:val="16"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="GadflyFooter"><w:name w:val="Gadfly Footer"/><w:basedOn w:val="Normal"/><w:pPr><w:jc w:val="center"/></w:pPr><w:rPr><w:color w:val="666666"/><w:sz w:val="15"/></w:rPr></w:style>
        </w:styles>
        """;
}
