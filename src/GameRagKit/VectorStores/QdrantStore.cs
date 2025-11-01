using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameRagKit.VectorStores;

public sealed class QdrantStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _collection;

    public QdrantStore(HttpClient httpClient, string collection)
    {
        _httpClient = httpClient;
        _collection = collection;
    }

    public async Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default)
    {
        var payload = new
        {
            points = records.Select(record => new
            {
                id = record.Key,
                vector = record.Embedding,
                payload = BuildPayload(record)
            }).ToArray()
        };

        if (payload.points.Length == 0)
        {
            return;
        }

        using var response = await _httpClient.PostAsJsonAsync($"/collections/{_collection}/points?wait=true", payload, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RagHit>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? filters = null,
        CancellationToken ct = default)
    {
        var request = new Dictionary<string, object>
        {
            ["vector"] = query.ToArray(),
            ["limit"] = topK,
            ["with_payload"] = true
        };

        if (filters != null && filters.Count > 0)
        {
            request["filter"] = new
            {
                must = filters.Select(pair => new
                {
                    key = BuildFilterPath(pair.Key),
                    @match = new { value = pair.Value }
                }).ToArray()
            };
        }

        using var response = await _httpClient.PostAsJsonAsync($"/collections/{_collection}/points/search", request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);

        if (!document.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RagHit>();
        }

        var hits = new List<RagHit>();
        foreach (var element in resultArray.EnumerateArray())
        {
            var payload = element.GetProperty("payload");
            var text = payload.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
            var score = element.TryGetProperty("score", out var scoreNode) ? scoreNode.GetDouble() : (double?)null;
            var key = Guid.NewGuid().ToString();
            if (element.TryGetProperty("id", out var idNode))
            {
                key = idNode.ValueKind switch
                {
                    JsonValueKind.String => idNode.GetString() ?? key,
                    JsonValueKind.Number when idNode.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
                    _ => idNode.GetRawText()
                };
            }

            IReadOnlyDictionary<string, string> tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (payload.TryGetProperty("tags", out var tagsNode) && tagsNode.ValueKind == JsonValueKind.Object)
            {
                tags = tagsNode.EnumerateObject()
                    .Where(property => property.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            }

            hits.Add(new RagHit(key, text, score, tags));
        }

        return hits;
    }

    private static Dictionary<string, object> BuildPayload(RagRecord record)
    {
        var payload = new Dictionary<string, object>
        {
            ["collection"] = record.Collection,
            ["text"] = record.Text,
            ["tags"] = record.TagsOrEmpty
        };

        return payload;
    }

    private static string BuildFilterPath(string key)
    {
        if (string.Equals(key, "collection", StringComparison.OrdinalIgnoreCase))
        {
            return "collection";
        }

        return $"tags.{key}";
    }
}
