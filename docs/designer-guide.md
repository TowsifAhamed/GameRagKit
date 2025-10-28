# Designer quickstart

This guide walks writers and quest designers through updating NPC lore without touching engine code.

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
