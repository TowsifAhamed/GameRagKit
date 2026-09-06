# World state (structured perception input)

Game clients can pass structured facts about the current game world — nearby entities,
player inventory, time of day, combat status — instead of hand-formatting a free-text
blob. GameRagKit renders these deterministically into the prompt, so identical world
state always produces identical prompt text regardless of which engine or code path
constructed it.

This isn't computer vision or true perception — it's the same trick real shipped NPC
systems use: the game already knows what's nearby, in inventory, etc., and just needs to
tell the NPC in a consistent format.

## Sending world state

Include a `worldState` object under `options` on `POST /ask` or `POST /ask/stream`:

```json
{
  "npc": "guard-north-gate",
  "question": "What's going on?",
  "options": {
    "worldState": {
      "timeOfDay": "night",
      "inCombat": true,
      "nearbyEntities": [
        { "id": "goblin-1", "type": "goblin", "distanceMeters": 4.2 },
        { "id": "chest-1" }
      ],
      "playerInventory": [
        { "itemId": "brass_token", "quantity": 1 },
        { "itemId": "torch" }
      ],
      "custom": { "weather": "raining" }
    }
  }
}
```

All fields are optional. `custom` is an open string/string dictionary for anything not
covered by the built-in fields — games have very different state shapes, so GameRagKit
doesn't try to force a rigid schema for everything.

This produces the following prompt block, appended before retrieved lore/sources:

```
WORLD STATE:
Time of day: night
Player is currently in combat.
Nearby: goblin-1 (goblin, 4.2m away); chest-1
Player inventory: brass_token x1, torch x1
weather: raining
---
```

Field order in the rendered block is fixed (time of day, combat, nearby entities,
inventory, then custom fields in the order given) so the same world state always
produces byte-identical prompt text.

## Relationship to `state` (freeform text)

`AskOptions`/`options` also has an older `state` string field for arbitrary freeform text,
which is still supported unchanged. `worldState` is additive, not a replacement — use
`worldState` for structured facts you want rendered consistently, and `state` for
anything that doesn't fit the structured shape (or during migration). Both can be set on
the same request; `worldState` renders first, `state` second.

## Supported on both `/ask` and `/ask/stream`

Both endpoints share the same context-building step, so `worldState` works identically on
the non-streaming and streaming paths with no additional configuration.
