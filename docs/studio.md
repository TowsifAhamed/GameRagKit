# GameRagKit Studio (local designer UI)

`gamerag studio` launches a local web UI for browsing NPCs, editing a handful of persona
fields, and testing dialogue in-browser — without hand-editing YAML or using curl/Postman.

## Running it

```bash
dotnet run --project src/GameRagKit.Cli -- studio --config ./NPCs --port 5290
```

Open `http://localhost:5290`. Every `.yaml` file under `--config` (searched recursively,
same as `gamerag serve`) becomes a node on the canvas.

## What it does

- **NPC canvas** — each NPC is a node on a pan/zoom/drag WebGL canvas (rendered with
  Three.js). Scroll to zoom, drag the background to pan, drag a node to reposition it,
  click a node to open its edit panel.
- **Persona editor** — the side panel exposes `system_prompt`, `style`, and
  `mood_tracking` as form fields. Saving writes the change back to the NPC's actual YAML
  file on disk.
- **Chat tester** — a chat panel calls the real `/ask` endpoint against the NPC being
  edited, so you can try dialogue immediately using the same RAG/routing/actions/mood
  pipeline a real game client would hit.
- **Live hot-reload** — saving a persona edit immediately reloads that NPC's live agent
  (re-reads its YAML, re-indexes if lore sources changed) so the next chat-test message
  uses the updated persona — no server restart needed.

## What it doesn't do (yet)

- Only `system_prompt`, `style`, and `mood_tracking` are editable from the form. Actions,
  traits, routing/provider settings, and lore files are not exposed in the UI yet — edit
  those fields directly in the YAML file as before.
- No streaming in the chat tester — it calls `/ask` (non-streaming), not `/ask/stream`.
- No voice testing (`/ask/voice`) in the chat panel.
- Single-user, no auth by default — same as `gamerag serve`, set `SERVICE_API_KEY` if you
  need to restrict access (studio's `/studio/api/*` routes are protected by the same
  `ApiAuthenticationMiddleware` as `/ask`).

## Why editing preserves your YAML formatting

Studio doesn't re-serialize the whole YAML file through a generic parser when you save —
that would silently strip any comments you'd written and reformat key ordering/spacing.
Instead, `PersonaYamlPatcher` edits only the specific lines for the fields you changed,
leaving everything else in the file — comments, unrelated sections, your own formatting —
byte-for-byte untouched. It recognizes the field shapes those three fields actually use in
practice (plain scalars, YAML folded/literal block scalars for `system_prompt`, boolean
literals for `mood_tracking`). If a field's on-disk shape doesn't match a pattern it
recognizes, the save is rejected with an error rather than guessing and risking a
malformed file — your file is left completely unchanged in that case.

## Architecture note

The studio backend is a normal ASP.NET Core host (`WebApplication.CreateBuilder()`, not
the trimmed `CreateSlimBuilder()` `gamerag serve` uses, since the studio needs to serve
static HTML/CSS/JS). It shares the same `AgentRegistry`/`/ask` controller as `gamerag
serve` — the chat tester isn't a separate reimplementation, it's the real endpoint. A new
`AgentRegistry.ReplaceAgentAsync` method supports swapping in a freshly-reloaded NPC agent
after a save without restarting the process; the previous agent instance is disposed after
a short grace period rather than immediately, so a request already in flight against it
isn't disrupted.
