using FluentAssertions;
using GameRagKit.Config;
using GameRagKit.Pipeline;
using GameRagKit.VectorStores;

namespace GameRagKit.Tests.Pipeline;

public sealed class RetrieverTests
{
    private sealed class RecordingVectorStore : IVectorStore
    {
        public List<(string Collection, int Limit)> Calls { get; } = new();

        public Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RagHit>> SearchAsync(ReadOnlyMemory<float> query, int topK, IReadOnlyDictionary<string, string>? filters = null, CancellationToken ct = default)
        {
            var collection = filters != null && filters.TryGetValue("collection", out var value) ? value : string.Empty;
            Calls.Add((collection, topK));
            return Task.FromResult<IReadOnlyList<RagHit>>(Array.Empty<RagHit>());
        }
    }

    [Fact]
    public async Task RetrieveAsync_Queries_A_Scope_Per_Declared_Tier_Regardless_Of_Tier_Names()
    {
        var persona = new PersonaConfig
        {
            Id = "shop-clerk",
            Tiers = new List<PersonaTier>
            {
                new("continent", "aros"),
                new("kingdom", "eldoria"),
                new("guild", "merchants-guild")
            }
        };
        var store = new RecordingVectorStore();
        var retriever = new Retriever(store, persona, filters: null);

        await retriever.RetrieveAsync(new float[] { 0.1f }, topK: 4, CancellationToken.None);

        var collections = store.Calls.Select(c => c.Collection).ToList();
        collections.Should().Contain("continent:aros");
        collections.Should().Contain("kingdom:eldoria");
        collections.Should().Contain("guild:merchants-guild");
        collections.Should().Contain("npc:shop-clerk");
        collections.Should().Contain("memory:shop-clerk");
    }

    [Fact]
    public async Task RetrieveAsync_With_No_Declared_Tiers_Only_Queries_Npc_And_Memory_Scopes()
    {
        var persona = new PersonaConfig { Id = "solo-npc" };
        var store = new RecordingVectorStore();
        var retriever = new Retriever(store, persona, filters: null);

        await retriever.RetrieveAsync(new float[] { 0.1f }, topK: 4, CancellationToken.None);

        store.Calls.Select(c => c.Collection).Should().BeEquivalentTo(new[] { "npc:solo-npc", "memory:solo-npc" });
    }

    [Fact]
    public async Task RetrieveAsync_With_Five_Declared_Tiers_Queries_All_Five_Plus_Npc_And_Memory()
    {
        var persona = new PersonaConfig
        {
            Id = "deep-npc",
            Tiers = new List<PersonaTier>
            {
                new("world", "eldoria"),
                new("continent", "aros"),
                new("kingdom", "valeria"),
                new("city", "port-haven"),
                new("guild", "city-guard")
            }
        };
        var store = new RecordingVectorStore();
        var retriever = new Retriever(store, persona, filters: null);

        await retriever.RetrieveAsync(new float[] { 0.1f }, topK: 4, CancellationToken.None);

        store.Calls.Should().HaveCount(7); // 5 tiers + npc + memory
    }
}
