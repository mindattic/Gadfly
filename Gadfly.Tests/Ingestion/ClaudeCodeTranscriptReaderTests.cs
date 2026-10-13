using Gadfly.Ingestion;
using NUnit.Framework;

namespace Gadfly.Tests.Ingestion;

[TestFixture]
public class ClaudeCodeTranscriptReaderTests
{
    private string tempFile = "";

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }

    private string WriteLines(params string[] lines)
    {
        tempFile = Path.GetTempFileName();
        File.WriteAllLines(tempFile, lines);
        return tempFile;
    }

    [Test]
    public void ReadFile_ExtractsHumanTypedMessage()
    {
        var path = WriteLines(
            """{"type":"user","message":{"role":"user","content":"hello there"},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1","cwd":"D:/x","origin":{"kind":"human"},"promptSource":"typed"}""");

        var messages = ClaudeCodeTranscriptReader.ReadFile(path).ToList();

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Role, Is.EqualTo(CorpusRole.Human));
        Assert.That(messages[0].Text, Is.EqualTo("hello there"));
        Assert.That(messages[0].SessionId, Is.EqualTo("s1"));
        Assert.That(messages[0].ProjectPath, Is.EqualTo("D:/x"));
    }

    [Test]
    public void ReadFile_SkipsToolResultArrayContent()
    {
        var path = WriteLines(
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"ok"}]},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1","origin":{"kind":"human"}}""");

        Assert.That(ClaudeCodeTranscriptReader.ReadFile(path).ToList(), Is.Empty);
    }

    [Test]
    public void ReadFile_ExtractsOnlyTextBlocksFromAssistantMessage()
    {
        var path = WriteLines(
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"hmm"},{"type":"tool_use","name":"Read"},{"type":"text","text":"here is my answer"}]},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1"}""");

        var messages = ClaudeCodeTranscriptReader.ReadFile(path).ToList();

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Role, Is.EqualTo(CorpusRole.Assistant));
        Assert.That(messages[0].Text, Is.EqualTo("here is my answer"));
    }

    [Test]
    public void ReadFile_SkipsUnknownLineTypes()
    {
        var path = WriteLines("""{"type":"file-history-snapshot","data":{}}""");

        Assert.That(ClaudeCodeTranscriptReader.ReadFile(path).ToList(), Is.Empty);
    }

    [Test]
    public void ReadFile_SkipsUserMessageThatIsNeitherHumanOriginNorTypedPromptSource()
    {
        var path = WriteLines(
            """{"type":"user","message":{"role":"user","content":"a synthetic followup"},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1"}""");

        Assert.That(ClaudeCodeTranscriptReader.ReadFile(path).ToList(), Is.Empty);
    }

    [Test]
    public void ReadFile_TreatsTypedPromptSourceAsHumanEvenWithoutOrigin()
    {
        var path = WriteLines(
            """{"type":"user","message":{"role":"user","content":"typed but no origin field"},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1","promptSource":"typed"}""");

        Assert.That(ClaudeCodeTranscriptReader.ReadFile(path).ToList(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ReadFile_TolerateMalformedTrailingLine()
    {
        tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile,
            """{"type":"user","message":{"role":"user","content":"fine"},"timestamp":"2026-01-01T00:00:00.000Z","sessionId":"s1","promptSource":"typed"}""" +
            "\n{\"type\":\"user\",\"message\":{\"content\":\"truncated mid-wr");

        var messages = ClaudeCodeTranscriptReader.ReadFile(tempFile).ToList();

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Text, Is.EqualTo("fine"));
    }
}
