using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using QdrantValue = Qdrant.Client.Grpc.Value;

namespace GameRagKit.VectorStores;

public sealed class QdrantStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly string _collection;
    private readonly int _dims;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public QdrantStore(QdrantClient client, string collection, int embeddingDims)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new ArgumentException("A Qdrant collection name is required.", nameof(collection));
        }

        if (embeddingDims <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddingDims), embeddingDims, "Embedding dimensions must be positive.");
        }

        _client = client;
        _collection = collection;
        _dims = embeddingDims;
    }

    public async Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(createIfMissing: true, ct).ConfigureAwait(false);

        var pointList = new List<PointStruct>();
        foreach (var record in records)
        {
            if (record.Embedding is not { Length: > 0 } embedding)
            {
                throw new InvalidOperationException("Embeddings must contain at least one value.");
            }

            if (embedding.Length != _dims)
            {
                throw new InvalidOperationException($"All embeddings must have {_dims} dimensions when writing to {_collection}.");
            }

            pointList.Add(CreatePoint(record));
        }

        var points = pointList.ToArray();
        if (points.Length == 0)
        {
            return;
        }

        await _client.UpsertAsync(_collection, points, wait: true, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RagHit>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? filters = null,
        CancellationToken ct = default)
    {
        if (query.Length != _dims)
        {
            throw new InvalidOperationException($"Expected query embedding with {_dims} dimensions but received {query.Length}.");
        }

        if (topK <= 0)
        {
            return Array.Empty<RagHit>();
        }

        var ready = await EnsureCollectionAsync(createIfMissing: false, ct).ConfigureAwait(false);
        if (!ready)
        {
            return Array.Empty<RagHit>();
        }

        Filter? filter = null;
        if (filters != null && filters.Count > 0)
        {
            filter = new Filter();
            foreach (var condition in filters.Select(BuildCondition))
            {
                filter.Must.Add(condition);
            }
        }

        var results = await _client.SearchAsync(
                _collection,
                query,
                filter: filter,
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                cancellationToken: ct)
            .ConfigureAwait(false);

        var hits = new List<RagHit>(results.Count);
        foreach (var point in results)
        {
            var payload = point.Payload;
            var text = payload.TryGetValue("text", out var textValue) ? textValue.StringValue : string.Empty;
            var tags = payload.Count > 0
                ? ExtractTags(payload)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var key = ExtractId(point.Id);
            var scoreValue = (double)point.Score;
            var score = double.IsNaN(scoreValue) ? (double?)null : scoreValue;
            hits.Add(new RagHit(key, text, score, tags));
        }

        return hits;
    }

    public async ValueTask DisposeAsync()
    {
        await _initializationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _client.Dispose();
        }
        finally
        {
            _initializationLock.Dispose();
        }
    }

    private async Task<bool> EnsureCollectionAsync(bool createIfMissing, CancellationToken ct)
    {
        if (_initialized)
        {
            return true;
        }

        await _initializationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return true;
            }

            var exists = await _client.CollectionExistsAsync(_collection, ct).ConfigureAwait(false);
            if (!exists)
            {
                if (!createIfMissing)
                {
                    return false;
                }

                var vectorParams = new VectorParams
                {
                    Size = (ulong)_dims,
                    Distance = Distance.Cosine
                };

                await _client.CreateCollectionAsync(
                        _collection,
                        vectorParams,
                        cancellationToken: ct)
                    .ConfigureAwait(false);

                _initialized = true;
                return true;
            }

            await EnsureDimensionsAsync(ct).ConfigureAwait(false);
            _initialized = true;
            return true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task EnsureDimensionsAsync(CancellationToken ct)
    {
        var info = await _client.GetCollectionInfoAsync(_collection, ct).ConfigureAwait(false);
        var configuredSize = info.Config?.Params?.VectorsConfig?.Params?.Size;
        if (!configuredSize.HasValue)
        {
            throw new InvalidOperationException($"Collection {_collection} does not expose a primary vector size.");
        }

        if ((int)configuredSize.Value != _dims)
        {
            throw new InvalidOperationException(
                $"Collection {_collection} is configured for {configuredSize.Value} dimensions but {_dims} were requested.");
        }
    }

    private PointStruct CreatePoint(RagRecord record)
    {
        var point = new PointStruct
        {
            Id = CreatePointId(record.Key),
            Vectors = new Vectors
            {
                Vector = new Vector
                {
                    Data = { record.Embedding }
                }
            }
        };

        point.Payload.Add("collection", new QdrantValue { StringValue = record.Collection });
        point.Payload.Add("text", new QdrantValue { StringValue = record.Text });

        if (record.Tags != null)
        {
            foreach (var (key, value) in record.Tags)
            {
                point.Payload[$"tag_{key}"] = new QdrantValue { StringValue = value };
            }
        }

        return point;
    }

    private static Condition BuildCondition(KeyValuePair<string, string> filter)
    {
        var field = string.Equals(filter.Key, "collection", StringComparison.OrdinalIgnoreCase)
            ? "collection"
            : $"tag_{filter.Key}";
        return Conditions.MatchKeyword(field, filter.Value);
    }

    private static Dictionary<string, string> ExtractTags(MapField<string, QdrantValue> payload)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in payload)
        {
            if (pair.Key.StartsWith("tag_", StringComparison.OrdinalIgnoreCase))
            {
                var key = pair.Key.Substring(4);
                tags[key] = pair.Value.StringValue;
            }
        }

        return tags;
    }

    private static string ExtractId(PointId? id)
    {
        if (id == null)
        {
            return string.Empty;
        }

        if (id.HasUuid)
        {
            return id.Uuid;
        }

        if (id.HasNum)
        {
            return id.Num.ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static PointId CreatePointId(string key)
    {
        if (Guid.TryParse(key, out var guid))
        {
            return guid;
        }

        if (ulong.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
        {
            return num;
        }

        return new PointId { Uuid = key };
    }
}
