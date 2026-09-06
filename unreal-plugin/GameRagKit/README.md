# GameRagKit Unreal Plugin

An Unreal Engine plugin for talking to a GameRagKit server: ask NPCs questions, stream
responses with genuine incremental delivery for a typewriter effect, send structured
world state, and receive validated NPC actions — all Blueprint-callable.

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
