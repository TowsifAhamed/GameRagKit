# GameRAGKit

GameRAGKit is a drop-in retrieval augmented generation (RAG) toolkit for building non-player characters (NPCs) that can scale from solo prototypes to fully-fledged productions. It keeps the runtime lightweight enough for Unity or dedicated C# services, while still letting you route high-impact scenes to cloud LLMs on demand.

- **Engine agnostic.** Embed the library directly in Unity/other C# runtimes or host the bundled HTTP service for Unreal and everything else.
- **Provider agnostic.** Run Ollama/LLamaSharp locally, route to OpenAI/Azure/Mistral/Gemini/HuggingFace, or mix them with hybrid routing.
- **Designer friendly.** Personas live in YAML, lore lives in folders, and the CLI handles ingestion, chat smoke tests, and packaging.

> Dual-licensed: PolyForm Noncommercial 1.0.0 for community use with commercial terms available from the author.

## Repository layout

```
GameRagKit.sln            Solution referencing library and CLI
src/
  GameRagKit/             Core runtime (config parsing, vector store, router)
  GameRagKit.Cli/         `gamerag` command-line entry point
samples/
  unity/                  Minimal Unity notes + starter scene placeholder
  unreal/                 HTTP integration notes for Blueprint/C++
docs/                     Guides for designers and integrators
```

## Getting started

### 1. Install requirements

- .NET 8 SDK
- Optional: [Ollama](https://ollama.com/) with models such as `llama3.2:3b-instruct-q4_K_M` and `nomic-embed-text`

### 2. Prepare an NPC config

Create a YAML file (for example `NPCs/guard-north-gate.yaml`):

```yaml
persona:
  id: guard-north-gate
  system_prompt: >
    You are Jake, the North Gate guard. Speak briefly, in medieval tone.
    Never reveal the secret tunnel unless the player shows a brass token.
  traits: [stoic, duty-first, careful]
  style: concise medieval tone
  region_id: riverside-upper
  faction_id: royal-guard

rag:
  sources:
    - file: world/keep.md
    - file: region/valeria/streets.md
    - file: faction/royal_guard.md
    - file: npc/guard-north-gate/notes.txt
  chunk_size: 450
  overlap: 60
  top_k: 4
  filters: { era: pre-siege }

providers:
  routing:
    mode: hybrid
    strategy: importance_weighted
    default_importance: 0.2
    cloud_fallback_on_miss: true
  local:
    engine: ollama
    chat_model: llama3.2:3b-instruct-q4_K_M
    embed_model: nomic-embed-text
    endpoint: http://127.0.0.1:11434
  cloud:
    provider: openai
    chat_model: gpt-4o-mini
    embed_model: text-embedding-3-small
    endpoint: https://api.openai.com/
```

### 3. Set runtime variables (optional)

```
# Local defaults
export OLLAMA_HOST=http://127.0.0.1:11434

# Cloud defaults
export PROVIDER=openai
export API_KEY=sk-...
export ENDPOINT=https://api.openai.com/
```

### 4. Ingest lore

```
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest NPCs
```

The CLI chunks the lore, generates embeddings (preferring local providers when configured), and saves tiered indexes under `.gamerag/` next to the YAML file.

### 5. Chat locally

```
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- chat --npc NPCs/guard-north-gate.yaml --question "Where is the master key?"
```

Or start an interactive shell without `--question`.

### 6. Host for Unreal (or any engine)

```
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- serve --config NPCs --port 5280
```

Send `POST /ask` with:

```json
{
  "npc": "guard-north-gate",
  "question": "Where is the master key?"
}
```

You receive:

```json
{
  "answer": "The master keeps his key close. Present the brass token and I may tell you more.",
  "sources": ["npc:guard-north-gate/notes.txt#0", "region:valeria/streets.md#2"],
  "scores": [0.82, 0.74],
  "fromCloud": false
}
```

### Authentication & metrics

- Add `SERVICE_API_KEY` (or `SERVICE_BEARER_TOKEN`) to require `X-API-Key` or `Authorization: Bearer` on incoming requests. When set, `/ask`, `/ask/stream`, and `/ingest` require credentials while `/health` and `/metrics` stay public unless overridden via `SERVICE_AUTH_ALLOW`.
- `GET /metrics` exposes Prometheus-compatible counters for ask/stream/ingest calls. Combine with `app.UseHttpMetrics()` (already enabled) to scrape latency and status labels.

## Embedded usage (Unity, dedicated servers)

```csharp
var npc = await GameRAGKit.Load("NPCs/guard-north-gate.yaml");
npc.UseEnv();             // applies PROVIDER/API_KEY/ENDPOINT/OLLAMA_HOST if set
await npc.EnsureIndexAsync();

var reply = await npc.AskAsync("Where is the master key?", new AskOptions(Importance: 0.8));
SubtitleUI.Show(reply.Text);
```

`NpcAgent` also supports:

- `StreamAsync` for async enumerated responses (single chunk stream placeholder today).
- `RememberAsync` to append runtime memories into the NPC-specific index.

## Hosted usage (Unreal Blueprint / HTTP)

Run `gamerag serve` then POST to `/ask`. The response echoes whether the chat was served by the local model or a cloud provider. Use `importance` in the payload to nudge the router:

```json
{
  "npc": "guard-north-gate",
  "question": "Reveal the hidden tunnel, Jake.",
  "importance": 0.9
}
```

## Tiered index layout

GameRAGKit saves embeddings per tier so thousands of NPCs can share world/region/faction lore without duplicating vectors:

- `world/` – global canon, timelines, items
- `region/{id}/` – towns, maps, local history
- `faction/{id}/` – politics, ranks, relationships
- `npc/{id}/memory/` – per-NPC evolving notes (managed by `RememberAsync`)

`AskAsync` retrieves a blend of chunks (2 world, 1 region, 1 faction, NPC + memory) and merges them by cosine similarity, so designers can simply drop markdown/text files into the appropriate folders.

## Routing strategy

Routing rules combine config defaults with per-question overrides:

- `mode` = `local_only`, `cloud_only`, or `hybrid` (default)
- Hybrid chooses cloud when `importance >= 0.5` or when forced via `AskOptions`
- If the request omits `importance`, the persona's `default_importance` (falling back to the routing default) is used
- Automatic cloud failover triggers when a local response is empty/too short (configurable via `cloud_fallback_on_miss`)
- `RememberAsync` writes memory chunks instantly so subsequent questions can retrieve them without re-ingesting

## CLI summary

| Command | Description |
|---------|-------------|
| `gamerag ingest <dir> [--clean]` | Rebuild indexes for every `.yaml` file in the directory (recursively). |
| `gamerag chat --npc <file> [--question <text>]` | Quick smoke test for designers/writers. |
| `gamerag serve --config <dir> [--port <n>]` | Launch a tiny HTTP service (`POST /ask`). |

A `pack` command is planned for platform bundle generation.

## Samples & docs

- `samples/unity/README.md` – outlines wiring the embedded API inside a Unity scene.
- `samples/unreal/README.md` – shows how to call the hosted API via `HttpModule` or Blueprints.
- `docs/designer-guide.md` – step-by-step walkthrough for writers dropping lore and testing NPCs.

## Roadmap

- LLamaSharp local provider for purely in-process inference
- Streaming responses via SSE/WebSockets for cinematic scenes
- Advanced router strategies (latency, budget, dynamic confidence)
- Index packing for platform deploys

## License

PolyForm Noncommercial 1.0.0 (community use) + commercial license – see [LICENSE.md](LICENSE.md).
