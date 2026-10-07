using System.Collections.Concurrent;

namespace GameRagKit.VectorStores;

/// <summary>
/// Process-local vector store (DB=memory) for local testing without Postgres or Qdrant.
/// Uses brute-force cosine similarity with the same filter semantics as PgVectorStore:
/// a "collection" filter matches RagRecord.Collection, every other key must match a tag.
/// Nothing is persisted, so NpcAgent re-embeds every source on startup when this store is
/// in use rather than trusting the on-disk ingest manifest.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, RagRecord> _records = new(StringComparer.Ordinal);

    public Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            _records[record.Key] = record;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RagHit>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? filters = null,
        CancellationToken ct = default)
    {
        var queryNorm = Norm(query.Span);
        IReadOnlyList<RagHit> hits = _records.Values
            .Where(record => Matches(record, filters))
            .Select(record => new RagHit(
                record.Key,
                record.Text,
                Cosine(query.Span, queryNorm, record.Embedding),
                record.Tags != null
                    ? new Dictionary<string, string>(record.Tags, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(hit => hit.Score)
            .Take(Math.Max(0, topK))
            .ToArray();

        return Task.FromResult(hits);
    }

    private static bool Matches(RagRecord record, IReadOnlyDictionary<string, string>? filters)
    {
        if (filters == null)
        {
            return true;
        }

        foreach (var (key, value) in filters)
        {
            if (string.Equals(key, "collection", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(record.Collection, value, StringComparison.Ordinal))
                {
                    return false;
                }

                continue;
            }

            if (record.Tags == null
                || !record.Tags.TryGetValue(key, out var tagValue)
                || !string.Equals(tagValue, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static double Norm(ReadOnlySpan<float> vector)
    {
        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * (double)v;
        }

        return Math.Sqrt(sum);
    }

    private static double Cosine(ReadOnlySpan<float> query, double queryNorm, float[] candidate)
    {
        var length = Math.Min(query.Length, candidate.Length);
        double dot = 0;
        for (var i = 0; i < length; i++)
        {
            dot += query[i] * (double)candidate[i];
        }

        var denominator = queryNorm * Norm(candidate);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
