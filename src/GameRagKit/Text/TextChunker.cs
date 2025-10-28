using System.Text;

namespace GameRagKit.Text;

public sealed class TextChunker
{
    public IEnumerable<string> Chunk(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        chunkSize = Math.Max(chunkSize, 128);
        overlap = Math.Clamp(overlap, 0, chunkSize / 2);

        var normalized = Normalize(text);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        var start = 0;

        while (start < words.Length)
        {
            builder.Clear();
            var end = Math.Min(start + chunkSize, words.Length);
            for (var i = start; i < end; i++)
            {
                builder.Append(words[i]);
                builder.Append(' ');
            }

            yield return builder.ToString().Trim();
            if (end >= words.Length)
            {
                yield break;
            }

            start = Math.Max(0, end - overlap);
        }
    }

    private static string Normalize(string text)
    {
        return text.Replace('\r', ' ')
                   .Replace('\n', ' ')
                   .Replace('\t', ' ')
                   .Replace("  ", " ");
    }
}
