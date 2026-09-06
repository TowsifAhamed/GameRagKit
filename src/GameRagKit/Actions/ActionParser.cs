using System.Text.Json;
using GameRagKit.Config;

namespace GameRagKit.Actions;

public static class ActionParser
{
    private const string Tag = "action";

    /// <summary>
    /// Validates the "action"-tagged blocks out of a shared GameRagBlockParser result.
    /// Callers that also care about other tags (e.g. "mood") should call
    /// GameRagBlockParser.Parse once and pass the same Blocks list to each validator,
    /// rather than re-parsing the raw text per tag.
    /// </summary>
    public static ActionParseResult Validate(IReadOnlyList<GameRagBlock> blocks, IReadOnlyList<ActionDefinition> allowedActions)
    {
        if (blocks.Count == 0 || allowedActions.Count == 0)
        {
            return new ActionParseResult(Array.Empty<ActionCall>(), Array.Empty<string>());
        }

        var definitionsByName = allowedActions.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var calls = new List<ActionCall>();
        var errors = new List<string>();

        foreach (var block in blocks)
        {
            if (!string.Equals(block.Tag, Tag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryParseCall(block.RawJson, definitionsByName, calls, errors);
        }

        return new ActionParseResult(calls, errors);
    }

    private static bool TryParseCall(
        string json,
        IReadOnlyDictionary<string, ActionDefinition> definitionsByName,
        List<ActionCall> calls,
        List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"Malformed action JSON: {ex.Message}");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                errors.Add("Action block missing required \"name\" field.");
                return false;
            }

            var name = nameElement.GetString()!;
            if (!definitionsByName.TryGetValue(name, out var definition))
            {
                errors.Add($"Action \"{name}\" is not in the NPC's allowed action list.");
                return false;
            }

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argsElement.EnumerateObject())
                {
                    args[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => property.Value.GetRawText()
                    };
                }
            }

            foreach (var argDef in definition.Args)
            {
                if (argDef.Required && !args.ContainsKey(argDef.Name))
                {
                    errors.Add($"Action \"{name}\" missing required arg \"{argDef.Name}\".");
                    return false;
                }
            }

            var declaredNames = definition.Args.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownArgs = args.Keys.Where(k => !declaredNames.Contains(k)).ToList();
            foreach (var unknown in unknownArgs)
            {
                args.Remove(unknown);
            }

            calls.Add(new ActionCall(name, args));
            return true;
        }
    }
}
