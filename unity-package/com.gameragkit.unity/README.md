# GameRagKit Unity Client

An installable Unity package (UPM) for talking to a GameRagKit server: ask NPCs
questions, stream responses for a typewriter effect, send structured world state, and
receive validated NPC actions.

This package is an HTTP client only — it does not embed the GameRagKit RAG/LLM runtime.
Run a GameRagKit server separately (`gamerag serve`) and point this package at it, the
same way Convai/Inworld's own Unity SDKs talk to their hosted backends.

## Install

### Via Package Manager (git URL)

In Unity's Package Manager, "Add package from git URL":

```
https://github.com/TowsifAhamed/GameRagKit.git?path=unity-package/com.gameragkit.unity
```

### Via local path (for development against a cloned GameRagKit repo)

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.gameragkit.unity": "file:../../GameRagKit/unity-package/com.gameragkit.unity"
  }
}
```

## Quick start

1. Start a GameRagKit server: `dotnet run --project src/GameRagKit.Cli -- serve --config samples/example-npcs --port 5280`
2. Add a `NpcDialogueManager` component to a GameObject, set `Server Url` to `http://localhost:5280`.
3. Call it from your own script:

```csharp
using GameRagKit.Unity;

public class MyNpcController : MonoBehaviour
{
    public NpcDialogueManager dialogueManager;

    void Start()
    {
        dialogueManager.AskNpc(
            "guard-north-gate",
            "What is your duty?",
            importance: 0.3f,
            onSuccess: response => Debug.Log(response.Answer),
            onError: error => Debug.LogError(error));
    }
}
```

For a typewriter effect, use `AskNpcStreaming` instead — see the `BasicDialogue` sample
(Package Manager → GameRagKit Unity Client → Samples → Import) for a complete UI wiring.

## Structured world state

Pass a `WorldState` to either `AskNpc` or `AskNpcStreaming` to tell the NPC what's nearby,
what's in the player's inventory, or the time of day — see
[`docs/world-state.md`](../../docs/world-state.md) in the main repo for the full field
reference:

```csharp
var worldState = new WorldState
{
    timeOfDay = "night",
    inCombat = true,
    nearbyEntities = new[] { new NearbyEntity("goblin-1", "goblin", 4.2f) },
    playerInventory = new[] { new InventoryItem("brass_token", 1) }
};

dialogueManager.AskNpc("guard-north-gate", "What's going on?", 0.3f, worldState, onSuccess, onError);
```

> **Known limitation:** the server's `worldState.custom` open dictionary field isn't
> exposed on the Unity side yet, since Unity's built-in `JsonUtility` can't serialize
> dictionaries. Use the built-in fields (`timeOfDay`, `inCombat`, `nearbyEntities`,
> `playerInventory`) for now.

## NPC actions

If an NPC's YAML declares `persona.actions` (see [`docs/actions.md`](../../docs/actions.md)),
validated action calls come back on `response.Actions` (non-streaming) or in the
`onComplete` callback's second parameter (streaming):

```csharp
dialogueManager.AskNpcStreaming(
    "guard-north-gate", "I have a brass token", 0.4f,
    onChunk: chunk => dialogueText.text += chunk,
    onComplete: (sources, actions) =>
    {
        foreach (var action in actions)
        {
            if (action.Name == "give_item")
            {
                inventory.AddItem(action.Args["item_id"], int.Parse(action.Args["quantity"]));
            }
        }
    });
```

## Voice conversations (one or more NPCs)

`NpcSceneManager` holds spoken conversations through the server's `/scene` API (see
[`docs/scenes.md`](../../docs/scenes.md)): the player holds a key and talks (or types), the
server decides which NPC(s) should answer, and each reply plays in that NPC's own voice
from an `AudioSource` on the NPC. NPCs can answer each other, and the player can talk over
them: only the words actually heard are remembered, the NPC keeps what it didn't get to
say, and "go on" hands it the floor back. One participant works too.

```csharp
using GameRagKit.Unity;

public class TavernTable : MonoBehaviour
{
    public NpcSceneManager scene; // participants set in the Inspector: npcId, displayName, voice (AudioSource), root, animator, headBone
    public TMPro.TMP_Text subtitle;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V)) scene.StartListening();      // push-to-talk down (also cuts NPCs off)
        if (Input.GetKeyUp(KeyCode.V)) scene.StopListeningAndSend();  // push-to-talk up
    }

    void Start()
    {
        scene.LineStarted += line => subtitle.text = $"{line.NpcId}: {line.Text}"; // line.Gesture, line.Actions, line.Mood
        scene.LineFinished += (line, interrupted, said, unsaid) => { if (interrupted) subtitle.text = said + " —"; };
        scene.Say("Mira, does Bram owe you money?");                 // typed input works the same way
    }
}
```

- **Voices** come from the server (Kokoro or Piper per NPC). Set each `AudioSource`'s
  spatial blend to 1 so speech comes from where the NPC stands. Without server voices,
  lines still arrive (and stay on screen for their reading time) as text.
- **Microphone input** needs speech-to-text on the server (whisper.cpp). Already have your
  own capture or recognizer? Call `SendSpeech(wavBytes)` (any 16-bit PCM WAV; see
  `WavUtility.EncodeSpeechWav`) or `Say(text)`.
- **Body language**, all optional per participant: `root` turns to face whoever the NPC is
  talking or listening to; `animator` gets a `Talking` bool while speaking and a trigger
  named after the suggested gesture (`Interact`, `Cheer`, `Block`, `Use_Item`, or the
  participant's `signatureGesture`) when the controller defines it; `headBone` nods with
  the loudness of the speech. `GetSpeechLevel(npcId)` gives that loudness for jaw bones or
  blend shapes.
- **Interruptions**: `Interrupt()` (also called by `StartListening` and `Say`) stops the
  voice mid-line and reports `said`/`unsaid` through `LineFinished`; `History` holds only
  what was heard.
- Unity 2022+ blocks plain `http://` by default: set **Project Settings → Player → Allow
  downloads over HTTP** for a local server, or use HTTPS.
- Microphone capture isn't available in WebGL builds; use `Say()` with the browser's
  speech recognition there.

Import the **Voice Scene** sample (Package Manager → Samples) for a working hold-to-talk
setup. Play Mode tests in `Tests/Runtime` run the whole loop against a local server
(`scripts/run-voice-scene.sh`); add the package to your manifest's `testables` to run them.

## Why streaming actually streams

`AskNpcStreaming` parses Server-Sent Events incrementally via a custom
`DownloadHandlerScript` (`SseDownloadHandler`), so `onChunk` fires as each chunk arrives
over the wire — not all at once after the whole response completes. This is what makes
the typewriter effect real rather than an all-at-once reveal with an artificial delay.

## Why there's a bundled MiniJson parser

The server's action-call `args` field is a JSON object with dynamic string keys (e.g.
`{"item_id":"brass_token"}`). Unity's built-in `JsonUtility` cannot deserialize arbitrary
dictionaries at all, so this package bundles a small, dependency-free JSON parser
(`MiniJson.cs`) used only to parse that one field. Every other field uses `JsonUtility`.

## Requirements

- Unity 2021.3 LTS or later
- The `com.unity.modules.unitywebrequest`, `com.unity.modules.audio` and `com.unity.modules.animation` built-in modules (included by default in most projects)
- TextMeshPro, only if you import the `BasicDialogue` sample

## Testing

The voice code (`NpcSceneManager`, `WavUtility`, scene event parsing) compiles against
Unity 6000.5.6f1's engine assemblies, and its logic was checked against a running server:
speech encoded by `WavUtility` was transcribed correctly by the server, every scene event
parsed, and the NPC voice clips decode. Play Mode tests that run the whole loop
(`Tests/Runtime`, including an interruption + "go on" round trip) need a running server
(`scripts/run-voice-scene.sh`) and an activated Unity Editor.

This package's `MiniJson`/`NpcResponseParser` logic was verified by a batch-mode Unity
Editor run (27 assertions covering escaped strings, unicode escapes, malformed JSON,
multiple actions, and empty arrays — all passing) against Unity 6000.5.6f1. The Runtime
assembly was confirmed to compile cleanly (`Library/ScriptAssemblies/GameRagKit.Unity.dll`)
in a real Unity project resolving this package via a local `file:` dependency.
