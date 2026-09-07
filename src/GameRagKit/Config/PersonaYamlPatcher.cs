using System.Text;

namespace GameRagKit.Config;

/// <summary>
/// A single field edit. When <paramref name="IsRawScalar"/> is true, ScalarValue is
/// written to the YAML verbatim (e.g. "true", "false", "42") instead of being treated as
/// a string that might need quoting -- use this for genuinely typed fields like
/// mood_tracking (bool). Leave it false for free-text fields like style/system_prompt.
/// </summary>
public sealed record PersonaFieldEdit(string Key, string? ScalarValue, bool IsRawScalar = false);

public sealed record PersonaYamlPatchResult(bool Success, string? UpdatedYaml, string? UnsupportedReason);

/// <summary>
/// Applies targeted edits to a handful of known persona.* fields (system_prompt, style,
/// mood_tracking) by editing only the affected lines of the original YAML text, rather
/// than re-serializing the whole file through YamlDotNet's object model -- which would
/// silently strip any comments or custom formatting a designer wrote elsewhere in the
/// file. Deliberately narrow in scope: only recognizes the specific syntax shapes these
/// fields use in practice (plain scalars, folded/literal block scalars, simple booleans).
/// If a field's on-disk shape doesn't match a recognized pattern, this returns
/// Success = false rather than guessing, so the caller can fall back to a safe path
/// (e.g. warning the designer and doing a full re-serialize) instead of risking a
/// malformed YAML file.
/// </summary>
public static class PersonaYamlPatcher
{
    public static PersonaYamlPatchResult ApplyEdits(string originalYaml, IReadOnlyList<PersonaFieldEdit> edits)
    {
        var lines = originalYaml.Replace("\r\n", "\n").Split('\n').ToList();

        var personaLineIndex = FindTopLevelKey(lines, "persona");
        if (personaLineIndex < 0)
        {
            return new PersonaYamlPatchResult(false, null, "No top-level \"persona:\" key found.");
        }

        var personaIndent = GetIndent(lines[personaLineIndex]);
        var fieldIndent = personaIndent + 2;
        var blockEnd = FindBlockEnd(lines, personaLineIndex, personaIndent);

        foreach (var edit in edits)
        {
            var result = ApplySingleEdit(lines, personaLineIndex, blockEnd, fieldIndent, edit);
            if (!result.Success)
            {
                return result;
            }

            // Block length may have changed (block scalar edits can add/remove lines);
            // recompute before the next edit.
            blockEnd = FindBlockEnd(lines, personaLineIndex, personaIndent);
        }

        return new PersonaYamlPatchResult(true, string.Join('\n', lines), null);
    }

    private static PersonaYamlPatchResult ApplySingleEdit(
        List<string> lines,
        int personaLineIndex,
        int blockEnd,
        int fieldIndent,
        PersonaFieldEdit edit)
    {
        var fieldLineIndex = FindFieldKey(lines, personaLineIndex + 1, blockEnd, fieldIndent, edit.Key);

        if (fieldLineIndex < 0)
        {
            // Field doesn't exist yet -- insert a new simple scalar line right after
            // "persona:" (before any existing fields). Only supported for plain scalar
            // adds; block-scalar fields are expected to already exist if being edited.
            if (edit.ScalarValue == null)
            {
                return new PersonaYamlPatchResult(false, null, $"Cannot add block-scalar field \"{edit.Key}\" that doesn't already exist.");
            }

            var insertAt = personaLineIndex + 1;
            var indent = new string(' ', fieldIndent);
            var value = edit.IsRawScalar ? edit.ScalarValue : EscapeScalar(edit.ScalarValue);
            lines.Insert(insertAt, $"{indent}{edit.Key}: {value}");
            return new PersonaYamlPatchResult(true, null, null);
        }

        var existingLine = lines[fieldLineIndex];
        var colonIndex = existingLine.IndexOf(':');
        var afterColon = existingLine[(colonIndex + 1)..].TrimStart();

        if (afterColon.StartsWith('>') || afterColon.StartsWith('|'))
        {
            if (edit.ScalarValue == null)
            {
                return new PersonaYamlPatchResult(false, null, $"Field \"{edit.Key}\" edit did not provide a value.");
            }

            var fieldEnd = FindBlockScalarEnd(lines, fieldLineIndex, fieldIndent);
            var replacement = RenderBlockScalar(edit.Key, edit.ScalarValue, fieldIndent);
            lines.RemoveRange(fieldLineIndex, fieldEnd - fieldLineIndex);
            lines.InsertRange(fieldLineIndex, replacement);
            return new PersonaYamlPatchResult(true, null, null);
        }

        // Plain scalar on a single line (covers unquoted, quoted, and flow-list values
        // like traits: [a, b, c] -- though traits editing isn't exposed by the studio UI
        // yet, the single-line replacement here would work for it too).
        if (existingLine.Contains('\n'))
        {
            return new PersonaYamlPatchResult(false, null, $"Field \"{edit.Key}\" has unexpected multi-line content.");
        }

        if (edit.ScalarValue == null)
        {
            return new PersonaYamlPatchResult(false, null, $"Field \"{edit.Key}\" edit did not provide a value.");
        }

        var indentStr = new string(' ', GetIndent(existingLine));
        var newValue = edit.IsRawScalar ? edit.ScalarValue : EscapeScalar(edit.ScalarValue);
        lines[fieldLineIndex] = $"{indentStr}{edit.Key}: {newValue}";
        return new PersonaYamlPatchResult(true, null, null);
    }

    private static List<string> RenderBlockScalar(string key, string value, int fieldIndent)
    {
        var indent = new string(' ', fieldIndent);
        var contentIndent = new string(' ', fieldIndent + 2);
        var result = new List<string> { $"{indent}{key}: >" };

        // Fold into paragraph lines the same way the original samples are hand-wrapped:
        // one line per sentence-ish chunk is unnecessary -- YAML folded scalars join line
        // breaks with spaces, so a single wrapped paragraph is safe and simplest.
        foreach (var line in WrapText(value, 100))
        {
            result.Add($"{contentIndent}{line}");
        }

        return result;
    }

    private static IEnumerable<string> WrapText(string text, int maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxWidth)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string EscapeScalar(string value)
    {
        var needsQuoting = value.Length == 0
            || value.StartsWith('#')
            || value.Contains(':')
            || value.Contains('#')
            || value is "true" or "false" or "null" or "~";

        if (!needsQuoting)
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static int FindTopLevelKey(List<string> lines, string key)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (GetIndent(line) == 0 && line.TrimStart().StartsWith(key + ":"))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindFieldKey(List<string> lines, int start, int end, int expectedIndent, string key)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (GetIndent(line) == expectedIndent && line.TrimStart().StartsWith(key + ":"))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindBlockEnd(List<string> lines, int blockStartIndex, int blockIndent)
    {
        for (var i = blockStartIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (GetIndent(line) <= blockIndent)
            {
                return i;
            }
        }

        return lines.Count;
    }

    private static int FindBlockScalarEnd(List<string> lines, int headerLineIndex, int fieldIndent)
    {
        for (var i = headerLineIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (GetIndent(line) <= fieldIndent)
            {
                return i;
            }
        }

        return lines.Count;
    }

    private static int GetIndent(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }
}
