using System.Text.RegularExpressions;

namespace Gadfly.Sources;

/// <summary>A retrieved passage and its fused rank score.</summary>
public sealed record RetrievedPassage(SourcePassage Passage, double Score);

/// <summary>
/// Hybrid retrieval over a collection's passages: BM25 over words, plus cosine similarity over
/// embeddings when both the passages and an <see cref="IEmbeddingProvider"/> have them, fused by
/// reciprocal rank (RRF). Used by the question generator (grounding for a concept) and the judge
/// (the passages a student's answer touches). Build once per collection; queries are in memory.
/// </summary>
public sealed class GroundingRetriever
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int RrfK = 60;

    private static readonly Regex Word = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the","a","an","and","or","of","to","in","is","are","was","were","be","been","it","its","that","this","these","those",
        "for","on","with","as","by","at","from","but","if","then","so","than","into","about","which","what","who","how","why",
    };

    private readonly IReadOnlyList<SourcePassage> _passages;
    private readonly List<Dictionary<string, int>> _termFreqs;
    private readonly int[] _lengths;
    private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);
    private readonly double _avgLength;
    private readonly IEmbeddingProvider? _embeddings;

    public GroundingRetriever(IReadOnlyList<SourcePassage> passages, IEmbeddingProvider? embeddings = null)
    {
        _passages = passages;
        _embeddings = embeddings;
        _termFreqs = new List<Dictionary<string, int>>(passages.Count);
        _lengths = new int[passages.Count];
        for (var i = 0; i < passages.Count; i++)
        {
            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            var n = 0;
            foreach (var t in Tokenize(passages[i].Text))
            {
                tf[t] = tf.GetValueOrDefault(t) + 1;
                n++;
            }
            _termFreqs.Add(tf);
            _lengths[i] = n;
            foreach (var t in tf.Keys) _docFreq[t] = _docFreq.GetValueOrDefault(t) + 1;
        }
        _avgLength = passages.Count == 0 ? 0 : _lengths.Average();
    }

    public int Count => _passages.Count;

    /// <summary>The top <paramref name="k"/> passages for <paramref name="query"/>.</summary>
    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, int k = 6, CancellationToken ct = default)
    {
        if (_passages.Count == 0 || string.IsNullOrWhiteSpace(query) || k <= 0) return [];

        var lexical = Bm25(query);
        var rankings = new List<IReadOnlyList<int>> { lexical };

        if (_embeddings is not null && _passages.Any(p => p.Embedding is not null))
        {
            var q = (await _embeddings.EmbedAsync([query], ct))[0];
            var qn = Math.Sqrt(q.Sum(x => (double)x * x));
            if (qn > 0)
            {
                var semantic = Enumerable.Range(0, _passages.Count)
                    .Where(i => _passages[i].Embedding is { } e && e.Length == q.Length)
                    .Select(i => (i, Sim: Dot(_passages[i].Embedding!, q) / qn))
                    .OrderByDescending(x => x.Sim)
                    .Take(Math.Max(k * 4, 20))
                    .Select(x => x.i)
                    .ToList();
                rankings.Add(semantic);
            }
        }

        var fused = new Dictionary<int, double>();
        foreach (var ranking in rankings)
            for (var r = 0; r < ranking.Count; r++)
                fused[ranking[r]] = fused.GetValueOrDefault(ranking[r]) + 1.0 / (RrfK + r + 1);

        return fused.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(k)
            .Select(x => new RetrievedPassage(_passages[x.Key], x.Value)).ToList();
    }

    /// <summary>Passages by id, in the given order; unknown ids are skipped.</summary>
    public IReadOnlyList<SourcePassage> ById(IEnumerable<string> ids)
    {
        var map = _passages.ToDictionary(p => p.Id, StringComparer.Ordinal);
        return ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
    }

    private List<int> Bm25(string query)
    {
        var terms = Tokenize(query).Distinct().ToList();
        var n = _passages.Count;
        var scores = new List<(int Index, double Score)>();
        for (var i = 0; i < n; i++)
        {
            double s = 0;
            var tf = _termFreqs[i];
            foreach (var t in terms)
            {
                if (!tf.TryGetValue(t, out var f)) continue;
                var df = _docFreq[t];
                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                s += idf * f * (K1 + 1) / (f + K1 * (1 - B + B * _lengths[i] / Math.Max(1, _avgLength)));
            }
            if (s > 0) scores.Add((i, s));
        }
        return scores.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(50).Select(x => x.Index).ToList();
    }

    internal static IEnumerable<string> Tokenize(string text) =>
        Word.Matches(text.ToLowerInvariant()).Select(m => Stem(m.Value)).Where(w => w.Length > 1 && !Stop.Contains(w));

    /// <summary>A light suffix stripper so "cells"/"cell" and "dividing"/"divide" match.</summary>
    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) return w[..^3];
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 4 && w.EndsWith("es", StringComparison.Ordinal) && !w.EndsWith("ses", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) return w[..^2];
        return w;
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
