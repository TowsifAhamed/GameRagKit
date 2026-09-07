using System.Linq;
using GameRagKit.Config;

namespace GameRagKit.Storage;

/// <summary>
/// Identifies one shared index/lore scope, e.g. "faction:city-guard" or "npc:guard-north-gate".
/// TierPath carries the NPC's full ancestor chain (whatever names/depth the dev declared)
/// so callers like NpcAgent.BuildMetadata can tag chunks with every ancestor, not just a
/// fixed region/faction pair. Equality/hashing is by Scope alone (which already fully
/// identifies the scope) -- TierPath is payload, not identity, because PersonaConfig.TierPath
/// builds a fresh List each call, and record struct's default equality would otherwise
/// compare that list by reference and treat two logically-identical scopes as different keys.
/// </summary>
public readonly record struct IndexScopeKey(string Scope, string NpcId, IReadOnlyList<PersonaTier> TierPath)
{
    public bool Equals(IndexScopeKey other) => string.Equals(Scope, other.Scope, StringComparison.Ordinal);

    public override int GetHashCode() => Scope.GetHashCode();

    public static IndexScopeKey FromSource(PersonaConfig persona, SourceConfig source)
    {
        var tier = source.Tier;
        if (string.IsNullOrWhiteSpace(tier))
        {
            tier = InferTierFromPath(persona, source.File);
        }

        if (string.Equals(tier, "memory", StringComparison.OrdinalIgnoreCase))
        {
            return ForMemory(persona);
        }

        if (string.Equals(tier, "npc", StringComparison.OrdinalIgnoreCase))
        {
            return ForPersona(persona);
        }

        var match = persona.TierPath.FirstOrDefault(t => string.Equals(t.Name, tier, StringComparison.OrdinalIgnoreCase));
        return match is { Name.Length: > 0 } ? ForTier(persona, match.Name) : ForPersona(persona);
    }

    public static IndexScopeKey ForTier(PersonaConfig persona, string tierName)
    {
        var tier = persona.TierPath.FirstOrDefault(t => string.Equals(t.Name, tierName, StringComparison.OrdinalIgnoreCase));
        var id = tier is { Name.Length: > 0 } ? tier.Id : "default";
        return new(tierName + ":" + id, persona.Id, persona.TierPath);
    }

    public static IndexScopeKey ForPersona(PersonaConfig persona)
        => new("npc:" + persona.Id, persona.Id, persona.TierPath);

    public static IndexScopeKey ForMemory(PersonaConfig persona)
        => new("memory:" + persona.Id, persona.Id, persona.TierPath);

    public override string ToString() => Scope;

    private static string InferTierFromPath(PersonaConfig persona, string path)
    {
        path = path.Replace('\\', '/');

        foreach (var tier in persona.TierPath)
        {
            if (path.Contains("/" + tier.Name + "/", StringComparison.OrdinalIgnoreCase))
            {
                return tier.Name;
            }
        }

        if (path.Contains("/world/", StringComparison.OrdinalIgnoreCase))
        {
            return "world";
        }

        if (path.Contains("/region/", StringComparison.OrdinalIgnoreCase))
        {
            return "region";
        }

        if (path.Contains("/faction/", StringComparison.OrdinalIgnoreCase))
        {
            return "faction";
        }

        if (path.Contains("/memory/", StringComparison.OrdinalIgnoreCase))
        {
            return "memory";
        }

        return "npc";
    }
}
