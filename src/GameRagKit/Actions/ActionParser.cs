using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameRagKit.Config;

namespace GameRagKit.Actions;

public static class ActionParser
{
    private static readonly Regex ActionBlockPattern = new(
        @"```action\s*(?<json>.*?)\s*```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static ActionParseResult Parse(string rawText, IReadOnlyList<ActionDefinition> allowedActions)
    {
        if (string.IsNullOrEmpty(rawText) || allowedActions.Count == 0)
        {
            return new ActionParseResult(rawText, Array.Empty<ActionCall>(), Array.Empty<string>());
        }

        var definitionsByName = allowedActions.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var calls = new List<ActionCall>();
        var errors = new List<string>();

        var cleaned = ActionBlockPattern.Replace(rawText, match =>
        {
            var json = match.Groups["json"].Value;
            if (!TryParseCall(json, definitionsByName, calls, errors))
            {
                // Leave parse/validation errors recorded; still strip the block from player-visible text.
            }

            return string.Empty;
        });

        return new ActionParseResult(CollapseBlankLines(cleaned), calls, errors);
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

    private static string CollapseBlankLines(string text)
    {
        var lines = text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        var builder = new StringBuilder();
        var previousBlank = false;
        foreach (var line in lines)
        {
            var isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && previousBlank)
            {
                continue;
            }

            builder.AppendLine(line);
            previousBlank = isBlank;
        }

        return builder.ToString().Trim();
    }
}
