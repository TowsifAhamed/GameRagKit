namespace GameRagKit.VectorStores;

public interface IVectorStore
{
    Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default);

    Task<IReadOnlyList<RagHit>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? filters = null,
        CancellationToken ct = default);
}

public sealed record RagRecord(
    string Key,
    string Collection,
    string Text,
    float[] Embedding,
    Dictionary<string, string>? Tags = null);

public sealed record RagHit(
    string Key,
    string Text,
    double? Score,
    Dictionary<string, string> Tags);
