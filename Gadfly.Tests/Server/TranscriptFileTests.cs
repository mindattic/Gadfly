using System.IO.Compression;
using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Transcripts;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// What the transcript's PDF and Word files actually say (GAD-T295, under GAD-T399). TranscriptTests checks the document model
/// and that both files are well-formed; these read the files back: the questions and answers, the verification code on every
/// PDF page with its page number, the copy's limits carried into the file, long transcripts paging, letters outside the PDF's
/// fonts, and markup in a title. Pure: no database.
/// </summary>
[TestFixture]
public sealed class TranscriptFileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid();

    private const string NoteSummary = "Eyes moved left to right for 6 seconds.";

    private static TranscriptSource Source(string title = "Cell biology oral", int turns = 2, string? student = null, string answerTail = "")
    {
        var session = new ExamSession
        {
            OrganizationId = Org, ExamId = Guid.NewGuid(), StudentUserId = Guid.NewGuid(), Attempt = 1, State = SessionState.Ended,
            EndReason = "Mastery", StartedAt = T0, EndedAt = T0.AddMinutes(30), CreatedAt = T0,
        };
        var list = Enumerable.Range(0, turns).Select(i => new SessionTurn
        {
            OrganizationId = Org, SessionId = session.Id, Index = i, ConceptKey = "osmosis", Move = "Probe",
            Question = $"Question number {i + 1}: why does water cross the membrane?", AskedAt = T0.AddMinutes(i),
            AnswerText = $"Answer number {i + 1}: water follows the concentration gradient.{answerTail}", AnswerMode = "text",
            AnswerStartedAt = T0.AddMinutes(i).AddSeconds(10), AnswerCompletedAt = T0.AddMinutes(i).AddSeconds(40),
        }).ToList();
        var integrity = new IntegrityEvent
        {
            OrganizationId = Org, SessionId = session.Id, Source = "webcam", Type = "offscreen-reading", StartedAt = T0.AddSeconds(20),
            Confidence = 0.4, Severity = IntegritySeverity.Low, Summary = NoteSummary, TurnId = list[0].Id,
        };
        return new TranscriptSource
        {
            Session = session,
            Exam = new Exam { OrganizationId = Org, Title = title, Subject = "Biology", Mode = ExamMode.Voice },
            OrganizationName = "Test College",
            StudentName = student ?? "Ada (ada01)",
            ConceptTitles = new Dictionary<string, string> { ["osmosis"] = "Osmosis" },
            Turns = list,
            IntegrityEvents = [integrity],
        };
    }

    private static (TranscriptDocument Doc, TranscriptStamp Stamp) Compose(TranscriptSource src, TranscriptAudience audience)
    {
        var d = TranscriptComposer.Compose(src, audience);
        return (d, TranscriptHash.Stamp(d, T0.AddHours(1), "https://gadfly.example"));
    }

    // ------------------------------------------------------------------ PDF

    [Test]
    public void The_pdf_shows_every_question_and_answer_and_the_verification_code()
    {
        var (d, stamp) = Compose(Source(), TranscriptAudience.Reviewer);
        var text = PdfText.Squash(PdfText.All(TranscriptPdf.Render(d, stamp)));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(PdfText.Squash("Official exam transcript")));
            Assert.That(text, Does.Contain(PdfText.Squash("Question number 1: why does water cross the membrane?")));
            Assert.That(text, Does.Contain(PdfText.Squash("Answer number 2: water follows the concentration gradient.")));
            Assert.That(text, Does.Contain(stamp.Code));
            Assert.That(text, Does.Contain(stamp.Sha256));
        });
    }

    [Test]
    public void The_pdf_properties_carry_the_title_and_the_code()
    {
        var (d, stamp) = Compose(Source(), TranscriptAudience.Reviewer);
        var info = PdfText.Info(TranscriptPdf.Render(d, stamp));
        Assert.That(info, Does.Contain("/Title (Transcript: Cell biology oral)"));
        Assert.That(info, Does.Contain("Verification code " + stamp.Code));
    }

    [Test]
    public void A_long_transcript_pages_and_every_page_footer_names_the_copy_code_and_page()
    {
        var (d, stamp) = Compose(Source(turns: 40, answerTail: string.Concat(Enumerable.Repeat(" Then the cell swells as water enters by osmosis.", 6))),
            TranscriptAudience.Student);
        var pages = PdfText.Pages(TranscriptPdf.Render(d, stamp));
        Assert.That(pages, Has.Count.GreaterThan(3), "forty long answers fit on three pages");
        for (var p = 0; p < pages.Count; p++)
        {
            Assert.That(pages[p], Does.Contain($"Student copy · verification code {stamp.Code} · page {p + 1} of {pages.Count}"), $"page {p + 1}");
        }
        Assert.That(PdfText.Squash(pages[^1]), Does.Contain(PdfText.Squash("Answer number 40:")));
    }

    [Test]
    public void The_reviewer_pdf_carries_the_reviewer_note_and_the_student_pdf_does_not()
    {
        var src = Source();
        var (rd, rs) = Compose(src, TranscriptAudience.Reviewer);
        var (sd, ss) = Compose(src, TranscriptAudience.Student);
        var reviewer = PdfText.Squash(PdfText.All(TranscriptPdf.Render(rd, rs)));
        var student = PdfText.Squash(PdfText.All(TranscriptPdf.Render(sd, ss)));
        Assert.That(reviewer, Does.Contain(PdfText.Squash(NoteSummary)));
        Assert.That(reviewer, Does.Contain(PdfText.Squash("never a finding of misconduct")));
        Assert.That(student, Does.Not.Contain(PdfText.Squash(NoteSummary)));
        Assert.That(student, Does.Contain(PdfText.Squash("This copy leaves out reviewer notes about the session")));
    }

    [Test]
    public void Letters_outside_the_pdf_fonts_lose_their_accents_readably_while_the_word_file_keeps_them()
    {
        var (d, stamp) = Compose(Source(student: "Łucja Wróbel (Łódź)"), TranscriptAudience.Reviewer);
        var pdf = PdfText.All(TranscriptPdf.Render(d, stamp));
        Assert.That(pdf, Does.Contain("Lucja Wróbel (Lódz)"), "Ł becomes L, ź becomes z, ó (in Windows-1252) stays");
        Assert.That(PdfText.DocxText(TranscriptDocx.Render(d, stamp)), Does.Contain("Łucja Wróbel (Łódź)"));
    }

    [Test]
    public void Brackets_and_backslashes_in_text_are_escaped_so_the_pdf_stays_readable()
    {
        var (d, stamp) = Compose(Source(title: @"Paths (C:\data) and sets {a, b}"), TranscriptAudience.Reviewer);
        var pdf = TranscriptPdf.Render(d, stamp);
        Assert.That(PdfText.All(pdf), Does.Contain(@"Paths (C:\data) and sets {a, b}"));
        Assert.That(Encoding.ASCII.GetString(pdf[^6..]).Trim(), Is.EqualTo("%%EOF"));
    }

    // ------------------------------------------------------------------ Word

    [Test]
    public void The_word_file_shows_the_questions_answers_and_code_and_its_footer_names_the_copy_and_code()
    {
        var (d, stamp) = Compose(Source(), TranscriptAudience.BlindGrader);
        var docx = TranscriptDocx.Render(d, stamp);
        var body = PdfText.DocxText(docx);
        Assert.That(body, Does.Contain("Question number 1: why does water cross the membrane?"));
        Assert.That(body, Does.Contain("Answer number 1: water follows the concentration gradient."));
        Assert.That(body, Does.Contain(stamp.Code));
        Assert.That(body, Does.Not.Contain(NoteSummary), "the blind grader's copy has no integrity notes");
        var footer = PdfText.DocxText(docx, "word/footer1.xml");
        Assert.That(footer, Does.Contain(stamp.Code));
        Assert.That(footer, Does.Contain("Grader copy (blind grading)"));
    }

    [Test]
    public void Markup_in_a_title_is_escaped_and_the_word_file_stays_well_formed()
    {
        const string title = "Salt & <Pepper> \"quoted\"";
        var (d, stamp) = Compose(Source(title: title), TranscriptAudience.Reviewer);
        var docx = TranscriptDocx.Render(d, stamp);
        Assert.That(PdfText.DocxText(docx), Does.Contain(title));   // parsing succeeded, so the XML is well-formed
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("docProps/core.xml")!.Open());
        var core = System.Xml.Linq.XDocument.Parse(reader.ReadToEnd());
        Assert.That(core.ToString(), Does.Contain("Salt &amp; &lt;Pepper&gt;"));
    }

    [Test]
    public void The_same_copy_renders_to_the_same_words_in_both_files()
    {
        var (d, stamp) = Compose(Source(turns: 3), TranscriptAudience.Student);
        var pdf = PdfText.Squash(PdfText.All(TranscriptPdf.Render(d, stamp)));
        var word = PdfText.Squash(PdfText.DocxText(TranscriptDocx.Render(d, stamp)));
        foreach (var block in TranscriptLayout.Blocks(d, stamp).Where(b => b.Kind is BlockKind.Quote or BlockKind.Heading))
        {
            var words = PdfText.Squash(block.PlainText);
            Assert.That(pdf, Does.Contain(words), "PDF: " + block.PlainText);
            Assert.That(word, Does.Contain(words), "Word: " + block.PlainText);
        }
    }
}
