using FluentAssertions;
using GameRagKit.VectorStores;

namespace GameRagKit.Tests.Storage;

public sealed class InMemoryVectorStoreTests
{
    [Fact]
    public async Task SearchAsync_Ranks_By_Cosine_Similarity()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            new RagRecord("a", "world", "north", new[] { 1f, 0f }),
            new RagRecord("b", "world", "diagonal", new[] { 1f, 1f }),
            new RagRecord("c", "world", "east", new[] { 0f, 1f })
        });

        var hits = await store.SearchAsync(new[] { 1f, 0.1f }, topK: 2);

        hits.Select(h => h.Key).Should().Equal("a", "b");
        hits[0].Score.Should().BeGreaterThan(hits[1].Score!.Value);
    }

    [Fact]
    public async Task SearchAsync_Applies_Collection_And_Tag_Filters()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            new RagRecord("a", "npc:mira", "mira", new[] { 1f, 0f }, new Dictionary<string, string> { ["region"] = "riverside" }),
            new RagRecord("b", "npc:mira", "mira-elsewhere", new[] { 1f, 0f }, new Dictionary<string, string> { ["region"] = "hills" }),
            new RagRecord("c", "npc:bram", "bram", new[] { 1f, 0f }, new Dictionary<string, string> { ["region"] = "riverside" })
        });

        var hits = await store.SearchAsync(
            new[] { 1f, 0f },
            topK: 5,
            new Dictionary<string, string> { ["collection"] = "npc:mira", ["region"] = "riverside" });

        hits.Select(h => h.Key).Should().Equal("a");
    }

    [Fact]
    public async Task UpsertAsync_Replaces_Records_With_The_Same_Key()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[] { new RagRecord("a", "world", "old", new[] { 1f, 0f }) });
        await store.UpsertAsync(new[] { new RagRecord("a", "world", "new", new[] { 1f, 0f }) });

        var hits = await store.SearchAsync(new[] { 1f, 0f }, topK: 5);

        hits.Should().ContainSingle().Which.Text.Should().Be("new");
    }
}
