# Shipping GameRAGKit bundles to players

## Build a pack once
1. Ingest your configs so `.gamerag/indexes` exist:
   ```bash
   dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest NPCs
   ```
2. Create a bundle that includes configs, lore, manifests, and tiered indexes:
   ```bash
   dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- pack NPCs --output gamerag-pack.zip
   ```

The generated `pack.json` captures chunking, models, embedding dimensions, and which indexes are inside.

## Load without re-ingesting
- Unzip the bundle alongside your game build (keep the `.gamerag` folder intact).
- Call `GameRAGKit.Load(...)` and `EnsureIndexAsync()`; manifests prevent duplicate ingestion and reuse the packed indexes.
- Use `WriteSnapshot(...)` for per-run state so you avoid mutating the shipped bundle.

## Update cadence
- Re-run `ingest` and `pack` whenever designers change lore or you ship a new build.
- If you stream live data, keep it ephemeral via snapshots so you don't need new packs between patches.
