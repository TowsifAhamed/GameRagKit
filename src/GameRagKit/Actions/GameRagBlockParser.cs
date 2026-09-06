using System.Text;
using System.Text.RegularExpressions;

namespace GameRagKit.Actions;

/// <summary>
/// Extracts GameRagKit's structured-data blocks from a model reply. The wire format is
/// deliberately NOT a markdown code fence (```tag ... ```), since that collides with
/// ordinary code fences a model might legitimately include in a reply. Instead:
///
///   [[gamerag:tag]]
///   { ...json... }
///   [[/gamerag]]
///
/// is used, which no model has any independent reason to produce for any other purpose.
/// This class only extracts (tag, rawJson) pairs and strips them from player-visible text;
/// it does not know or care what any particular tag's JSON means -- see ActionParser and
/// MoodParser for tag-specific validation.
/// </summary>
public static class GameRagBlockParser
{
    private static readonly Regex BlockPattern = new(
        @"\[\[gamerag:(?<tag>[a-zA-Z0-9_]+)\]\]\s*(?<json>.*?)\s*\[\[/gamerag\]\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static GameRagBlockParseResult Parse(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return new GameRagBlockParseResult(rawText, Array.Empty<GameRagBlock>());
        }

        var blocks = new List<GameRagBlock>();
        var cleaned = BlockPattern.Replace(rawText, match =>
        {
            blocks.Add(new GameRagBlock(match.Groups["tag"].Value, match.Groups["json"].Value));
            return string.Empty;
        });

        return new GameRagBlockParseResult(CollapseBlankLines(cleaned), blocks);
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
