using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Npgsql;
using NpgsqlTypes;

namespace GameRagKit.VectorStores;

public sealed class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _tableName;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public PgVectorStore(string connectionString, string tableName = "rag_chunks")
    {
        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        _tableName = tableName;
    }

    public async Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default)
    {
        var recordList = records as IList<RagRecord> ?? records.ToList();
        if (recordList.Count == 0)
        {
            return;
        }

        var dimension = recordList[0].Embedding.Length;
        if (dimension <= 0)
        {
            throw new InvalidOperationException("Embeddings must contain at least one value.");
        }

        for (var i = 1; i < recordList.Count; i++)
        {
            if (recordList[i].Embedding.Length != dimension)
            {
                throw new InvalidOperationException("All embeddings must have the same dimensionality.");
            }
        }

        await EnsureSchemaAsync(dimension, allowCreate: true, ct).ConfigureAwait(false);

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
            command.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(record.TagsOrEmpty));
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
        var schemaReady = await EnsureSchemaAsync(dimension: null, allowCreate: false, ct).ConfigureAwait(false);
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
            foreach (var (key, value) in filters)
            {
                if (string.Equals(key, "collection", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("collection = @collection");
                    command.Parameters.AddWithValue("collection", value);
                }
                else
                {
                    var paramName = $"tag_{command.Parameters.Count}";
                    conditions.Add($"tags ->> '{key}' = @{paramName}");
                    command.Parameters.AddWithValue(paramName, value);
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

    private async Task<bool> EnsureSchemaAsync(int? dimension, bool allowCreate, CancellationToken ct)
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

                if (dimension is null or <= 0)
                {
                    throw new InvalidOperationException("Cannot create the vector table without a known embedding dimension.");
                }

                await CreateSchemaAsync(connection, dimension.Value, ct).ConfigureAwait(false);
            }
            else
            {
                if (dimension is not null)
                {
                    var existingDimension = await GetExistingDimensionAsync(connection, ct).ConfigureAwait(false);
                    if (existingDimension.HasValue && existingDimension.Value != dimension.Value)
                    {
                        throw new InvalidOperationException($"The existing vector table uses dimension {existingDimension.Value}, which does not match the embedding dimension {dimension.Value}. Regenerate the table or align your embeddings.");
                    }
                }

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
        command.CommandText = "SELECT to_regclass(@tableName)";
        command.Parameters.AddWithValue("tableName", _tableName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private async Task<int?> GetExistingDimensionAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT NULLIF(atttypmod, -1)
            FROM pg_attribute
            WHERE attrelid = to_regclass(@tableName)
              AND attname = 'embedding';
        ";
        command.Parameters.AddWithValue("tableName", _tableName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is int typmod && typmod > 0)
        {
            var dimension = typmod - 4;
            return dimension > 0 ? dimension : null;
        }

        return null;
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

    private string BuildIdentifier(string suffix)
    {
        var sanitized = _tableName.Replace('"', '_').Replace('.', '_');
        return $"{sanitized}_{suffix}";
    }
}
