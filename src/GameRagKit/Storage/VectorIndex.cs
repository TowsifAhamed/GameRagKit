using System.Collections.Concurrent;
using System.Text.Json;

namespace GameRagKit.Storage;

public sealed class VectorIndex
{
    private readonly ConcurrentDictionary<string, VectorChunk> _chunks = new();

    public IReadOnlyCollection<VectorChunk> Chunks => _chunks.Values;

    public void Upsert(VectorChunk chunk)
    {
        _chunks[chunk.Id] = chunk;
    }

    public void RemoveBySource(string sourcePath)
    {
        var keysToRemove = _chunks.Where(kvp => string.Equals(kvp.Value.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToArray();
        foreach (var key in keysToRemove)
        {
            _chunks.TryRemove(key, out _);
        }
    }

    public bool ContainsSource(string sourcePath)
    {
        return _chunks.Values.Any(chunk => string.Equals(chunk.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<VectorChunk> Search(float[] query, int topK)
    {
        if (_chunks.Count == 0 || topK <= 0)
        {
            return Array.Empty<VectorChunk>();
        }

        var scored = new List<VectorChunk>(_chunks.Count);
        foreach (var chunk in _chunks.Values)
        {
            var score = CosineSimilarity(query, chunk.Embedding);
            scored.Add(chunk with { Score = score });
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToArray();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(_chunks.Values, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static VectorIndex FromJson(string json)
    {
        var index = new VectorIndex();
        if (string.IsNullOrWhiteSpace(json))
        {
            return index;
        }

        var chunks = JsonSerializer.Deserialize<VectorChunk[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? Array.Empty<VectorChunk>();

        foreach (var chunk in chunks)
        {
            index.Upsert(chunk);
        }

        return index;
    }

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var length = Math.Min(a.Count, b.Count);
        if (length == 0)
        {
            return 0d;
        }

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < length; i++)
        {
            var av = a[i];
            var bv = b[i];
            dot += av * bv;
            magA += av * av;
            magB += bv * bv;
        }

        if (magA <= 0 || magB <= 0)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
