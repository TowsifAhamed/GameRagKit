using System.Collections.Immutable;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameRagKit.Config;

public sealed class NpcConfig
{
    public PersonaConfig Persona { get; init; } = new();
    public RagConfig Rag { get; init; } = new();
    public ProvidersConfig Providers { get; init; } = new();

    public static NpcConfig LoadFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<NpcConfig>(yaml)
               ?? throw new InvalidOperationException("Unable to parse NPC configuration.");
    }
}

public sealed record PersonaConfig
{
    public string Id { get; init; } = string.Empty;
    public string? SystemPrompt { get; init; }
        = null;
    public List<string> Traits { get; init; } = new();
    public string? Style { get; init; }
        = null;
    public string? RegionId { get; init; }
        = null;
    public string? FactionId { get; init; }
        = null;
    public string? WorldId { get; init; }
        = null;

    /// <summary>
    /// A dev-declared, arbitrary-depth hierarchy above this NPC (e.g.
    /// [{name: continent, id: aros}, {name: kingdom, id: eldoria}, {name: guild, id: city-guard}]),
    /// outermost tier first. When set, this replaces world_id/region_id/faction_id entirely --
    /// use one or the other, not both. When unset, TierPath below falls back to the fixed
    /// world/region/faction shape built from those three fields for backward compatibility.
    /// </summary>
    public List<PersonaTier>? Tiers { get; init; }
        = null;

    public double? DefaultImportance { get; init; }
        = null;
    public List<ActionDefinition> Actions { get; init; } = new();
    public bool MoodTracking { get; init; } = false;

    /// <summary>
    /// The effective tier hierarchy for this NPC, outermost first: either the explicit
    /// Tiers list if the dev declared one, or the fixed world/region/faction fields
    /// expanded into the same shape (skipping any that are unset) for anything that
    /// still uses the original 3-tier fields. This is what IndexScopeKey, Retriever, and
    /// PersonaInheritanceResolver actually walk -- they don't know about world/region/
    /// faction as special names, just this ordered list.
    /// </summary>
    [YamlIgnore]
    public IReadOnlyList<PersonaTier> TierPath
    {
        get
        {
            if (Tiers is { Count: > 0 })
            {
                return Tiers;
            }

            var tiers = new List<PersonaTier>();
            if (!string.IsNullOrWhiteSpace(WorldId))
            {
                tiers.Add(new PersonaTier("world", WorldId));
            }

            if (!string.IsNullOrWhiteSpace(RegionId))
            {
                tiers.Add(new PersonaTier("region", RegionId));
            }

            if (!string.IsNullOrWhiteSpace(FactionId))
            {
                tiers.Add(new PersonaTier("faction", FactionId));
            }

            return tiers;
        }
    }
}

/// <summary>
/// One level of a dev-declared persona/lore hierarchy, e.g. {name: faction, id: city-guard}.
/// Name is a free-form label (folder name under configDirectory and metadata/scope key
/// prefix); Id is the specific instance of that tier this NPC belongs to.
/// </summary>
public sealed record PersonaTier
{
    public string Name { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;

    public PersonaTier() { }

    public PersonaTier(string name, string id)
    {
        Name = name;
        Id = id;
    }
}

public sealed record RagConfig
{
    public List<SourceConfig> Sources { get; init; } = new();

    public int ChunkSize { get; init; } = 450;
    public int Overlap { get; init; } = 60;
    public int TopK { get; init; } = 4;
    public string? Reranker { get; init; }
        = null;
    public IDictionary<string, string>? Filters { get; init; }
        = new Dictionary<string, string>();
}

public sealed record SourceConfig
{
    public string File { get; init; } = string.Empty;
    public string? Tier { get; init; }
        = null;
    public IDictionary<string, string>? Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed record ProvidersConfig
{
    public RoutingConfig Routing { get; init; } = new();
    public LocalProviderConfig? Local { get; init; }
        = new();
    public CloudProviderConfig? Cloud { get; init; }
        = new();
    public VoiceConfig? Voice { get; init; }
        = null;
}

public sealed record RoutingConfig
{
    public string Mode { get; init; } = "hybrid"; // local_only | cloud_only | hybrid
    public string Strategy { get; init; } = "importance_weighted";
    public double DefaultImportance { get; init; } = 0.2;
    public bool CloudFallbackOnMiss { get; init; } = true;
}

public sealed record LocalProviderConfig
{
    public string Engine { get; init; } = "ollama"; // ollama | llamasharp
    public string? ChatModel { get; init; }
        = null;
    public string? EmbedModel { get; init; }
        = null;
    public string? Endpoint { get; init; }
        = null;
    public string? ModelPath { get; init; }
        = null;
    public string? EmbedModelPath { get; init; }
        = null;
    public int ContextSize { get; init; } = 4096;
    public int EmbeddingContextSize { get; init; } = 1024;
    public int GpuLayerCount { get; init; } = 0;
    public int? Threads { get; init; }
        = null;
    public int? BatchThreads { get; init; }
        = null;
    public uint BatchSize { get; init; } = 512;
    public uint MicroBatchSize { get; init; } = 512;
    public int MaxTokens { get; init; } = 256;
}

public sealed record CloudProviderConfig
{
    // Supported providers: openai, azure, gemini, groq, openrouter, mistral
    // Note: anthropic/claude and cohere are NOT supported (different API formats)
    public string Provider { get; init; } = "openai";
    public string? ChatModel { get; init; }
        = null;
    public string? EmbedModel { get; init; }
        = null;
    public string? Endpoint { get; init; }
        = null;
}
