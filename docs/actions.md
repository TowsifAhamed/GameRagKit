# NPC actions (tool-calling)

NPCs can be given a whitelist of game actions they're allowed to invoke — give an item,
start a quest, update reputation — without depending on any provider's native
function-calling API. The same mechanism works identically across every provider
GameRagKit supports (OpenAI, Azure, Gemini, Groq, OpenRouter, Mistral, Ollama, LLamaSharp)
because it's implemented entirely in GameRagKit's own prompt/parse layer, not delegated
to the model provider.

## How it works

1. If an NPC's YAML declares `persona.actions`, GameRagKit appends an action catalogue to
   the system prompt describing each action's name, args, and types, plus the exact
   fenced-block format to use.
2. The model's raw reply may include a block like:

   ```
   Ah, you've proven yourself.
   ```action
   {"name":"give_item","args":{"item_id":"brass_token","quantity":1}}
   ```
   Take this token and guard it well.
   ```

3. GameRagKit parses every `` ```action `` block out of the reply, validates the action
   name against the NPC's declared whitelist and required args, drops any undeclared
   args, and strips the block from the player-visible text.
4. The cleaned text and the validated action calls are returned separately:
   - HTTP `/ask` response gains an `actions` array: `[{ "name": "give_item", "args": {...} }]`.
   - The game client is responsible for executing the action (giving the item, starting
     the quest) — GameRagKit only validates and reports the call, it never executes
     gameplay logic itself.

Malformed JSON inside an action block, or a call to an action not in the whitelist, is
recorded as a parse error and the block is still stripped from the reply so players never
see raw JSON — but the action is not returned, so the game client should treat a missing
action as "the NPC didn't do anything" rather than erroring.

## Declaring actions in YAML

```yaml
persona:
  id: guard-north-gate
  system_prompt: You are Jake, the North Gate guard...
  actions:
    - name: give_item
      description: Gives an item to the player
      args:
        - name: item_id
          type: string
          required: true
        - name: quantity
          type: number
          required: false
    - name: start_quest
      description: Starts a quest for the player
      args:
        - name: quest_id
          type: string
```

Supported arg `type` values: `string`, `number`, `boolean`. These are descriptive hints
for the model's prompt — GameRagKit does not coerce or type-check argument values beyond
presence, since the game client typically parses/validates the final values itself.

## Design notes

- Actions are optional — an NPC with no `actions` declared behaves exactly as before,
  with zero prompt overhead.
- The parser only trusts action names and args declared in `persona.actions`; anything
  else the model emits (unknown action names, extra args) is silently dropped, never
  passed through to the game client.
- Because parsing happens after the full response is returned, actions are only
  available on the non-streaming `/ask` path today; `/ask/stream` streams raw tokens and
  does not yet parse actions out of the stream.
