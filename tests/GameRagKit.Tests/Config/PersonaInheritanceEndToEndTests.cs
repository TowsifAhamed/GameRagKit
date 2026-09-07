using System.Reflection;
using FluentAssertions;
using GameRagKit.Config;
using GameRagKit.Pipeline;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;
using GameRagKit.VectorStores;

namespace GameRagKit.Tests.Config;

/// <summary>
/// Exercises persona inheritance the way a real game dev's project actually looks: a
/// configDirectory on disk with world/region/faction persona.yaml fragments and an NPC
/// yaml that declares only ids, loaded through the same NpcAgent constructor GameRAGKit.Load
/// uses (via reflection, matching the pattern in AgentRegistryTests -- NpcAgent's
/// constructor is internal and there is no public factory that skips real file I/O).
/// </summary>
public sealed class PersonaInheritanceEndToEndTests : IDisposable
{
    private readonly string _configDirectory;

    public PersonaInheritanceEndToEndTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), "gamerag-persona-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_configDirectory))
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
    }

    private void WriteFragment(string tier, string id, string yaml)
    {
        var dir = Path.Combine(_configDirectory, tier, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "persona.yaml"), yaml);
    }

    private static NpcAgent CreateAgentFromConfigDirectory(NpcConfig config, string configDirectory)
    {
        var chunker = new TextChunker();
        var storageRoot = Path.Combine(Path.GetTempPath(), "gamerag-persona-e2e-storage-" + Guid.NewGuid().ToString("N"));
        var manifestRepo = new VectorIndexRepository(storageRoot);
        var moodRepo = new MoodRepository(storageRoot);
        var vectorStore = new NoOpVectorStore();
        var router = new Router(new ProviderResolver());

        var ctor = typeof(NpcAgent).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return (NpcAgent)ctor.Invoke(new object[] { config, configDirectory, chunker, manifestRepo, moodRepo, vectorStore, router });
    }

    private static ResolvedPersona GetResolvedPersona(NpcAgent agent)
    {
        var field = typeof(NpcAgent).GetField("_resolvedPersona", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("NpcAgent no longer has a _resolvedPersona field.");
        return (ResolvedPersona)field.GetValue(agent)!;
    }

    [Fact]
    public void NpcAgent_Resolves_Persona_From_Real_Fragment_Files_On_Disk_At_Construction_Time()
    {
        WriteFragment("world", "eldoria", """
        system_prompt: This is the world of Eldoria, a land recovering from war.
        """);
        WriteFragment("region", "valeria", """
        system_prompt: Valeria is a coastal region known for its markets.
        """);
        WriteFragment("faction", "city-guard", """
        system_prompt: The City Guard values order and duty above all.
        mood_tracking: true
        """);

        var config = new NpcConfig
        {
            Persona = new PersonaConfig
            {
                Id = "guard-north-gate",
                WorldId = "eldoria",
                RegionId = "valeria",
                FactionId = "city-guard"
            }
        };

        var agent = CreateAgentFromConfigDirectory(config, _configDirectory);
        var resolved = GetResolvedPersona(agent);

        resolved.SystemPrompt.Should().Be(
            "This is the world of Eldoria, a land recovering from war.\n\n" +
            "Valeria is a coastal region known for its markets.\n\n" +
            "The City Guard values order and duty above all.");
        resolved.MoodTracking.Should().BeTrue();
    }

    [Fact]
    public void NpcAgent_With_No_Fragment_Files_On_Disk_Falls_Back_To_Its_Own_Persona_Unaffected()
    {
        var config = new NpcConfig
        {
            Persona = new PersonaConfig
            {
                Id = "solo-npc",
                SystemPrompt = "You are a lone wanderer."
            }
        };

        var agent = CreateAgentFromConfigDirectory(config, _configDirectory);
        var resolved = GetResolvedPersona(agent);

        resolved.SystemPrompt.Should().Be("You are a lone wanderer.");
        resolved.Style.Should().Be("concise");
    }

    private sealed class NoOpVectorStore : IVectorStore
    {
        public Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RagHit>> SearchAsync(ReadOnlyMemory<float> query, int topK, IReadOnlyDictionary<string, string>? filters = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RagHit>>(Array.Empty<RagHit>());
    }
}
