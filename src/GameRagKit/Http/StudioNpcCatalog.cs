namespace GameRagKit.Http;

public sealed record StudioNpcEntry(string PersonaId, string YamlFilePath);

/// <summary>
/// Tracks which YAML file backs each loaded NPC, so the studio UI can read/write the
/// correct file. AgentRegistry only supports lookup by key (persona id or file-stem
/// alias) and doesn't retain file paths, so this is a separate small catalog built
/// alongside it when `gamerag studio` starts.
/// </summary>
public sealed class StudioNpcCatalog
{
    private readonly List<StudioNpcEntry> _entries;

    public StudioNpcCatalog(IEnumerable<StudioNpcEntry> entries)
    {
        _entries = entries.ToList();
    }

    public IReadOnlyList<StudioNpcEntry> All => _entries;

    public bool TryGetYamlPath(string personaId, out string yamlFilePath)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.PersonaId, personaId, StringComparison.OrdinalIgnoreCase));
        yamlFilePath = entry?.YamlFilePath ?? string.Empty;
        return entry != null;
    }
}
