# Systems Assistant ("Rage Chat") starter kit

Drop this folder into `gamerag serve --config examples/systems-assistant` and players can ask:

- "Why are citizens angry about food?"
- "What broke the port deliveries?"
- "How did the guild react to the strike?"
- "What can I fix right now to stabilize supply?"
- "Summarize the current run's blockers in two bullets."

## Layout
- `world/` – evergreen design notes about supply, happiness, and penalties
- `region/port-rush/` – map-level state such as blocked docks
- `faction/logistics-guild/` – what NPC operators care about
- `npc/systems-guide/` – persona config + per-run snapshot (written to `memory/`)

## Expected answer style
- Lead with the concrete blocker
- Point to the active run data (`RUNTIME STATE` + memory) first
- Cite world or region rules to justify the explanation
- Offer a terse, actionable fix

## Try it
```bash
# 1) Ingest
 dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest examples/systems-assistant

# 2) Ask over HTTP
 curl -X POST http://localhost:5280/ask \
   -H "Content-Type: application/json" \
   -H "X-GameRAG-Protocol: 1" \
   -d '{"npc":"systems-guide","question":"Why are citizens angry about food?"}'
```

Use `/ask/stream` with the same payload for SSE streaming chunks, or use the C# `StreamAsync` API to render partial text in-engine.
