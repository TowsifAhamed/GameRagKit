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
    public string SystemPrompt { get; init; } = string.Empty;
    public IReadOnlyList<string> Traits { get; init; } = Array.Empty<string>();
    public string? Style { get; init; }
        = "concise";
    public string? RegionId { get; init; }
        = null;
    public string? FactionId { get; init; }
        = null;
    public string? WorldId { get; init; }
        = null;
    public double? DefaultImportance { get; init; }
        = null;
}

public sealed record RagConfig
{
    public IReadOnlyList<SourceConfig> Sources { get; init; } = Array.Empty<SourceConfig>();
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
}

public sealed record CloudProviderConfig
{
    public string Provider { get; init; } = "openai"; // openai | azure | mistral | gemini | hf
    public string? ChatModel { get; init; }
        = null;
    public string? EmbedModel { get; init; }
        = null;
    public string? Endpoint { get; init; }
        = null;
}
