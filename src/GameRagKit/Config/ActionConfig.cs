namespace GameRagKit.Config;

public sealed record ActionDefinition
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
        = null;
    public List<ActionArgDefinition> Args { get; init; } = new();
}

public sealed record ActionArgDefinition
{
    public string Name { get; init; } = string.Empty;

    // Supported types: string | number | boolean
    public string Type { get; init; } = "string";
    public bool Required { get; init; } = true;
}
