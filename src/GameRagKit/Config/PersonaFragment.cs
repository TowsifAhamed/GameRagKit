using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GameRagKit.Config;

/// <summary>
/// A persona.yaml file at a world/, region/&lt;id&gt;/, or faction/&lt;id&gt;/ folder root,
/// declaring shared personality context inherited by every NPC scoped under that tier.
/// See PersonaInheritanceResolver for how fragments are combined into an NPC's final
/// system prompt.
/// </summary>
public sealed record PersonaFragment
{
    public string? SystemPrompt { get; init; }
        = null;
    public string? Style { get; init; }
        = null;
    public List<string> Traits { get; init; } = new();
    public List<ActionDefinition> Actions { get; init; } = new();
    public bool? MoodTracking { get; init; }
        = null;

    public static PersonaFragment LoadFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<PersonaFragment>(yaml)
               ?? throw new InvalidOperationException("Unable to parse persona fragment.");
    }
}
