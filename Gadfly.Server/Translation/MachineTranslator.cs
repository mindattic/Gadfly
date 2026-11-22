using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Gadfly.Engine;
using Gadfly.Socrates;
using Gadfly.Socrates.Mock;

namespace Gadfly.Server.Translation;

/// <summary>One machine translation, and what made it.</summary>
/// <param name="Mock">True when the deterministic mock made it (no model was called): never a real translation.</param>
public sealed record MachineTranslation(string Text, string From, string To, bool Mock, string Provider, string Model, bool FromCache);

/// <summary>
/// Machine translation for Gadfly's staff (GAD-T540 drafts of an exam's texts, GAD-T548 translations of a transcript for graders).
/// Live, it asks the one fast model the live turn uses (<see cref="ILiveTurnModel"/>, Claude Sonnet 5 by default, never Opus),
/// metered as kind <c>translate</c>; in mock mode it returns a deterministic, plainly labelled mock translation and calls nothing.
/// A translation is never the record: callers keep the original and label the translation as machine translation wherever it is
/// shown. Results are cached in memory by language pair and the text's hash, so a page opened twice, or by two graders, is
/// translated once per process.
/// </summary>
public sealed class MachineTranslator(IPanelInfo panel, ILiveTurnModel model)
{
    /// <summary>The usage kind a translation call is metered as.</summary>
    public const string UsageKind = "translate";

    /// <summary>The longest text translated in one call; longer texts are refused rather than cut.</summary>
    public const int MaxChars = 12_000;

    private const int MaxCached = 10_000;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public bool IsMock => panel.IsMock;

    /// <summary>The label every machine translation carries where a person reads it.</summary>
    public static string Label(string from, string to, bool mock) =>
        $"Machine translation from {ExamLanguage.EnglishName(from)} into {ExamLanguage.EnglishName(to)}{(mock ? " (mock: no model was called)" : "")}. Not the record; the original above is.";

    /// <summary>
    /// Translates <paramref name="text"/> from <paramref name="from"/> into <paramref name="to"/> (BCP-47 tags). The same language,
    /// or blank text, comes back unchanged without a call. Throws when the live model fails.
    /// </summary>
    public async Task<MachineTranslation> TranslateAsync(string text, string from, string to, CancellationToken ct = default)
    {
        from = ExamLanguage.Normalize(from);
        to = ExamLanguage.Normalize(to);
        var mock = panel.IsMock;
        if (string.IsNullOrWhiteSpace(text) || from == to) return new(text, from, to, mock, "", "", true);
        if (text.Length > MaxChars) throw new ArgumentException($"Translate at most {MaxChars} characters at a time.", nameof(text));

        if (mock) return new(MockTranslate(text, to), from, to, true, MockVotingClient.ProviderId, MockVotingClient.ModelName, false);

        var key = Key(text, from, to, model.Model);
        if (Cache.TryGetValue(key, out var cached)) return new(cached, from, to, false, model.ProviderId, model.Model, true);

        string reply;
        using (PanelUsage.UseLiveKind(UsageKind))
            reply = await model.CompleteAsync(SystemPrompt(from, to), UserMessage(text), MaxTokens(text), temperature: 0, ct);
        var translated = Clean(reply);
        if (translated.Length == 0) throw new InvalidOperationException("The translation came back empty.");
        if (Cache.Count >= MaxCached) Cache.Clear();
        Cache[key] = translated;
        return new(translated, from, to, false, model.ProviderId, model.Model, false);
    }

    /// <summary>
    /// The mock translation: the text unchanged behind a marker naming the target language, so a page in mock mode shows exactly
    /// where a translation would go and can never pass it off as one.
    /// </summary>
    public static string MockTranslate(string text, string to) => $"[{ExamLanguage.EnglishName(to)}, mock translation] {text.Trim()}";

    public static string SystemPrompt(string from, string to) =>
        $"""
        You translate text for an examination platform from {ExamLanguage.EnglishName(from)} into {ExamLanguage.EnglishName(to)}.
        Translate faithfully: keep the meaning, register and any errors or hesitations of the original; never correct, improve,
        explain, summarize or answer it. Keep names, numbers, formulas, technical terms and Markdown formatting as they are.
        If part of the text is already in {ExamLanguage.EnglishName(to)}, keep that part unchanged.
        Reply with the translation only: no preamble, no notes, no quotation marks around it.
        """;

    public static string UserMessage(string text) => $"Translate this text:\n<text>\n{text.Trim()}\n</text>";

    private static int MaxTokens(string text) => Math.Clamp(text.Length * 2 + 100, 200, 8000);

    /// <summary>Strips the wrapper a model sometimes echoes back.</summary>
    public static string Clean(string reply)
    {
        var t = (reply ?? "").Trim();
        if (t.StartsWith("<text>", StringComparison.Ordinal)) t = t["<text>".Length..];
        if (t.EndsWith("</text>", StringComparison.Ordinal)) t = t[..^"</text>".Length];
        return t.Trim();
    }

    private static string Key(string text, string from, string to, string modelId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"{from}>{to}|{modelId}|{Convert.ToHexString(hash)}";
    }
}
