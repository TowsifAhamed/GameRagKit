namespace GameRagKit;

public sealed record WorldState
{
    public string? TimeOfDay { get; init; }
        = null;
    public bool? InCombat { get; init; }
        = null;
    public IReadOnlyList<NearbyEntity> NearbyEntities { get; init; } = Array.Empty<NearbyEntity>();
    public IReadOnlyList<InventoryItem> PlayerInventory { get; init; } = Array.Empty<InventoryItem>();
    public IReadOnlyDictionary<string, string> Custom { get; init; } = new Dictionary<string, string>();
}

public sealed record NearbyEntity(string Id, string? Type = null, double? DistanceMeters = null);

public sealed record InventoryItem(string ItemId, int Quantity = 1);
