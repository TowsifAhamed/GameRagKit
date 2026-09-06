# NPC mood (persistent emotion tracking)

NPCs can track a persistent mood that carries across conversations — an NPC the player
angered stays wary or hostile in later interactions, not just within a single exchange.
Mood is self-reported by the model itself (the same model already generating the NPC's
dialogue), not computed by a separate sentiment classifier or keyword heuristic, since the
model already understands context, tone, and sarcasm far better than a lexicon-based
heuristic would.

## Enabling mood tracking

Mood tracking is opt-in per NPC, like actions. Add to the NPC's YAML:

```yaml
persona:
  id: guard-north-gate
  system_prompt: You are Jake, the North Gate guard...
  mood_tracking: true
```

An NPC without `mood_tracking: true` behaves exactly as before — no mood instructions in
the prompt, no mood file written, no `mood` field in responses.

## How it works

1. When enabled, GameRagKit loads the NPC's current persisted mood (or "neutral" if none
   exists yet) and tells the model in the system prompt.
2. The model may optionally report a mood shift using the same structured-block mechanism
   as actions:

   ```
   I don't take kindly to threats.
   [[gamerag:mood]]
   {"value":"hostile","intensity":0.8}
   [[/gamerag]]
   ```

3. GameRagKit strips the block from the player-visible reply (the player only sees "I
   don't take kindly to threats."), validates it, and persists the new mood to a small
   JSON file scoped to that NPC. The mood survives server restarts.
4. Both `/ask` and `/ask/stream` return the NPC's mood after the reply — `mood` in the
   `/ask` JSON response, and on the `end` SSE event for `/ask/stream`:

   ```json
   { "answer": "I don't take kindly to threats.", "mood": { "value": "hostile", "intensity": 0.8 }, ... }
   ```

   `mood` is `null` if `mood_tracking` isn't enabled, or if the model hasn't reported a
   mood shift yet (still "neutral", not yet reflected in a response payload until the
   first mood block is emitted — though the system prompt always tells the model its
   current mood, including "neutral").

The game client can use `mood.value`/`mood.intensity` to drive animation, voice tone
(e.g. picking a different Piper voice or TTS parameters), or dialogue UI (a mood icon).

## The `[[gamerag:tag]]` block format

Actions and mood share one wire format:

```
[[gamerag:<tag>]]
{ ...json... }
[[/gamerag]]
```

This is deliberately not a markdown code fence (`` ```tag ``), which would collide with
ordinary code blocks a model might legitimately include in a reply (e.g. an NPC explaining
a rune formula). `[[gamerag:...]]` is a marker no model has any independent reason to
produce for any other purpose. `GameRagBlockParser` extracts all such blocks from a reply
in one pass; `ActionParser` and `MoodParser` each validate the blocks tagged for them.

> **Migration note:** actions previously used ` ```action ` as their marker (Phase 1 of
> this toolkit's development). That format has been replaced by `[[gamerag:action]]` —
> if you have NPC prompts or fine-tuned models referencing the old format, they need
> updating. The action JSON schema itself (`name`/`args`) is unchanged.

## Design notes

- Only one mood value is tracked per NPC at a time (not a per-player mood) — an NPC's
  mood is a property of the NPC, not of any specific conversation. If multiple players
  interact with the same NPC concurrently, the last mood update wins.
- If the model emits multiple mood blocks in one reply (it's asked not to, but nothing
  enforces it), the last valid one is used.
- `intensity` is clamped to `0.0`-`1.0`; omitted intensity defaults to `0.5`.
- Mood is stored in `.gamerag/mood/<npc-id>.json` under the NPC's config directory — the
  same storage root used for vector index manifests, but its own separate file, since
  mood is a small direct-read/write value, not something that benefits from vector search.
