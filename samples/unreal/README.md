# Unreal sample

Use the hosted mode when integrating with Unreal Engine:

1. Run `gamerag serve --config NPCs --port 5280` on a companion PC or dedicated host.
2. In Unreal, add a Blueprint function (or C++ helper) that POSTs `{ "npc": "guard-north-gate", "question": "..." }` to `http://localhost:5280/ask`.
3. Parse the JSON response and update your UI, VO, or behaviour trees with the `answer` text. Inspect `sources` during QA.
4. Optionally support streaming in the future via chunked transfer/SSE.

See `AskNpcLibrary.h/.cpp` for a native helper outline.
