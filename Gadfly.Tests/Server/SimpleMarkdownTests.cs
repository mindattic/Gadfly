using Gadfly.Server.Review;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The session report's Markdown renderer: the subset the report uses, and every piece of text HTML-encoded first.</summary>
[TestFixture]
public class SimpleMarkdownTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \n  ")]
    public void Blank_RendersNothing(string? markdown) => Assert.That(SimpleMarkdown.ToHtml(markdown), Is.EqualTo(""));

    [Test]
    public void Headings_StepDownOneLevel_BecauseThePageOwnsH1()
    {
        Assert.That(SimpleMarkdown.ToHtml("# Report"), Is.EqualTo("<h2>Report</h2>"));
        Assert.That(SimpleMarkdown.ToHtml("### Objective"), Is.EqualTo("<h4>Objective</h4>"));
        Assert.That(SimpleMarkdown.ToHtml("##### Deep"), Is.EqualTo("<h6>Deep</h6>"));
    }

    [Test]
    public void ConsecutiveLines_JoinIntoOneParagraph_BlankLinesSplitThem()
    {
        Assert.That(SimpleMarkdown.ToHtml("one\ntwo\n\nthree"), Is.EqualTo("<p>one<br />two</p><p>three</p>"));
    }

    [Test]
    public void WindowsLineEndings_AreHandled()
    {
        Assert.That(SimpleMarkdown.ToHtml("one\r\n\r\ntwo"), Is.EqualTo("<p>one</p><p>two</p>"));
    }

    [Test]
    public void Bullets_BecomeAList()
    {
        Assert.That(SimpleMarkdown.ToHtml("- first\n* second\nafter"), Is.EqualTo("<ul><li>first</li><li>second</li></ul><p>after</p>"));
    }

    [Test]
    public void PipeTable_DropsTheSeparatorRow()
    {
        var html = SimpleMarkdown.ToHtml("| Objective | Score |\n|---|:---:|\n| Factors | 8 |");
        Assert.That(html, Is.EqualTo("<table class=\"grid\"><tr><td>Objective</td><td>Score</td></tr><tr><td>Factors</td><td>8</td></tr></table>"));
    }

    [Test]
    public void Blockquote_AndComments()
    {
        Assert.That(SimpleMarkdown.ToHtml("> quoted"), Is.EqualTo("<blockquote>quoted</blockquote>"));
        Assert.That(SimpleMarkdown.ToHtml("<!-- hidden -->\nshown"), Is.EqualTo("<p>shown</p>"));
    }

    [Test]
    public void InlineBoldItalicAndCode()
    {
        Assert.That(SimpleMarkdown.ToHtml("**bold** and *italic* and `x < y`"),
            Is.EqualTo("<p><strong>bold</strong> and <em>italic</em> and <code>x &lt; y</code></p>"));
    }

    [Test]
    public void AsterisksInsideWords_AreNotItalic()
    {
        Assert.That(SimpleMarkdown.ToHtml("2*3*4"), Is.EqualTo("<p>2*3*4</p>"));
    }

    [Test]
    public void StudentText_CannotInjectMarkup()
    {
        var html = SimpleMarkdown.ToHtml("> <script>alert('x')</script>\n\n| <img src=x onerror=alert(1)> |");
        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Not.Contain("<img"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
    }
}
