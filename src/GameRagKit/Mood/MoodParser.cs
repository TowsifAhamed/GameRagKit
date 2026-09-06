using System.Text.Json;
using GameRagKit.Actions;

namespace GameRagKit.Mood;

public static class MoodParser
{
    private const string Tag = "mood";

    /// <summary>
    /// Validates the "mood"-tagged blocks out of a shared GameRagBlockParser result. If
    /// multiple mood blocks appear in one reply (the model should only emit at most one,
    /// but nothing stops it from emitting more), the last valid one wins -- treated as the
    /// NPC's final mood after the whole reply. Returns null if no valid mood block was
    /// found; callers should keep the NPC's previously persisted mood in that case.
    /// </summary>
    public static MoodParseResult Validate(IReadOnlyList<GameRagBlock> blocks, DateTimeOffset now)
    {
        MoodState? result = null;
        var errors = new List<string>();

        foreach (var block in blocks)
        {
            if (!string.Equals(block.Tag, Tag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseMood(block.RawJson, now, errors, out var mood))
            {
                result = mood;
            }
        }

        return new MoodParseResult(result, errors);
    }

    private static bool TryParseMood(string json, DateTimeOffset now, List<string> errors, out MoodState? mood)
    {
        mood = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"Malformed mood JSON: {ex.Message}");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
            {
                errors.Add("Mood block missing required \"value\" field.");
                return false;
            }

            var value = valueElement.GetString()!.Trim();
            if (string.IsNullOrEmpty(value))
            {
                errors.Add("Mood block \"value\" must not be empty.");
                return false;
            }

            var intensity = 0.5;
            if (root.TryGetProperty("intensity", out var intensityElement))
            {
                if (intensityElement.ValueKind != JsonValueKind.Number)
                {
                    errors.Add("Mood block \"intensity\" must be a number.");
                    return false;
                }

                intensity = Math.Clamp(intensityElement.GetDouble(), 0.0, 1.0);
            }

            mood = new MoodState(value, intensity, now);
            return true;
        }
    }
}

public sealed record MoodParseResult(MoodState? Mood, IReadOnlyList<string> Errors);
