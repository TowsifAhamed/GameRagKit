using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameRagKit.Unity
{
    /// <summary>One spoken NPC line in a scene, as handed to NpcSceneManager's events.</summary>
    public class NpcSceneLine
    {
        public int Index;
        public string NpcId;
        public string Text;
        /// <summary>"routed" (answering the player) or "reaction" (answering another NPC).</summary>
        public string Reason;
        public string Mood;
        public ActionCall[] Actions = Array.Empty<ActionCall>();
        /// <summary>Suggested gesture: "Interact", "Cheer", "Block", "Use_Item", the NPC's signature gesture, or null.</summary>
        public string Gesture;
        /// <summary>Who the line is aimed at: "player" or another participant's NPC id.</summary>
        public string Addressee = "player";
    }

    /// <summary>A line of scene history: only what was actually heard.</summary>
    [Serializable]
    public class SceneHistoryLine
    {
        /// <summary>"player" or an NPC id.</summary>
        public string speaker;
        public string text;
        /// <summary>The speaker was talked over; text holds just the part that was heard.</summary>
        public bool interrupted;
    }

    /// <summary>Server-sent scene event (see docs/scenes.md in the GameRagKit repo).</summary>
    internal sealed class SceneEvent
    {
        public string Type;
        public string Text;
        public string Npc;
        public int Index;
        public string Reason;
        public string Method;
        public string[] Responders = Array.Empty<string>();
        public string Mood;
        public ActionCall[] Actions = Array.Empty<ActionCall>();
        public string WavBase64;
        public string Error;
    }

    /// <summary>
    /// Conversation rules shared with the web demo (voice-io.js): where an interrupted line
    /// was cut, which gesture fits a line, and who a line is aimed at.
    /// </summary>
    internal static class SceneSpeech
    {
        private static readonly Regex Laugh = new(@"\b(ha){2,}\b|\bha!|\bhah\b", RegexOptions.IgnoreCase);
        private static readonly Regex Proclaim = new(@"^\W*(hear ye|oyez|listen|attention)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Splits a line at roughly <paramref name="spokenChars"/> into what was heard and
        /// what wasn't, on a word boundary; the word being spoken when it was cut counts as unsaid.
        /// </summary>
        public static (string Said, string Unsaid) SplitSpoken(string text, int spokenChars)
        {
            text ??= string.Empty;
            if (spokenChars >= text.Length - 2)
            {
                return (text.Trim(), string.Empty);
            }

            if (spokenChars <= 0)
            {
                return (string.Empty, text.Trim());
            }

            var cut = text.LastIndexOf(' ', Math.Min(spokenChars, text.Length - 1));
            return cut <= 0 ? (string.Empty, text.Trim()) : (text[..cut].Trim(), text[cut..].Trim());
        }

        public static string ChooseGesture(string text, string mood, ActionCall[] actions, string signature, Random random)
        {
            text ??= string.Empty;
            var m = (mood ?? string.Empty).ToLowerInvariant();
            if (actions != null && actions.Length > 0)
            {
                return Regex.IsMatch(actions[0].Name ?? string.Empty, "give|hand|trade|sell|offer|buy", RegexOptions.IgnoreCase) ? "Use_Item" : "Interact";
            }

            if (Regex.IsMatch(m, "delight|happy|joy|excit|cheer|amus|glad|thrill|merry") || Laugh.IsMatch(text))
            {
                return "Cheer";
            }

            if (Regex.IsMatch(m, "wary|suspici|annoy|angry|hostile|defensive|irritat|offend"))
            {
                return "Block";
            }

            if (!string.IsNullOrEmpty(signature) && (Proclaim.IsMatch(text) || text.Count(c => c == '!') >= 2))
            {
                return signature;
            }

            return random.NextDouble() < 0.65 ? "Interact" : null;
        }

        /// <summary>The first other participant named in the line, else "player".</summary>
        public static string AddresseeOf(string text, string speakerId, IEnumerable<(string NpcId, string Name)> participants)
        {
            var best = "player";
            var bestIndex = int.MaxValue;
            foreach (var (npcId, name) in participants)
            {
                if (npcId == speakerId || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var match = Regex.Match(text ?? string.Empty, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);
                if (match.Success && match.Index < bestIndex)
                {
                    best = npcId;
                    bestIndex = match.Index;
                }
            }

            return best;
        }

        public static string JsonString(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var sb = new System.Text.StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
