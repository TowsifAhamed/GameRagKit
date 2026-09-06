using System;
using System.Text;
using UnityEngine.Networking;

namespace GameRagKit.Unity
{
    /// <summary>
    /// A DownloadHandlerScript that parses Server-Sent Events incrementally as bytes
    /// arrive over the wire, instead of buffering the entire response and parsing it only
    /// after the request completes (which is what DownloadHandlerBuffer forces you into).
    /// This is what makes AskNpcStreaming a genuine typewriter effect: onLine fires for
    /// each complete "data: ..." line as soon as it's received, not all at once at the end.
    /// </summary>
    internal sealed class SseDownloadHandler : DownloadHandlerScript
    {
        private const string DataPrefix = "data:";
        private readonly Action<string> _onLine;
        private readonly StringBuilder _pending = new();

        public SseDownloadHandler(Action<string> onLine) : base(new byte[4096])
        {
            _onLine = onLine;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return false;
            }

            _pending.Append(Encoding.UTF8.GetString(data, 0, dataLength));
            DrainCompleteLines();
            return true;
        }

        protected override void CompleteContent()
        {
            // Flush any trailing line that wasn't newline-terminated.
            DrainCompleteLines(flushRemainder: true);
        }

        private void DrainCompleteLines(bool flushRemainder = false)
        {
            while (true)
            {
                var text = _pending.ToString();
                var newlineIndex = text.IndexOf('\n');
                if (newlineIndex < 0)
                {
                    if (flushRemainder && text.Length > 0)
                    {
                        _pending.Clear();
                        EmitLine(text);
                    }

                    return;
                }

                var line = text[..newlineIndex];
                _pending.Remove(0, newlineIndex + 1);
                EmitLine(line);
            }
        }

        private void EmitLine(string line)
        {
            line = line.TrimEnd('\r');
            if (line.Length == 0 || !line.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var payload = line[DataPrefix.Length..].Trim();
            if (payload.Length > 0)
            {
                _onLine?.Invoke(payload);
            }
        }
    }
}
