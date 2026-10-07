using System.Collections.Generic;
using GameRagKit.Unity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Minimal voice conversation with a group of NPCs: hold V to talk, Enter to type, Esc to
/// cut the NPCs off. Put it on a GameObject with an NpcSceneManager whose participants each
/// have an AudioSource on the NPC (spatial blend 1) and, optionally, an Animator with a
/// "Talking" bool and gesture triggers (Interact, Cheer, Block, Use_Item).
/// </summary>
[RequireComponent(typeof(NpcSceneManager))]
public class VoiceSceneExample : MonoBehaviour
{
    private NpcSceneManager _scene;
    private readonly List<string> _log = new();
    private string _typed = "";
    private bool _typing;
    private string _status = "";

    private void Awake()
    {
        _scene = GetComponent<NpcSceneManager>();
        _scene.TranscriptReceived += text => _log.Add($"You: {text}");
        _scene.NpcThinking += npc => _status = $"{NameOf(npc)} is thinking...";
        _scene.LineStarted += line =>
        {
            _status = $"{NameOf(line.NpcId)} is speaking...";
            _log.Add($"{NameOf(line.NpcId)}: {line.Text}");
            foreach (var action in line.Actions)
            {
                Debug.Log($"{line.NpcId} wants to {action.Name}");
            }
        };
        _scene.LineFinished += (line, interrupted, said, unsaid) =>
        {
            _status = "";
            if (interrupted)
            {
                // Only what was heard stays in the log; the NPC remembers the rest.
                _log[_log.Count - 1] = $"{NameOf(line.NpcId)}: {said} —";
            }
        };
        _scene.Error += message => _log.Add($"(error) {message}");
    }

    private void Update()
    {
        if (_typing)
        {
            return;
        }

        if (KeyDown(KeyCodeV())) _scene.StartListening();
        if (KeyUp(KeyCodeV())) _scene.StopListeningAndSend();
        if (KeyDown(KeyCodeEscape())) _scene.Interrupt();
        if (KeyDown(KeyCodeEnter())) _typing = true;
        _status = _scene.IsListening ? "Listening... release V to send" : _status;
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, Screen.height - 260, 600, 240), GUI.skin.box);
        var start = Mathf.Max(0, _log.Count - 8);
        for (var i = start; i < _log.Count; i++)
        {
            GUILayout.Label(_log[i]);
        }

        GUILayout.Label(_status);
        if (_typing)
        {
            GUI.SetNextControlName("say");
            _typed = GUILayout.TextField(_typed);
            GUI.FocusControl("say");
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                if (_typed.Trim().Length > 0)
                {
                    _log.Add($"You: {_typed}");
                    _scene.Say(_typed);
                }

                _typed = "";
                _typing = false;
            }
        }
        else
        {
            GUILayout.Label("Hold V to talk · Enter to type · Esc to cut them off");
        }

        GUILayout.EndArea();
    }

    private string NameOf(string npcId) => _scene.participants.Find(p => p.npcId == npcId)?.displayName ?? npcId;

#if ENABLE_INPUT_SYSTEM
    private static Key KeyCodeV() => Key.V;
    private static Key KeyCodeEscape() => Key.Escape;
    private static Key KeyCodeEnter() => Key.Enter;
    private static bool KeyDown(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
    private static bool KeyUp(Key key) => Keyboard.current != null && Keyboard.current[key].wasReleasedThisFrame;
#else
    private static KeyCode KeyCodeV() => KeyCode.V;
    private static KeyCode KeyCodeEscape() => KeyCode.Escape;
    private static KeyCode KeyCodeEnter() => KeyCode.Return;
    private static bool KeyDown(KeyCode key) => Input.GetKeyDown(key);
    private static bool KeyUp(KeyCode key) => Input.GetKeyUp(key);
#endif
}
