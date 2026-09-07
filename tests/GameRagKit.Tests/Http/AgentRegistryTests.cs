using System.Reflection;
using FluentAssertions;
using GameRagKit;
using GameRagKit.Config;
using GameRagKit.Http;
using GameRagKit.Pipeline;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;
using GameRagKit.VectorStores;

namespace GameRagKit.Tests.Http;

public sealed class AgentRegistryTests
{
    // NpcAgent's constructor is internal and this class has no public factory that avoids
    // real I/O (GameRAGKit.Load reads a file from disk), so tests construct minimal
    // instances via reflection -- this exercises the real constructor/type, not a fake.
    private static NpcAgent CreateAgent(string personaId, TrackingVectorStore? store = null)
    {
        var config = new NpcConfig { Persona = new PersonaConfig { Id = personaId, SystemPrompt = "test" } };
        var chunker = new TextChunker();
        var storageRoot = Path.Combine(Path.GetTempPath(), "gamerag-registrytest-" + Guid.NewGuid().ToString("N"));
        var manifestRepo = new VectorIndexRepository(storageRoot);
        var moodRepo = new MoodRepository(storageRoot);
        var vectorStore = store ?? new TrackingVectorStore();
        var router = new Router(new ProviderResolver());

        var ctor = typeof(NpcAgent).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return (NpcAgent)ctor.Invoke(new object[] { config, "/tmp", chunker, manifestRepo, moodRepo, vectorStore, router });
    }

    [Fact]
    public void TryGetAgent_Finds_Agent_By_Registered_Key()
    {
        var agent = CreateAgent("guard-north-gate");
        var registry = new AgentRegistry(new[] { new KeyValuePair<string, NpcAgent>("guard-north-gate", agent) });

        registry.TryGetAgent("guard-north-gate", out var found).Should().BeTrue();
        found.Should().BeSameAs(agent);
    }

    [Fact]
    public async Task ReplaceAgentAsync_Updates_All_Aliases_Pointing_At_Same_Npc()
    {
        var oldAgent = CreateAgent("guard-north-gate");
        var registry = new AgentRegistry(new[]
        {
            new KeyValuePair<string, NpcAgent>("guard-north-gate", oldAgent),
            new KeyValuePair<string, NpcAgent>("guard-north-gate-file-alias", oldAgent)
        });

        var newAgent = CreateAgent("guard-north-gate");
        await registry.ReplaceAgentAsync(newAgent);

        registry.TryGetAgent("guard-north-gate", out var byPersonaId).Should().BeTrue();
        registry.TryGetAgent("guard-north-gate-file-alias", out var byFileAlias).Should().BeTrue();
        byPersonaId.Should().BeSameAs(newAgent);
        byFileAlias.Should().BeSameAs(newAgent);
    }

    [Fact]
    public async Task ReplaceAgentAsync_Does_Not_Affect_Other_Npcs()
    {
        var guardAgent = CreateAgent("guard-north-gate");
        var merchantAgent = CreateAgent("merchant");
        var registry = new AgentRegistry(new[]
        {
            new KeyValuePair<string, NpcAgent>("guard-north-gate", guardAgent),
            new KeyValuePair<string, NpcAgent>("merchant", merchantAgent)
        });

        var newGuardAgent = CreateAgent("guard-north-gate");
        await registry.ReplaceAgentAsync(newGuardAgent);

        registry.TryGetAgent("merchant", out var stillMerchant).Should().BeTrue();
        stillMerchant.Should().BeSameAs(merchantAgent);
    }

    [Fact]
    public async Task ReplaceAgentAsync_Does_Not_Dispose_Old_Agent_Immediately()
    {
        var store = new TrackingVectorStore();
        var oldAgent = CreateAgent("guard-north-gate", store);
        var registry = new AgentRegistry(new[] { new KeyValuePair<string, NpcAgent>("guard-north-gate", oldAgent) });

        var newAgent = CreateAgent("guard-north-gate");
        await registry.ReplaceAgentAsync(newAgent);

        // Old agent's vector store must not be disposed synchronously -- an in-flight
        // request that already grabbed a reference to the old agent must be able to keep
        // using it immediately after the swap.
        store.Disposed.Should().BeFalse();
    }

    private sealed class TrackingVectorStore : IVectorStore, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RagHit>> SearchAsync(ReadOnlyMemory<float> query, int topK, IReadOnlyDictionary<string, string>? filters = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RagHit>>(Array.Empty<RagHit>());

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
