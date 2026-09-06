namespace GameRagKit;

public static class WorldStateFormatter
{
    /// <summary>
    /// Renders a WorldState into a deterministic "WORLD STATE:" prompt block, or an empty
    /// string if there's nothing to report. Field order is fixed so identical WorldState
    /// input always produces identical prompt text.
    /// </summary>
    public static string Format(WorldState? worldState)
    {
        if (worldState is null)
        {
            return string.Empty;
        }

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(worldState.TimeOfDay))
        {
            lines.Add($"Time of day: {worldState.TimeOfDay}");
        }

        if (worldState.InCombat == true)
        {
            lines.Add("Player is currently in combat.");
        }

        if (worldState.NearbyEntities.Count > 0)
        {
            var entities = worldState.NearbyEntities.Select(e =>
            {
                var typePart = string.IsNullOrWhiteSpace(e.Type) ? null : e.Type;
                var distancePart = e.DistanceMeters.HasValue ? $"{e.DistanceMeters:0.#}m away" : null;
                var details = string.Join(", ", new[] { typePart, distancePart }.Where(s => s != null));
                return details.Length > 0 ? $"{e.Id} ({details})" : e.Id;
            });
            lines.Add($"Nearby: {string.Join("; ", entities)}");
        }

        if (worldState.PlayerInventory.Count > 0)
        {
            var items = worldState.PlayerInventory.Select(i => $"{i.ItemId} x{i.Quantity}");
            lines.Add($"Player inventory: {string.Join(", ", items)}");
        }

        foreach (var pair in worldState.Custom)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                lines.Add($"{pair.Key}: {pair.Value}");
            }
        }

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("WORLD STATE:");
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine("---");
        return builder.ToString();
    }
}
