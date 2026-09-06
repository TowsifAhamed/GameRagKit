using System.Text;

namespace GameRagKit.Actions;

/// <summary>
/// Splits an incoming token stream into player-visible text chunks, holding back any text
/// once a "[[gamerag:tag]]" block opens so raw structured-data JSON (actions, mood, etc.)
/// is never shown to the player, and resuming visible output once the matching
/// "[[/gamerag]]" close marker is seen. Safe to feed one token at a time; correctly
/// handles a marker split across token boundaries. The marker is deliberately not a
/// markdown code fence (```tag), since that would collide with ordinary code blocks a
/// model might legitimately include in a reply -- see GameRagBlockParser for the exact
/// wire format this filter's open/close markers match.
/// </summary>
public sealed class ActionFenceStreamFilter
{
    private const string OpenMarkerPrefix = "[[gamerag:";
    private const string CloseMarker = "[[/gamerag]]";

    private readonly StringBuilder _buffer = new();
    private bool _insideBlock;

    public IEnumerable<string> Push(string token)
    {
        var results = new List<string>();
        _buffer.Append(token);

        while (true)
        {
            if (!_insideBlock)
            {
                var text = _buffer.ToString();
                var openIndex = text.IndexOf(OpenMarkerPrefix, StringComparison.Ordinal);
                if (openIndex < 0)
                {
                    // No opener found yet. Hold back a small tail in case the marker is
                    // split across this token and the next one.
                    var safeLength = Math.Max(0, text.Length - OpenMarkerPrefix.Length);
                    if (safeLength > 0)
                    {
                        results.Add(text[..safeLength]);
                        _buffer.Remove(0, safeLength);
                    }

                    break;
                }

                // Found the opener prefix. Wait for the closing "]]" of "[[gamerag:tag]]"
                // before committing to hiding the block, so a partial prefix match doesn't
                // prematurely swallow player-visible text.
                var closeBracketIndex = text.IndexOf("]]", openIndex, StringComparison.Ordinal);
                if (closeBracketIndex < 0)
                {
                    if (openIndex > 0)
                    {
                        results.Add(text[..openIndex]);
                        _buffer.Remove(0, openIndex);
                    }

                    break;
                }

                if (openIndex > 0)
                {
                    results.Add(text[..openIndex]);
                }

                _buffer.Remove(0, closeBracketIndex + "]]".Length);
                _insideBlock = true;
                continue;
            }

            var buffered = _buffer.ToString();
            var closeIndex = buffered.IndexOf(CloseMarker, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                break;
            }

            _buffer.Remove(0, closeIndex + CloseMarker.Length);
            _insideBlock = false;
        }

        return results;
    }

    /// <summary>
    /// Call once the underlying token stream is exhausted. Emits any remaining held-back
    /// text that turned out not to be part of a block (e.g. trailing "[[gam" that never
    /// grew into a full opener). If the stream ended mid block, that trailing content is
    /// incomplete/unparseable and is intentionally dropped rather than shown to the player.
    /// </summary>
    public string Flush()
    {
        if (_insideBlock || _buffer.Length == 0)
        {
            _buffer.Clear();
            return string.Empty;
        }

        var remaining = _buffer.ToString();
        _buffer.Clear();
        return remaining;
    }
}
