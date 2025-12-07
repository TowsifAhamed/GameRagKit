using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace GameRagKit.VectorStores;

public sealed class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;
    private readonly int _dims;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public PgVectorStore(string connectionString, int embeddingDims, string tableName = "rag_chunks")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        if (embeddingDims <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddingDims), embeddingDims, "Embedding dimensions must be positive.");
        }

        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        _tableName = tableName;
        _dims = embeddingDims;
    }

    public async Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default)
    {
        var recordList = records as IList<RagRecord> ?? records.ToList();
        if (recordList.Count == 0)
        {
            return;
        }

        for (var i = 0; i < recordList.Count; i++)
        {
            if (recordList[i].Embedding is not { Length: > 0 } embedding)
            {
                throw new InvalidOperationException("Embeddings must contain at least one value.");
            }

            if (embedding.Length != _dims)
            {
                throw new InvalidOperationException("All embeddings must have the same dimensionality.");
            }
        }

        await EnsureSchemaAsync(allowCreate: true, ct).ConfigureAwait(false);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var record in recordList)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT INTO {_tableName} (key, collection, tags, text, embedding)
                VALUES (@key, @collection, @tags, @text, CAST(@embedding AS vector))
                ON CONFLICT (key) DO UPDATE
                    SET collection = EXCLUDED.collection,
                        tags = EXCLUDED.tags,
                        text = EXCLUDED.text,
                        embedding = EXCLUDED.embedding;
                ";

            command.Parameters.AddWithValue("key", record.Key);
            command.Parameters.AddWithValue("collection", record.Collection);
            command.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(record.Tags ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
            command.Parameters.AddWithValue("text", record.Text);
            command.Parameters.AddWithValue("embedding", FormatVectorLiteral(record.Embedding));

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
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

        var schemaReady = await EnsureSchemaAsync(allowCreate: false, ct).ConfigureAwait(false);
        if (!schemaReady)
        {
            return Array.Empty<RagHit>();
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var conditions = new List<string>();
        var formattedQuery = FormatVectorLiteral(query.Span);
        command.Parameters.AddWithValue("embedding", formattedQuery);
        command.Parameters.AddWithValue("topK", topK);

        if (filters != null)
        {
            var tagIndex = 0;
            foreach (var (key, value) in filters)
            {
                if (string.Equals(key, "collection", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("collection = @collection");
                    command.Parameters.AddWithValue("collection", value);
                }
                else
                {
                    var paramName = $"tag_{tagIndex++}";
                    conditions.Add($"tags @> @{paramName}");
                    var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [key] = value
                    };
                    command.Parameters.AddWithValue(paramName, NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload));
                }
            }
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        command.CommandText = $@"
            SELECT key, text, tags, 1 - (embedding <=> CAST(@embedding AS vector)) AS score
            FROM {_tableName}
            {whereClause}
            ORDER BY embedding <-> CAST(@embedding AS vector)
            LIMIT @topK;
            ";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<RagHit>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var text = reader.GetString(1);
            var tagsJson = reader.GetString(2);
            var score = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3);
            var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            results.Add(new RagHit(key, text, score, tags));
        }

        return results;
    }

    private async Task<bool> EnsureSchemaAsync(bool allowCreate, CancellationToken ct)
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

            await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

            var tableExists = await TableExistsAsync(connection, ct).ConfigureAwait(false);
            if (!tableExists)
            {
                if (!allowCreate)
                {
                    return false;
                }

                await CreateSchemaAsync(connection, _dims, ct).ConfigureAwait(false);
            }
            else
            {
                await EnsureEmbeddingDimensionsAsync(connection, ct).ConfigureAwait(false);
                await EnsureIndexesAsync(connection, ct).ConfigureAwait(false);
            }

            _initialized = true;
            return true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static string FormatVectorLiteral(ReadOnlySpan<float> values)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[i].ToString("G9", CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
        _initializationLock.Dispose();
    }

    private async Task<bool> TableExistsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@tableName)::text";
        command.Parameters.AddWithValue("tableName", _tableName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private async Task CreateSchemaAsync(NpgsqlConnection connection, int dimension, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var collectionIndex = BuildIdentifier("collection_idx");
        var embeddingIndex = BuildIdentifier("embedding_hnsw");
        command.CommandText = $@"
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE EXTENSION IF NOT EXISTS pgcrypto;
            CREATE TABLE IF NOT EXISTS {_tableName} (
                key UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                collection TEXT NOT NULL,
                tags JSONB DEFAULT '{{}}'::jsonb,
                text TEXT NOT NULL,
                embedding VECTOR({dimension}) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS {collectionIndex} ON {_tableName}(collection);
            CREATE INDEX IF NOT EXISTS {embeddingIndex} ON {_tableName} USING HNSW (embedding vector_l2_ops);
        ";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureIndexesAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var collectionIndex = BuildIdentifier("collection_idx");
        var embeddingIndex = BuildIdentifier("embedding_hnsw");
        command.CommandText = $@"
            CREATE INDEX IF NOT EXISTS {collectionIndex} ON {_tableName}(collection);
            CREATE INDEX IF NOT EXISTS {embeddingIndex} ON {_tableName} USING HNSW (embedding vector_l2_ops);
        ";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureEmbeddingDimensionsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT atttypmod
            FROM pg_attribute
            WHERE attrelid = @tableName::regclass
              AND attname = 'embedding';
        ";
        command.Parameters.AddWithValue("tableName", _tableName);

        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException($"Embedding column not found on table {_tableName}.");
        }

        var typeModifier = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        var existingDims = typeModifier - 4; // pgvector stores size as typmod = 4 + dimensions
        if (existingDims != _dims)
        {
            throw new InvalidOperationException($"Existing embedding dimension {existingDims} does not match configured dimension {_dims} for table {_tableName}.");
        }
    }

    private string BuildIdentifier(string suffix)
    {
        var sanitized = _tableName.Replace('"', '_').Replace('.', '_');
        return $"{sanitized}_{suffix}";
    }
}
