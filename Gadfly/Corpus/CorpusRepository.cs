using System.Security.Cryptography;
using System.Text;
using Gadfly.Ingestion;

namespace Gadfly.Corpus;

/// <summary>
/// Read-side queries over the corpus, plus a stable hash of the current
/// message-id set used to cache-key <see cref="Gadfly.Mining.CorpusSummarizer"/>'s digest.
/// </summary>
public class CorpusRepository
{
    private readonly CorpusStore store;

    public CorpusRepository(CorpusStore store) => this.store = store;

    public IReadOnlyList<CorpusMessage> GetAll() => store.ReadAll().OrderBy(m => m.TimestampUtc).ToList();

    public IReadOnlyList<CorpusMessage> GetHumanMessages() =>
        GetAll().Where(m => m.Role == CorpusRole.Human).ToList();

    public IReadOnlyList<CorpusMessage> GetSince(DateTimeOffset since) =>
        GetAll().Where(m => m.TimestampUtc >= since).ToList();

    /// <summary>
    /// Stable hash over the sorted message-id set — changes if and only if the corpus's
    /// membership changes, independent of read order. Used as the digest-cache key so
    /// repeat <c>interrogate</c>/<c>reflect</c>/<c>examine</c> runs on an unchanged corpus
    /// skip re-summarizing.
    /// </summary>
    public string CorpusHash()
    {
        var ids = GetAll().Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var id in ids) sb.Append(id).Append('\n');
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }
}
