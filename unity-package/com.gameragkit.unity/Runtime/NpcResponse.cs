using System;
using System.Collections.Generic;

namespace GameRagKit.Unity
{
    /// <summary>
    /// A single NPC response. <see cref="Actions"/> is populated by
    /// <see cref="NpcDialogueManager"/> after parsing the raw JSON, since Unity's built-in
    /// JsonUtility cannot deserialize the server's dynamic-keyed action args objects.
    /// </summary>
    public class NpcResponse
    {
        public string Answer;
        public string[] Sources;
        public float[] Scores;
        public bool FromCloud;
        public ActionCall[] Actions = Array.Empty<ActionCall>();
    }

    public class ActionCall
    {
        public string Name;
        public IReadOnlyDictionary<string, string> Args = new Dictionary<string, string>();
    }
}
