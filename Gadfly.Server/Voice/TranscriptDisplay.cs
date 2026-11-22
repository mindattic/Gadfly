using System.Text;
using System.Text.RegularExpressions;

namespace Gadfly.Server.Voice;

/// <summary>
/// Turns a recognizer's raw word stream into something easier to read, for display only (GAD-T360): spelled-out
/// numbers become digits ("twenty six" becomes "26") and spoken punctuation words become marks ("comma" becomes
/// ","), with the result capitalized and given a closing full stop if it has none. Nothing here ever touches what is
/// stored or graded: <see cref="VoiceAnswerBuilder"/> builds the answer from the recognizer's own words, verbatim,
/// before this class is ever called, and a reviewer's transcript keeps those words too. This is purely a kinder shape
/// of the same text for a screen: the student's on-screen transcript and, where a page chooses to use it, a reviewer's.
/// </summary>
public static class TranscriptDisplay
{
    private static readonly Dictionary<string, string> PunctuationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["comma"] = ",", ["period"] = ".", ["full stop"] = ".", ["question mark"] = "?",
        ["exclamation mark"] = "!", ["exclamation point"] = "!", ["colon"] = ":", ["semicolon"] = ";",
    };

    private static readonly Dictionary<string, int> Ones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Dictionary<string, int> Magnitudes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hundred"] = 100, ["thousand"] = 1_000, ["million"] = 1_000_000,
    };

    /// <summary>Longest input accepted; anything longer is returned unchanged rather than walked word by word.</summary>
    public const int MaxLength = 20_000;

    /// <summary>
    /// The display form of <paramref name="text"/>: numbers spelled out in words become digits, spoken punctuation words
    /// become marks, and the result is capitalized with a closing full stop if it lacks one. Blank or oversized input
    /// comes back unchanged (trimmed).
    /// </summary>
    public static string ForDisplay(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength) return trimmed;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var outWords = new List<string>();
        var i = 0;
        while (i < words.Length)
        {
            // Two-word punctuation phrases ("full stop") before single words.
            if (i + 1 < words.Length && PunctuationWords.TryGetValue(words[i] + " " + words[i + 1], out var twoWordMark))
            {
                Append(outWords, twoWordMark);
                i += 2;
                continue;
            }
            if (PunctuationWords.TryGetValue(words[i], out var mark))
            {
                Append(outWords, mark);
                i++;
                continue;
            }
            var (number, consumed) = ReadNumber(words, i);
            if (consumed > 0)
            {
                outWords.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                i += consumed;
                continue;
            }
            outWords.Add(words[i]);
            i++;
        }

        var joined = Join(outWords);
        return Capitalize(EnsureTerminalPunctuation(joined));
    }

    /// <summary>Reads a run of number words starting at <paramref name="start"/>; returns (value, wordsConsumed), consumed 0 if none.</summary>
    private static (int Value, int Consumed) ReadNumber(IReadOnlyList<string> words, int start)
    {
        var i = start;
        var total = 0;
        var current = 0;
        var any = false;
        while (i < words.Count)
        {
            var w = words[i];
            if (string.Equals(w, "and", StringComparison.OrdinalIgnoreCase) && any && i + 1 < words.Count && IsNumberWord(words[i + 1]))
            {
                i++;
                continue;
            }
            if (Ones.TryGetValue(w, out var one))
            {
                current += one;
                any = true;
                i++;
            }
            else if (Tens.TryGetValue(w, out var ten))
            {
                current += ten;
                any = true;
                i++;
            }
            else if (Magnitudes.TryGetValue(w, out var mag))
            {
                current = (current == 0 ? 1 : current) * mag;
                if (mag >= 1000) { total += current; current = 0; }
                any = true;
                i++;
            }
            else break;
        }
        return any ? (total + current, i - start) : (0, 0);
    }

    private static bool IsNumberWord(string w) => Ones.ContainsKey(w) || Tens.ContainsKey(w) || Magnitudes.ContainsKey(w);

    private static void Append(List<string> outWords, string mark)
    {
        if (outWords.Count == 0) { outWords.Add(mark); return; }
        outWords[^1] += mark;   // no space before the mark
    }

    private static string Join(IReadOnlyList<string> words)
    {
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length > 0 && !(w.Length > 0 && IsAttachedMark(w[0]))) sb.Append(' ');
            sb.Append(w);
        }
        return sb.ToString();
    }

    private static bool IsAttachedMark(char c) => c is ',' or '.' or '?' or '!' or ':' or ';';

    private static readonly Regex TerminalPunctuation = new(@"[.?!]\s*$", RegexOptions.Compiled);

    private static string EnsureTerminalPunctuation(string text) =>
        text.Length == 0 || TerminalPunctuation.IsMatch(text) ? text : text + ".";

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
