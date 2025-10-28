using GameRagKit.Config;

namespace GameRagKit.Storage;

public readonly record struct IndexScopeKey(string Scope, string NpcId, string? RegionId, string? FactionId)
{
    public static IndexScopeKey FromSource(PersonaConfig persona, SourceConfig source)
    {
        var tier = source.Tier;
        if (string.IsNullOrWhiteSpace(tier))
        {
            tier = InferTierFromPath(source.File);
        }

        return tier switch
        {
            "world" => ForWorld(persona),
            "region" => ForRegion(persona),
            "faction" => ForFaction(persona),
            "memory" => ForMemory(persona),
            _ => ForPersona(persona)
        };
    }

    public static IndexScopeKey ForWorld(PersonaConfig persona)
        => new("world:" + (persona.WorldId ?? "global"), persona.Id, persona.RegionId, persona.FactionId);

    public static IndexScopeKey ForRegion(PersonaConfig persona)
        => new("region:" + (persona.RegionId ?? "default"), persona.Id, persona.RegionId, persona.FactionId);

    public static IndexScopeKey ForFaction(PersonaConfig persona)
        => new("faction:" + (persona.FactionId ?? "default"), persona.Id, persona.RegionId, persona.FactionId);

    public static IndexScopeKey ForPersona(PersonaConfig persona)
        => new("npc:" + persona.Id, persona.Id, persona.RegionId, persona.FactionId);

    public static IndexScopeKey ForMemory(PersonaConfig persona)
        => new("memory:" + persona.Id, persona.Id, persona.RegionId, persona.FactionId);

    public override string ToString() => Scope;

    private static string InferTierFromPath(string path)
    {
        path = path.Replace('\\', '/');
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
