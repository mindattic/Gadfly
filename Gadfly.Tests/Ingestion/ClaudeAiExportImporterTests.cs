using Gadfly.Ingestion;
using NUnit.Framework;

namespace Gadfly.Tests.Ingestion;

[TestFixture]
public class ClaudeAiExportImporterTests
{
    [Test]
    public void ParseConversations_ExtractsHumanAndAssistantMessages()
    {
        var json = """
        [
          {
            "uuid": "conv-1",
            "created_at": "2026-01-01T00:00:00Z",
            "chat_messages": [
              { "sender": "human", "text": "what do you think about X?", "created_at": "2026-01-01T00:00:01Z" },
              { "sender": "assistant", "text": "I think...", "created_at": "2026-01-01T00:00:02Z" }
            ]
          }
        ]
        """;

        var messages = ClaudeAiExportImporter.ParseConversations(json);

        Assert.That(messages, Has.Count.EqualTo(2));
        Assert.That(messages[0].Role, Is.EqualTo(CorpusRole.Human));
        Assert.That(messages[0].Source, Is.EqualTo(CorpusSource.ClaudeAiExport));
        Assert.That(messages[1].Role, Is.EqualTo(CorpusRole.Assistant));
    }

    [Test]
    public void ParseConversations_ExtractsTextFromContentBlockArray()
    {
        var json = """
        [
          {
            "uuid": "conv-1",
            "messages": [
              { "role": "user", "content": [{ "type": "text", "text": "hello" }] }
            ]
          }
        ]
        """;

        var messages = ClaudeAiExportImporter.ParseConversations(json);

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].Text, Is.EqualTo("hello"));
    }

    [Test]
    public void ParseConversations_ThrowsExportSchemaException_WhenNotAnArray()
    {
        Assert.Throws<ExportSchemaException>(() => ClaudeAiExportImporter.ParseConversations("""{"not":"an array"}"""));
    }

    [Test]
    public void ParseConversations_ThrowsExportSchemaException_WhenConversationsLackMessagesArray()
    {
        var json = """[{"uuid":"conv-1","title":"no messages field at all"}]""";

        Assert.Throws<ExportSchemaException>(() => ClaudeAiExportImporter.ParseConversations(json));
    }

    [Test]
    public void ParseConversations_ThrowsExportSchemaException_OnInvalidJson()
    {
        Assert.Throws<ExportSchemaException>(() => ClaudeAiExportImporter.ParseConversations("not json at all"));
    }

    [Test]
    public void ParseConversations_ReturnsEmpty_WhenArrayIsEmpty()
    {
        Assert.That(ClaudeAiExportImporter.ParseConversations("[]"), Is.Empty);
    }
}
