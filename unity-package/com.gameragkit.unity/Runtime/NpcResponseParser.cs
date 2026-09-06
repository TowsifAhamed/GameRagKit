using System;
using System.Collections.Generic;
using System.Linq;

namespace GameRagKit.Unity
{
    /// <summary>
    /// Parses GameRagKit's /ask and /ask/stream "end" JSON payloads using MiniJson, since
    /// both contain a dynamic-keyed action args object that Unity's JsonUtility cannot
    /// deserialize.
    /// </summary>
    internal static class NpcResponseParser
    {
        public static NpcResponse ParseAskResponse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>
                ?? throw new MiniJsonException("Expected a JSON object at the response root.");

            return new NpcResponse
            {
                Answer = GetString(root, "answer") ?? string.Empty,
                Sources = GetStringArray(root, "sources"),
                Scores = GetFloatArray(root, "scores"),
                FromCloud = GetBool(root, "fromCloud"),
                Actions = GetActions(root, "actions")
            };
        }

        public static bool TryParseStreamEvent(string json, out string type, out string text, out string[] sources, out ActionCall[] actions)
        {
            type = null;
            text = null;
            sources = Array.Empty<string>();
            actions = Array.Empty<ActionCall>();

            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
            {
                return false;
            }

            type = GetString(root, "type");
            text = GetString(root, "text");
            sources = GetStringArray(root, "sources");
            actions = GetActions(root, "actions");
            return type != null;
        }

        private static string GetString(Dictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out var value) ? value as string : null;
        }

        private static bool GetBool(Dictionary<string, object> obj, string key)
        {
            return obj.TryGetValue(key, out var value) && value is bool b && b;
        }

        private static string[] GetStringArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || !(value is List<object> list))
            {
                return Array.Empty<string>();
            }

            return list.Select(v => v as string ?? string.Empty).ToArray();
        }

        private static float[] GetFloatArray(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || !(value is List<object> list))
            {
                return Array.Empty<float>();
            }

            return list.Select(v => v is double d ? (float)d : 0f).ToArray();
        }

        private static ActionCall[] GetActions(Dictionary<string, object> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || !(value is List<object> list))
            {
                return Array.Empty<ActionCall>();
            }

            var results = new List<ActionCall>();
            foreach (var entry in list)
            {
                if (entry is not Dictionary<string, object> actionObj)
                {
                    continue;
                }

                var name = GetString(actionObj, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var args = new Dictionary<string, string>();
                if (actionObj.TryGetValue("args", out var argsValue) && argsValue is Dictionary<string, object> argsObj)
                {
                    foreach (var pair in argsObj)
                    {
                        args[pair.Key] = pair.Value switch
                        {
                            string s => s,
                            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            bool b => b ? "true" : "false",
                            _ => pair.Value?.ToString() ?? string.Empty
                        };
                    }
                }

                results.Add(new ActionCall { Name = name, Args = args });
            }

            return results.ToArray();
        }
    }
}
