namespace Gadfly.Ingestion;

/// <summary>
/// Thrown when a claude.ai export's <c>conversations.json</c> doesn't match the
/// shape <see cref="ClaudeAiExportImporter"/> expects. Carries a concrete,
/// human-readable diagnosis so the importer can be corrected against a real
/// export quickly, instead of silently ingesting partial or garbage records.
/// </summary>
public class ExportSchemaException : Exception
{
    public ExportSchemaException(string message) : base(message) { }
}
