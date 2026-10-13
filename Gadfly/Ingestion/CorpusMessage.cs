namespace Gadfly.Ingestion;

/// <summary>Which ingestion surface a <see cref="CorpusMessage"/> came from.</summary>
public enum CorpusSource
{
    ClaudeCodeCli,
    ClaudeAiExport,
}

/// <summary>Who authored a <see cref="CorpusMessage"/>.</summary>
public enum CorpusRole
{
    Human,
    Assistant,
}

/// <summary>
/// A single normalized message from either ingestion source, ready to be
/// appended to the corpus. <see cref="Id"/> is stable and source-derived so
/// re-ingesting the same transcript or export is idempotent (see
/// <see cref="Gadfly.Corpus.CorpusStore.AppendNew"/>).
/// </summary>
public class CorpusMessage
{
    public string Id { get; init; } = "";
    public CorpusSource Source { get; init; }
    public CorpusRole Role { get; init; }
    public string SessionId { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Working directory the message was authored in (Claude Code CLI only). Null for claude.ai export messages.</summary>
    public string? ProjectPath { get; init; }
}
