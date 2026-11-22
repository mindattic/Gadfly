using System.Text;

namespace Gadfly.Engine;

/// <summary>
/// Where the examiner's next question is spoken while it is still being written (GAD-T495). The question generator hands it each
/// part of the question only after that part has passed the leak guard, in order; the voice speaks the parts back to back. A part
/// that fails the guard is never handed over: the generator stops there and hands over the plain fallback question instead, so the
/// student never hears a leak. Everything said is exactly the question recorded: the parts, joined with single spaces, are the
/// question <see cref="QuestionGenerator.GenerateAsync"/> returns.
/// </summary>
public interface IQuestionSpeech
{
    /// <summary>One part of the question (one or more whole sentences), guarded and safe to say now. <paramref name="move"/> sets her speaking style.</summary>
    void Say(string part, MoveKind move);

    /// <summary>The question is complete: <paramref name="question"/> is every part said, joined. Called only after at least one <see cref="Say"/>.</summary>
    void Finish(string question);

    /// <summary>The question could not be finished (the step failed or was cancelled after something was said).</summary>
    void Abandon(string reason);
}

/// <summary>
/// The ambient <see cref="IQuestionSpeech"/> for the question being written right now (GAD-T495). The exam room sets it around the one
/// step whose next question should be spoken as it is written (a spoken answer on a live exam); everything else (the CLI, reflection
/// runs, a speculative prefetch on a background task, tests) has none and gets the whole question at once, as before.
/// </summary>
public static class QuestionSpeech
{
    private static readonly AsyncLocal<IQuestionSpeech?> Ambient = new();

    /// <summary>The speech for the question being generated, or null to return the whole question at once.</summary>
    public static IQuestionSpeech? Current => Ambient.Value;

    /// <summary>Makes <paramref name="speech"/> current until the result is disposed.</summary>
    public static IDisposable Begin(IQuestionSpeech? speech)
    {
        var previous = Ambient.Value;
        Ambient.Value = speech;
        return new Scope(previous);
    }

    private sealed class Scope(IQuestionSpeech? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>
/// Splits text arriving piece by piece into whole sentences (GAD-T495). A sentence ends at '.', '?' or '!' (or a run of them), any
/// closing quotes or brackets after it, and then whitespace; a full stop after a common abbreviation ("e.g.", "Dr.") or a single
/// letter (an initial) does not end one, nor does one inside a number ("3.5"). <see cref="Flush"/> returns whatever is left at the end.
/// </summary>
public sealed class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g", "i.e", "etc", "vs", "cf", "dr", "mr", "mrs", "ms", "prof", "st", "approx", "no", "fig", "eq", "al", "jr", "sr",
    };

    private readonly StringBuilder _buffer = new();

    /// <summary>Adds <paramref name="piece"/> and returns the sentences it completed, trimmed, in order.</summary>
    public IReadOnlyList<string> Add(string? piece)
    {
        if (string.IsNullOrEmpty(piece)) return [];
        _buffer.Append(piece);
        var done = new List<string>();
        var text = _buffer.ToString();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '?' or '!')) continue;
            var end = i;
            while (end + 1 < text.Length && text[end + 1] is '.' or '?' or '!') end++;
            while (end + 1 < text.Length && text[end + 1] is '"' or '\'' or '”' or '’' or ')' or ']') end++;
            // The end of the text so far is not yet the end of a sentence: the next piece may continue it ("3." then "5").
            if (end + 1 >= text.Length) break;
            if (!char.IsWhiteSpace(text[end + 1]) || (text[i] == '.' && !EndsSentence(text, start, i)))
            {
                i = end;
                continue;
            }
            var sentence = text[start..(end + 1)].Trim();
            if (sentence.Length > 0) done.Add(sentence);
            start = end + 1;
            i = end;
        }
        _buffer.Remove(0, start);
        return done;
    }

    /// <summary>The text not yet returned as a sentence (the last sentence, when the text has ended), trimmed; "" when none.</summary>
    public string Flush()
    {
        var rest = _buffer.ToString().Trim();
        _buffer.Clear();
        return rest;
    }

    /// <summary>Whether the full stop at <paramref name="dot"/> ends a sentence (not an abbreviation or an initial).</summary>
    private static bool EndsSentence(string text, int start, int dot)
    {
        var w = dot;
        while (w > start && !char.IsWhiteSpace(text[w - 1])) w--;
        var word = text[w..dot].TrimStart('(', '"', '\'', '“', '‘');
        if (word.Length == 0) return true;
        if (word.Length == 1 && char.IsLetter(word[0])) return false;
        return !Abbreviations.Contains(word);
    }
}
