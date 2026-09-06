using System.Text;

namespace GameRagKit.Actions;

/// <summary>
/// Splits an incoming token stream into player-visible text chunks, holding back any text
/// once a ```action fence opens so raw action JSON is never shown to the player, and
/// resuming visible output once the closing fence is seen. Safe to feed one token at a
/// time; correctly handles a fence marker split across token boundaries.
/// </summary>
public sealed class ActionFenceStreamFilter
{
    private const string OpenFence = "```action";
    private const string CloseFence = "```";

    private readonly StringBuilder _buffer = new();
    private bool _insideActionBlock;

    public IEnumerable<string> Push(string token)
    {
        var results = new List<string>();
        _buffer.Append(token);

        while (true)
        {
            if (!_insideActionBlock)
            {
                var text = _buffer.ToString();
                var openIndex = text.IndexOf(OpenFence, StringComparison.Ordinal);
                if (openIndex < 0)
                {
                    // No fence found yet. Hold back a small tail in case the fence marker
                    // is split across this token and the next one.
                    var safeLength = Math.Max(0, text.Length - OpenFence.Length);
                    if (safeLength > 0)
                    {
                        results.Add(text[..safeLength]);
                        _buffer.Remove(0, safeLength);
                    }

                    break;
                }

                if (openIndex > 0)
                {
                    results.Add(text[..openIndex]);
                }

                _buffer.Remove(0, openIndex + OpenFence.Length);
                _insideActionBlock = true;
                continue;
            }

            var buffered = _buffer.ToString();
            var closeIndex = buffered.IndexOf(CloseFence, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                break;
            }

            _buffer.Remove(0, closeIndex + CloseFence.Length);
            _insideActionBlock = false;
        }

        return results;
    }

    /// <summary>
    /// Call once the underlying token stream is exhausted. Emits any remaining held-back
    /// text that turned out not to be part of a fence (e.g. trailing "``" that never grew
    /// into "```action"). If the stream ended mid action-block, that trailing content is
    /// incomplete/unparseable and is intentionally dropped rather than shown to the player.
    /// </summary>
    public string Flush()
    {
        if (_insideActionBlock || _buffer.Length == 0)
        {
            _buffer.Clear();
            return string.Empty;
        }

        var remaining = _buffer.ToString();
        _buffer.Clear();
        return remaining;
    }
}
