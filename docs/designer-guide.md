# Designer quickstart

This guide walks writers and quest designers through updating NPC lore without touching engine code.

> **Prefer a UI over hand-editing YAML and the terminal?** `gamerag studio` gives you a
> browser-based NPC canvas, a form for `system_prompt`/`style`/`mood_tracking`, and an
> in-browser chat tester — see [`docs/studio.md`](studio.md). Steps 3 and 5 below are what
> it replaces; lore file editing (step 2) and rebuilding indexes (step 4) still happen the
> same way either way.

## 1. Check out the repo

Ask your engineer for the `NPCs` folder. Every NPC has a YAML file plus lore sources.

```
NPCs/
  guard-north-gate.yaml
  Lore/
    keep.md
  Dialogues/
    guard_clues.txt
  Factions/
    royal_guard.md
```

## 2. Edit lore files

Open any `.md` or `.txt` file and update the canon. Keep sentences short, avoid giant paragraphs, and be explicit with names.

Tip: add metadata by placing files under `world/`, `region/<id>/`, `faction/<id>/`, or `npc/<id>/memory/` so GameRAGKit can weight them correctly.

## 3. Update persona traits (optional)

`guard-north-gate.yaml` controls tone, mannerisms, and router defaults. Tweak `system_prompt`, `traits`, or `style`. Set `default_importance` higher for boss scenes so cloud models are used more often.

Want the NPC to give items, start quests, or take other gameplay actions during dialogue? See [`docs/actions.md`](actions.md) for the `persona.actions` YAML format.

Want players to speak to the NPC and hear it reply out loud? See [`docs/voice.md`](voice.md) for the `providers.voice` YAML format and the `/ask/voice` endpoint.

Want the NPC to remember how it feels about the player across conversations? See [`docs/mood.md`](mood.md) for `persona.mood_tracking`.

Want the game to tell the NPC what's nearby, what's in the player's inventory, or the time of day? See [`docs/world-state.md`](world-state.md) — this is set per-request by the game client, not in the NPC's YAML.

## 4. Rebuild the index

Run the CLI after saving lore:

```
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest NPCs
```

Use `--clean` if you need to force a full rebuild.

## 5. Smoke test dialogue

```
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- chat --npc NPCs/guard-north-gate.yaml
```

Type a few player questions. The CLI prints the NPC response, the vector sources, and whether the answer came from local or cloud routing.

## 6. Hand off to engineers

Commit the updated lore and YAML. The runtime uses the same indexes that the CLI generated, so there is no additional work before playtests.
