using System.Text;
using System.Text.RegularExpressions;

namespace Gadfly.Engine;

/// <summary>
/// Language-aware text heuristics (GAD-T537): word counts, end-of-answer completeness, "I don't know", hedging, vague wording,
/// reasons, filler removal and rephrase requests, for English, Korean and Spanish.
/// <para>
/// Every Korean rule keys on the script: it only ever fires on Hangul, so text without Hangul (every English answer and
/// question) goes through exactly the English rules it always did, and an English exam is unchanged. Korean code-switching
/// (English technical terms inside a Korean answer) is the normal case, so a Korean answer still gets the English rules for
/// its English words.
/// </para>
/// <para>
/// Spanish (GAD-T610) has no script of its own, so every Spanish rule keys on the text looking Spanish (<see cref="LooksSpanish"/>:
/// an accented vowel, ñ, ¿ or ¡, or two Spanish function words that are not English words), and its word lists are Spanish words
/// that never end or make up an English answer; English text keeps exactly the English rules. A Spanish word counts as one word, as
/// an English one does (Spanish runs a little longer than English for the same content, close enough for the length rules).
/// </para>
/// </summary>
public static partial class LanguageText
{
    /// <summary>A precomposed Hangul syllable (가..힣) or a Hangul jamo.</summary>
    public static bool IsHangul(char c) => c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏';

    public static bool HasHangul(string? text) => !string.IsNullOrEmpty(text) && text.Any(IsHangul);

    private static int HangulSyllables(string token) => token.Count(c => c is >= '가' and <= '힣');

    // ------------------------------------------------------------------ word counts

    /// <summary>
    /// Words in <paramref name="text"/>, in English-word units. Whitespace tokens count one each, as before; a Korean word (an
    /// eojeol: a stem with its particles and endings, so it carries more than an English word) counts one per started 2.5 syllables
    /// beyond the first, so "왜 그렇게 생각하세요?" (3 eojeol) counts 4, like "Why do you think so?". Text without Hangul counts
    /// exactly as <c>Split(whitespace)</c> did.
    /// </summary>
    public static int WordCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var n = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var syllables = HangulSyllables(token);
            n += syllables <= 2 ? 1 : (int)Math.Round(syllables / 2.5, MidpointRounding.AwayFromZero);
        }
        return n;
    }

    // ------------------------------------------------------------------ end of answer

    /// <summary>
    /// Whether a Korean answer visibly stops partway: it ends on a connective ending (-고, -는데, -서, -면, -지만, -니까...) or a
    /// bare conjunction (그리고, 그래서, 하지만, 왜냐하면...). Null when the last word is not Korean (the English rule decides).
    /// A sentence-final ending (-다, -요, -니다, -까, -죠, -네...) is finished; anything else (a noun, a number) is not called
    /// incomplete, the same cautious stance as the English rule.
    /// </summary>
    public static bool? IsIncompleteKorean(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        var last = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1].TrimEnd('"', '\'', '”', '’', ')', ']');
        if (!HasHangul(last)) return null;
        if (t.EndsWith("...", StringComparison.Ordinal) || t.EndsWith('…') || t.EndsWith(',') || t.EndsWith('-')) return true;
        if (last.Length > 0 && last[^1] is '.' or '?' or '!') return false;
        return EndsOnConnective(last);
    }

    /// <summary>Whether a Korean word ends in a sentence-final ending (a finished sentence).</summary>
    public static bool EndsFinishedKorean(string? word)
    {
        var w = (word ?? "").Trim().TrimEnd('.', '?', '!', '"', '\'', '”', '’', ')', ']');
        if (w.Length == 0 || !IsHangul(w[^1])) return false;
        if (EndsOnConnective(w)) return false;
        return FinalEndings.Any(e => w.EndsWith(e, StringComparison.Ordinal)) || EndsInFormalQuestion(w);
    }

    private static bool EndsOnConnective(string w)
    {
        if (BareConjunctions.Contains(w)) return true;
        // -니까 is "because", except the formal question ending -ㅂ니까 (합니까, 있습니까, 입니까).
        if (w.EndsWith("니까", StringComparison.Ordinal)) return !EndsInFormalQuestion(w);
        return ConnectiveEndings.Any(e => w.EndsWith(e, StringComparison.Ordinal) && !FinalEndings.Any(f => f.Length > e.Length && w.EndsWith(f, StringComparison.Ordinal)));
    }

    /// <summary>-ㅂ니까 / -습니까: the syllable before 니까 carries the final consonant ㅂ (jongseong index 17), or is 습.</summary>
    private static bool EndsInFormalQuestion(string w)
    {
        if (w.Length < 3 || !w.EndsWith("니까", StringComparison.Ordinal)) return false;
        var before = w[^3];
        return before is >= '가' and <= '힣' && (before - 0xAC00) % 28 == 17;
    }

    private static readonly string[] FinalEndings =
    [
        "다", "요", "죠", "까", "네", "지", "군", "구나", "걸", "래", "야", "어", "아", "니", "자", "라", "세요", "니다", "습니다", "에요", "예요",
    ];

    // Endings, not bare syllables: a noun can end in 서, 면 or 고 (교과서, 표면, 창고), so each is listed with the syllable it follows.
    private static readonly string[] ConnectiveEndings =
    [
        "하고", "되고", "있고", "없고", "이고", "었고", "았고", "였고", "했고", "같고", "않고", "겠고",
        "는데", "은데", "인데", "던데", "는데도",
        "어서", "아서", "해서", "여서", "라서", "되서", "돼서", "와서", "봐서",
        "으면", "하면", "되면", "다면", "라면", "이면", "있으면", "없으면", "면서", "으면서",
        "지만", "므로", "으며", "하며", "이며", "되며", "거나", "든지", "든가", "도록", "려고", "으려고", "다가", "자마자",
        "는지", "은지", "을지", "어도", "아도", "해도", "라도",
        "때문에", "위해", "위해서", "통해", "통해서", "인해", "또는", "혹은", "및",
    ];

    private static readonly HashSet<string> BareConjunctions = new(StringComparer.Ordinal)
    {
        "그리고", "그래서", "그러나", "하지만", "그런데", "근데", "그러면", "그럼", "그러니까", "왜냐하면", "따라서", "그러므로", "또", "또한", "즉",
    };

    // ------------------------------------------------------------------ I don't know

    /// <summary>A Korean way of saying "I don't know" (모르겠어요, 모르겠습니다, 잘 몰라요, 기억이 안 나요, 모름...).</summary>
    public static bool SaysDontKnowKorean(string? text) => !string.IsNullOrWhiteSpace(text) && KoreanDontKnow().IsMatch(text);

    /// <summary><paramref name="text"/> with its Korean "I don't know" phrases removed (to see whether anything else was said).</summary>
    public static string WithoutKoreanDontKnow(string text) => KoreanDontKnow().Replace(text, " ");

    [GeneratedRegex(@"모르겠|몰라|모릅니다|모르는데|모르겠는데|모름|잘\s*모르|잘\s*몰|기억(이|은)?\s*(안|잘\s*안)\s*나|생각(이|은)?\s*(안|잘\s*안)\s*나|알\s*수\s*(가\s*)?없|답(을|이)?\s*모르|대답(을|하기)?\s*(못|어렵)|아무\s*생각(이|도)?\s*없|전혀\s*모르|감이\s*안\s*(와|오|옵)|모르겠어|몰라요")]
    private static partial Regex KoreanDontKnow();

    // ------------------------------------------------------------------ pressure cues (GAD-T258 lists, Korean)

    /// <summary>Korean hedges (아마, 것 같아요, 확실하지 않지만...): the Korean half of the hedging cue.</summary>
    public static IReadOnlyList<string> KoreanHedges(string? text) => Matches(KoreanHedge(), text);

    /// <summary>Korean vague wording (그런 거, 뭔가, 뭐랄까, 등등...).</summary>
    public static IReadOnlyList<string> KoreanVague(string? text) => Matches(KoreanVaguePhrase(), text);

    /// <summary>A reason or example in Korean (왜냐하면, 때문에, 그래서, 예를 들어, 만약...).</summary>
    public static bool KoreanReasonOrExample(string? text) => !string.IsNullOrWhiteSpace(text) && KoreanReasoning().IsMatch(text);

    private static IReadOnlyList<string> Matches(Regex rx, string? text) => string.IsNullOrWhiteSpace(text) ? []
        : rx.Matches(text).Select(m => Regex.Replace(m.Value, @"\s+", " ").Trim()).Distinct().ToList();

    [GeneratedRegex(@"아마도?|(것|거)\s*같(아요|습니다|은데|아|다)|같은데요?|확실(하지|치)\s*(않|않은데|않지만)|잘은\s*모르지만|글쎄요?|혹시|어쩌면|대충|그런\s*것\s*같|일\s*(것|거)\s*(같|이)|듯(해요|합니다|하다)|제\s*생각(에는|엔)|기억(이|으로는|하기로는)")]
    private static partial Regex KoreanHedge();

    [GeneratedRegex(@"그런\s*(거|것|식)|이런\s*(거|것|식)|저런\s*(거|것)|뭔가|뭐랄까|뭐\s*그런|그거|등등|이것저것|어떻게든")]
    private static partial Regex KoreanVaguePhrase();

    [GeneratedRegex(@"왜냐하면|왜냐면|때문에|때문이|그래서|따라서|그러므로|그러니까|덕분에|결과(적으로|로)|(으)?니까|(으)?므로|예를\s*(들어|들면)|예컨대|가령|만약|만일|라면|이라면|으면|하면|다면|경우(에|에는)|위해(서)?|하려고|이유(는|가)")]
    private static partial Regex KoreanReasoning();

    // ------------------------------------------------------------------ Spanish (GAD-T610)

    /// <summary>
    /// Whether <paramref name="text"/> reads as Spanish: it has ñ, ¿ or ¡; or an accented vowel (á é í ó ú ü) and a Spanish function
    /// word that is not also an English word (que, los, las, del, por, para, una, pero, porque, muy, como, cuando...); or two different
    /// such words. English text, even with a loanword such as "café" or "résumé", has none of these.
    /// </summary>
    public static bool LooksSpanish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.IndexOfAny(SpanishMarks) >= 0) return true;
        var needed = text.IndexOfAny(SpanishVowels) >= 0 ? 1 : 2;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WordToken().Matches(text.ToLowerInvariant()))
            if (SpanishFunctionWords.Contains(m.Value) && seen.Add(m.Value) && seen.Count >= needed) return true;
        return false;
    }

    private static readonly char[] SpanishMarks = ['ñ', 'Ñ', '¿', '¡'];
    private static readonly char[] SpanishVowels = ['á', 'é', 'í', 'ó', 'ú', 'ü', 'Á', 'É', 'Í', 'Ó', 'Ú', 'Ü'];

    // Spanish words that are not English words (so "a", "no", "me", "he", "con", "sin" and the like are left out).
    private static readonly HashSet<string> SpanishFunctionWords = new(StringComparer.Ordinal)
    {
        "que", "los", "las", "del", "por", "para", "una", "unos", "unas", "pero", "porque", "muy", "como", "cuando", "donde", "entonces",
        "es", "son", "esta", "este", "esto", "eso", "ese", "esa", "hay", "tiene", "pues", "sino", "aunque", "el", "la", "lo", "se",
        "su", "sus", "le", "les", "sobre", "entre", "hasta", "desde", "cual", "quien", "mismo", "y", "sé", "está", "también", "qué",
        "cómo", "más", "sí", "creo", "tal", "vez",
    };

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordToken();

    /// <summary>
    /// Whether a Spanish answer visibly stops partway: it trails off ("...", "…", a comma, a dash) or ends on a conjunction or a word
    /// that cannot end a sentence (y, pero, porque, que, entonces, o sea, este, de, la, con...). Null when the text does not look
    /// Spanish or its last word is not one of those (the English rule decides, as before).
    /// </summary>
    public static bool? IsIncompleteSpanish(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        var words = WordToken().Matches(t.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count == 0) return null;
        var pair = words.Count >= 2 ? words[^2] + " " + words[^1] : "";
        // Ending on a Spanish-only connective ("pero", "porque", "o sea") marks Spanish by itself; "y", "de", "la" also end English
        // text ("x and y"), so they count only in text that otherwise looks Spanish.
        var spanishEnding = char.IsLetter(t[^1]) && (SpanishOnlyDangling.Contains(words[^1]) || SpanishDanglingPairs.Contains(pair));
        if (!spanishEnding && !LooksSpanish(t)) return null;
        if (t.EndsWith("...", StringComparison.Ordinal) || t.EndsWith('…') || t.EndsWith(',') || t.EndsWith('-') || t.EndsWith('—') || t.EndsWith('–')) return true;
        if (t[^1] is '.' or '?' or '!' or '"' or '”' or ')') return false;
        if (SpanishDanglingPairs.Contains(pair)) return true;
        return SpanishDangling.Contains(words[^1]) || SpanishOnlyDangling.Contains(words[^1]) ? true : null;
    }

    // Connectives that are not English words, so ending on one is Spanish by itself.
    private static readonly HashSet<string> SpanishOnlyDangling = new(StringComparer.Ordinal)
    {
        "pero", "porque", "entonces", "pues", "sino", "aunque", "mientras", "cuando", "donde", "que", "del", "para", "una", "este",
        "esto", "osea", "además", "también", "los", "las", "sus", "cual", "desde", "hasta", "entre", "sobre", "luego", "como",
    };

    private static readonly HashSet<string> SpanishDangling = new(StringComparer.Ordinal)
    {
        "y", "e", "o", "u", "ni", "pero", "porque", "que", "entonces", "pues", "sino", "aunque", "mientras", "cuando", "si", "como",
        "este", "esto", "osea", "de", "del", "la", "el", "los", "las", "un", "una", "en", "con", "por", "para", "al", "sobre", "entre",
        "hasta", "desde", "sin", "su", "sus", "mi", "es", "son", "era", "fue", "donde", "cual", "también", "además", "luego",
    };

    private static readonly HashSet<string> SpanishDanglingPairs = new(StringComparer.Ordinal)
    {
        "o sea", "ya que", "es decir", "así que", "por eso", "por lo", "lo que", "es que", "dado que", "puesto que",
    };

    /// <summary>A Spanish way of saying "I don't know" (no lo sé, no sé, no me acuerdo, ni idea, no tengo idea, no lo recuerdo...).</summary>
    public static bool SaysDontKnowSpanish(string? text) => !string.IsNullOrWhiteSpace(text) && SpanishDontKnow().IsMatch(text);

    /// <summary><paramref name="text"/> with its Spanish "I don't know" phrases removed (to see whether anything else was said).</summary>
    public static string WithoutSpanishDontKnow(string text) => SpanishDontKnow().Replace(text, " ");

    // "no se" without its accent counts only where the phrase ends ("no se puede" is "it can't be", not "I don't know").
    [GeneratedRegex(@"\bno\s+(lo\s+|la\s+|eso\s+)?sé(?!\p{L})|\bno\s+(lo\s+)?se(?=\s*([.,;!?…]|$))|\bno\s+(me\s+)?acuerdo\b|\bno\s+(lo\s+|la\s+)?recuerdo\b|\bni\s+idea\b|\bno\s+tengo\s+(ni\s+)?(idea|la\s+menor\s+idea)\b|\bno\s+sabr[íi]a\s+(decir|responder|contestar|qué\s+decir)(?!\p{L})|\bdesconozco\b|\bno\s+se\s+(la\s+respuesta|qué\s+decir|responder)(?!\p{L})|\bno\s+puedo\s+(responder|contestar)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishDontKnow();

    /// <summary>Spanish hedges (creo que, tal vez, quizás, a lo mejor, no estoy seguro...): the Spanish half of the hedging cue.</summary>
    public static IReadOnlyList<string> SpanishHedges(string? text) => LooksSpanish(text) ? Matches(SpanishHedge(), text!.ToLowerInvariant()) : [];

    /// <summary>Spanish vague wording (cosas, algo así, y eso, de alguna manera, etcétera...).</summary>
    public static IReadOnlyList<string> SpanishVague(string? text) => LooksSpanish(text) ? Matches(SpanishVaguePhrase(), text!.ToLowerInvariant()) : [];

    /// <summary>A reason or example in Spanish (porque, ya que, por eso, por lo tanto, por ejemplo, si...).</summary>
    public static bool SpanishReasonOrExample(string? text) => LooksSpanish(text) && SpanishReasoning().IsMatch(text!);

    [GeneratedRegex(@"\bcreo\s+que\b|\bme\s+parece\s+que\b|\btal\s+vez\b|\bquiz[áa]s?(?!\p{L})|\ba\s+lo\s+mejor\b|\bprobablemente\b|\bposiblemente\b|\bsupongo(\s+que)?\b|\bno\s+estoy\s+segur[oa]\b|\bno\s+s[eé]\s+si\b|\bpuede\s+(ser\s+)?que\b|\bm[áa]s\s+o\s+menos\b|\bsi\s+no\s+me\s+equivoco\b|\bdir[íi]a\s+que\b|\bpienso\s+que\b|\bcomo\s+que\b")]
    private static partial Regex SpanishHedge();

    [GeneratedRegex(@"\bcosas(\s+así)?(?!\p{L})|\balgo\s+así(?!\p{L})|\by\s+eso\b|\by\s+todo\s+eso\b|\bo\s+algo(\s+así)?(?!\p{L})|\bde\s+alguna\s+(manera|forma)\b|\betc[ée]tera\b|\blo\s+que\s+sea\b|\bese\s+tipo\s+de\b")]
    private static partial Regex SpanishVaguePhrase();

    [GeneratedRegex(@"\bporque\b|\bya\s+que\b|\bdebido\s+a\b|\bpor\s+eso\b|\bpor\s+lo\s+tanto\b|\bentonces\b|\bas[íi]\s+que\b|\bpor\s+ejemplo\b|\bsi\b|\ben\s+caso\s+de\b|\bpara\s+que\b|\bgracias\s+a\b|\bdado\s+que\b|\bpuesto\s+que\b|\bla\s+raz[óo]n(?!\p{L})|\bde\s+modo\s+que\b|\bse\s+debe\s+a\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpanishReasoning();

    // ------------------------------------------------------------------ rephrase requests

    /// <summary>
    /// The student asked to have the question said another way or repeated, in Korean ("다시 말씀해 주세요", "다른 말로", "무슨 뜻이에요?",
    /// "이해가 안 돼요"), Spanish ("¿puede repetir la pregunta?", "dígalo de otra manera", "no entiendo la pregunta", "¿qué quiere
    /// decir?", GAD-T610) or English ("can you say that another way", "what do you mean", "can you repeat the question").
    /// </summary>
    public static bool AsksToRephrase(string? text) => !string.IsNullOrWhiteSpace(text) && Rephrase().IsMatch(text);

    [GeneratedRegex(@"다시\s*(한\s*번\s*)?(말씀|말해|설명|물어|질문)|다른\s*말로|쉽게\s*(말씀|말해|설명)|무슨\s*(뜻|말|의미)|질문(이|을)?\s*(이해|잘)|이해가?\s*(안|잘\s*안)\s*(돼|되|가)|못\s*알아\s*듣|\b(puede|podr[íi]a)\s+(repetir|reformular|decir(lo|la)?\s+(de\s+)?otra)|\brep[íi]ta(lo|la|me)?\b|\b(de|en)\s+otras?\s+(manera|forma|palabras)\b|\bno\s+entend[íi]\s+la\s+pregunta\b|\bno\s+entiendo\s+(la\s+pregunta|lo\s+que\s+(me\s+)?pregunta)|\bqu[ée]\s+quiere\s+decir\b|\ba\s+qu[ée]\s+se\s+refiere\b|\bm[áa]s\s+despacio\b|\b(say|put|ask)\s+(it|that|the\s+question)\s+(another|a\s+different|differently|in\s+other)\b|\b(rephrase|reword)\s+(the|that|your)\s+question\b|\bwhat\s+do\s+you\s+mean\s+by\s+(the|that|your)\s+question\b|\b(repeat|say)\s+(the|that)\s+question(\s+again)?\b|\bi\s+don'?t\s+understand\s+(the|your)\s+question\b", RegexOptions.IgnoreCase)]
    private static partial Regex Rephrase();

    // ------------------------------------------------------------------ the judges' content view

    /// <summary>
    /// The judges' content view of an answer (GAD-T555, research note esl-and-korean R5.2): fillers ("um", "uh", "er", "음", "어", and
    /// in Spanish text "eh", "em"),
    /// immediate repetitions of a word ("the the") and abandoned false starts ("pho- photosynthesis") removed by a fixed rule, never by
    /// a model rewrite (a rewrite can add or remove content). Nothing else changes: no word is added, reordered or corrected. The
    /// verbatim transcript stays the record. Applied only for a student whose primary language differs from the exam language.
    /// </summary>
    public static string ContentView(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        var kept = new List<string>();
        var spanish = LooksSpanish(text);   // Spanish fillers only in Spanish text (GAD-T610), so English is unchanged
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = raw.Trim(',', '.', '…', ';', ':').ToLowerInvariant();
            if (Fillers.Contains(bare) || spanish && SpanishFillers.Contains(bare))
            {
                // A filler that ended a sentence hands its full stop to the word before it.
                if (kept.Count > 0 && raw.EndsWith('.') && !kept[^1].EndsWith('.')) kept[^1] = kept[^1].TrimEnd(',') + ".";
                continue;
            }
            // A false start: a cut-off word ("pho-") followed by a word it begins.
            if (kept.Count > 0 && kept[^1].EndsWith('-') && kept[^1].Length > 1
                && raw.StartsWith(kept[^1][..^1], StringComparison.OrdinalIgnoreCase))
            {
                kept[^1] = raw;
                continue;
            }
            // An immediate repetition ("the the", "그 그"), the same word said twice with no punctuation between.
            if (kept.Count > 0 && string.Equals(kept[^1], raw, StringComparison.OrdinalIgnoreCase) && Normalize(raw).Length > 0) continue;
            kept.Add(raw);
        }
        var view = string.Join(" ", kept);
        return view.Length == 0 ? text.Trim() : view;
    }

    private static string Normalize(string word)
    {
        var sb = new StringBuilder();
        foreach (var c in word) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "um", "umm", "ummm", "uh", "uhh", "uhm", "er", "erm", "ehm", "hmm", "hm", "mm", "mmm",
        "음", "음음", "으음", "어", "어어", "엄", "흠", "에", "에에", "아아",
    };

    /// <summary>Spanish fillers, removed only in Spanish text (GAD-T610). "este" is left: it is also the word "this".</summary>
    private static readonly HashSet<string> SpanishFillers = new(StringComparer.Ordinal) { "eh", "ehh", "ehhh", "em", "emm", "emmm" };
}
