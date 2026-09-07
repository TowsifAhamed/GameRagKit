using FluentAssertions;
using GameRagKit.VectorStores;

namespace GameRagKit.Tests.Storage;

/// <summary>
/// Integration tests against a real Postgres+pgvector instance. Each test no-ops
/// immediately unless PGVECTOR_TEST_CONNECTION_STRING is set, since they need a live
/// database -- e.g.:
///
///   docker run -d --name pgvector-test -e POSTGRES_PASSWORD=test -e POSTGRES_DB=testdb -p 5433:5432 pgvector/pgvector:pg16
///   PGVECTOR_TEST_CONNECTION_STRING="Host=localhost;Port=5433;Username=postgres;Password=test;Database=testdb" dotnet test
///
/// These exist because PgVectorStore had three real bugs (found via this exact manual
/// process against a live Neon deployment) that unit tests alone could not have caught:
/// an incorrect pgvector typmod-to-dimension offset, and the key column being written/
/// read as Npgsql's default `text` type instead of the actual `uuid` column type.
/// </summary>
public sealed class PgVectorStoreTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("PGVECTOR_TEST_CONNECTION_STRING");
    private static bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

    private static string UniqueTableName() => "rag_chunks_test_" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task UpsertAsync_Then_SearchAsync_Round_Trips_A_Record()
    {
        if (!IsAvailable)
        {
            return;
        }

        var tableName = UniqueTableName();
        await using var store = new PgVectorStore(ConnectionString!, 8, tableName);
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };
        var key = Guid.NewGuid().ToString();
        var record = new RagRecord(key, "test-collection", "The guard stands watch.", embedding, new Dictionary<string, string> { ["source"] = "test" });

        await store.UpsertAsync(new[] { record });

        var hits = await store.SearchAsync(embedding, topK: 5, filters: new Dictionary<string, string> { ["collection"] = "test-collection" });

        hits.Should().ContainSingle();
        hits[0].Key.Should().Be(key);
        hits[0].Text.Should().Be("The guard stands watch.");
        hits[0].Score.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public async Task UpsertAsync_Against_Existing_Table_From_A_New_Instance_Succeeds()
    {
        if (!IsAvailable)
        {
            return;
        }

        // Regression test for the pgvector typmod dimension-check bug: the first instance
        // creates the table; a second, independent instance (simulating a server restart)
        // must be able to validate the existing table's dimensions and insert into it
        // without throwing a false "dimension mismatch" error.
        var tableName = UniqueTableName();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };

        await using (var firstInstance = new PgVectorStore(ConnectionString!, 8, tableName))
        {
            var record = new RagRecord(Guid.NewGuid().ToString(), "test-collection", "First.", embedding, null);
            await firstInstance.UpsertAsync(new[] { record });
        }

        await using var secondInstance = new PgVectorStore(ConnectionString!, 8, tableName);
        var secondRecord = new RagRecord(Guid.NewGuid().ToString(), "test-collection", "Second.", embedding, null);

        var act = async () => await secondInstance.UpsertAsync(new[] { secondRecord });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RagHit_Key_Is_A_Valid_Guid_String()
    {
        if (!IsAvailable)
        {
            return;
        }

        var tableName = UniqueTableName();
        await using var store = new PgVectorStore(ConnectionString!, 8, tableName);
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };
        var key = Guid.NewGuid().ToString();
        var record = new RagRecord(key, "test-collection", "Text.", embedding, null);

        await store.UpsertAsync(new[] { record });
        var hits = await store.SearchAsync(embedding, topK: 1, filters: new Dictionary<string, string> { ["collection"] = "test-collection" });

        hits.Should().ContainSingle();
        Guid.TryParse(hits[0].Key, out var parsed).Should().BeTrue();
        parsed.Should().Be(Guid.Parse(key));
    }
}
