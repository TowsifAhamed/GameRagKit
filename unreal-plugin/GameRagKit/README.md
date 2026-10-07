# GameRagKit Unreal Plugin

An Unreal Engine plugin for talking to a GameRagKit server: ask NPCs questions, stream
responses with genuine incremental delivery for a typewriter effect, send structured
world state, receive validated NPC actions, and hold voice conversations with one or more
NPCs — all Blueprint-callable.

This plugin is an HTTP client only — it does not embed the GameRagKit RAG/LLM runtime.
Run a GameRagKit server separately (`gamerag serve`) and point this plugin at it.

## Install

Copy the `GameRagKit/` folder into your project's `Plugins/` directory, then enable it
in Edit → Plugins → AI → GameRagKit.

## Quick start (Blueprint)

1. Add an `NpcDialogueComponent` to an Actor.
2. Set `Server Url` (e.g. `http://localhost:5280`).
3. Call `Ask Npc` or `Ask Npc Streaming`, and bind to `On Response Received` /
   `On Text Chunk Received` / `On Stream Complete` / `On Error`.

## Quick start (C++)

```cpp
DialogueComponent->OnResponseReceived.AddDynamic(this, &AMyNpc::HandleResponse);
DialogueComponent->AskNpc(TEXT("guard-north-gate"), TEXT("What is your duty?"), 0.3f);
```

For streaming with real incremental delivery:

```cpp
DialogueComponent->OnTextChunkReceived.AddDynamic(this, &AMyNpc::OnChunk);
DialogueComponent->OnStreamComplete.AddDynamic(this, &AMyNpc::OnStreamDone);
DialogueComponent->AskNpcStreaming(TEXT("storyteller"), TEXT("Tell me the legend"), 0.5f);
```

## Voice conversations (one or more NPCs)

`UNpcSceneComponent` holds spoken conversations through the server's `/scene` API (see
[`docs/scenes.md`](../../docs/scenes.md)): the player holds a key and talks (or types), the
server decides which NPC(s) should answer, and each reply plays in that NPC's own voice,
attached to the NPC actor. NPCs can answer each other, and the player can talk over them:
only the words actually heard are remembered, the NPC keeps what it didn't get to say, and
"go on" hands it the floor back. One participant works too.

Blueprint:
1. Add an `NpcSceneComponent` to your player (or a "conversation" actor), set `Server Url`.
2. Fill `Participants`: `Npc Id`, `Display Name` (how people address them, e.g. "Mira"),
   `Actor` (the NPC in the level), optionally `Attenuation` and `Signature Gesture`.
3. Bind a key: **Pressed** → `Start Listening`, **Released** → `Stop Listening And Send`.
   For typed input call `Say`.
4. Bind `On Line Started` (subtitle, gesture, actions), `On Line Finished`
   (`bInterrupted`, `Said`, `Unsaid`), `On Transcript`, `On Routed`, `On Scene Error`.

C++:

```cpp
Scene->OnLineStarted.AddDynamic(this, &AMyPlayer::HandleLine); // Line.Text, Line.Gesture, Line.Actions, Line.Mood
Scene->Say(TEXT("Mira, does Bram owe you money?"));
// push-to-talk
Scene->StartListening();      // key down (also cuts the NPCs off)
Scene->StopListeningAndSend(); // key up
```

- **Voices** come from the server (Kokoro or Piper per NPC) and play attached to the NPC
  actor with `VoiceAttenuation` (or the participant's own), so they're positional.
  Without server voices, lines still arrive and stay up for their reading time.
- **Microphone input** uses the engine's AudioCapture plugin (enabled automatically) and
  needs speech-to-text on the server (whisper.cpp). Have your own capture or recognizer?
  Call `Send Speech` with WAV bytes or `Say` with text. On macOS, add
  `NSMicrophoneUsageDescription` to your packaged app's Info.plist.
- **Body language**: with `bTurnToFace`, the speaker turns to whoever it's addressing and
  the others turn to watch it. `Line.Gesture` suggests a montage (`Interact`, `Cheer`,
  `Block`, `Use_Item`, or the participant's `Signature Gesture`), and
  `Get Speech Level(NpcId)` gives the 0..1 loudness of the current line for a jaw bone or
  head-nod blend in the Anim Blueprint.
- **Interruptions**: `Interrupt` (also called by `Start Listening` and `Say`) stops the
  voice mid-line; `Get History` holds only what was heard.

## NPC actions

If an NPC's YAML declares `persona.actions` (see `docs/actions.md` in the main repo),
validated action calls come back as an `FNpcAction` array — on `FNpcResponse::Actions`
for `AskNpc`, or as the second parameter of `OnStreamComplete` for `AskNpcStreaming`:

```cpp
void AMyNpc::OnStreamDone(const TArray<FString>& Sources, const TArray<FNpcAction>& Actions)
{
    for (const FNpcAction& Action : Actions)
    {
        if (Action.Name == TEXT("give_item"))
        {
            const FString ItemId = Action.Args.FindRef(TEXT("item_id"));
            // Game code executes the action here.
        }
    }
}
```

## Structured world state

Pass an `FNpcWorldState` to `AskNpc`/`AskNpcStreaming` — see `docs/world-state.md` in the
main repo for the full field reference. The `custom` open dictionary from the server's
`WorldStatePayload` isn't exposed on the Unreal side yet; use the built-in fields
(`TimeOfDay`, `bInCombat`, `NearbyEntities`, `PlayerInventory`) for now.

## Why streaming actually streams

`AskNpcStreaming` uses `IHttpRequest::SetResponseBodyReceiveStreamDelegateV2`, which fires
as response bytes arrive over the wire — not `Request->OnProcessRequestComplete()` alone,
which only fires once the entire response has been buffered. `OnTextChunkReceived` is
broadcast incrementally as complete SSE lines are parsed out of that stream, and
`OnStreamComplete` fires once at the very end with the accumulated sources/actions.

## What changed from the original sample scripts

The previous `samples/unreal/` scripts had two real bugs, found during this
restructuring and fixed here:

1. `AskNpcLibrary.cpp` called `IHttpRequest::ProcessRequest()` as if it synchronously
   returned an `FHttpResponsePtr`. Unreal's HTTP API is asynchronous — `ProcessRequest()`
   returns a `bool` (whether the request was successfully queued), and the response only
   arrives later via `OnProcessRequestComplete()`. This function could not have compiled
   as written. It has been dropped; `NpcDialogueComponent` covers the same functionality
   correctly.
2. Both old scripts read the entire SSE response body via `Response->GetContentAsString()`
   only after the whole request completed, so despite being named "streaming" they never
   delivered a real typewriter effect. Fixed via the incremental stream delegate above.
3. Neither old script sent the `X-GameRAG-Protocol` header the server's `/ask` and
   `/ask/stream` controllers require — every request would have been rejected with
   `400 Bad Request`. Fixed.

## Testing

This plugin was compiled against a real Unreal Engine 5.8 installation using
`RunUAT.sh BuildPlugin`, which builds the Editor, Development Game, and Shipping Game
targets for both Apple Silicon and Intel architectures. All targets compiled and linked
successfully (`BUILD SUCCESSFUL`); the packaged binary
(`Binaries/Mac/libUnrealEditor-GameRagKit.dylib`) was confirmed to exist. Two real
compile errors were caught and fixed during this process (an `FString` conversion
mismatch on `FJsonObject::Values` keys, and a missing `Dom/JsonValue.h` include that only
happened to compile before because of include order in the `.cpp` file). This was not
merely a manual code review — it is a genuine compiler-verified build.

The voice/scene component (`UNpcSceneComponent`) was built the same way (`BUILD
SUCCESSFUL` on UE 5.8) and is covered by automation tests in
`Source/GameRagKit/Private/Tests/` — `GameRagKit.Scene.SplitSpoken`, `.ChooseGesture`,
`.Wav` (16 kHz mono encode/decode) and `.EndToEnd`, which holds a real conversation with a
running server (`scripts/run-voice-scene.sh`, or set `GAMERAG_TEST_SERVER`), interrupts
the NPC mid-line and checks that only the heard words are kept. Run them headless with
`UnrealEditor-Cmd <project> -ExecCmds="Automation RunTests GameRagKit.Scene; Quit" -nullrhi -unattended`.
