# GameRAGKit

[![NuGet](https://img.shields.io/nuget/v/GameRagKit.svg)](https://www.nuget.org/packages/GameRagKit/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/GameRagKit.svg)](https://www.nuget.org/packages/GameRagKit/)
[![License](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue.svg)](LICENSE.md)

GameRAGKit is a drop-in retrieval augmented generation (RAG) toolkit for building non-player characters (NPCs) that can scale from solo prototypes to fully-fledged productions. It keeps the runtime lightweight enough for Unity or dedicated C# services, while still letting you route high-impact scenes to cloud LLMs on demand.

- **Engine agnostic.** Embed the library directly in Unity/other C# runtimes or host the bundled HTTP service for Unreal and everything else.
- **Provider agnostic.** Run Ollama locally, route to OpenAI/Azure/Gemini/Groq/OpenRouter/Mistral, or mix them with hybrid routing. See [Provider Compatibility](docs/2025-11-29/PROVIDER_COMPATIBILITY.md) for full details.
- **Designer friendly.** Personas live in YAML, lore lives in folders, and the CLI handles ingestion, chat smoke tests, and packaging.

> Dual-licensed: PolyForm Noncommercial 1.0.0 for community use with commercial terms available from the author.

## Donate (optional)

If you’d like to support development, you can donate via WiseTag:

- WiseTag: `@towsifa8`
- Donations are used to build AI-enhanced mods for existing games (bringing smarter NPC experiences) and to improve GameRAGKit.

![WiseTag donation QR (@towsifa8)](https://raw.githubusercontent.com/TowsifAhamed/GameRagKit/main/wisetag.png)

## Community

- Add on to the Discord channel: [https://discord.gg/CmYuMVWGZm](https://discord.gg/CmYuMVWGZm)

<p align="center">
  <iframe
    src="https://discord.com/widget?id=1453102384334241856&theme=dark"
    width="350"
    height="500"
    allowtransparency="true"
    frameborder="0"
    sandbox="allow-popups allow-popups-to-escape-sandbox allow-same-origin allow-scripts">
  </iframe>
</p>

![Discord member count](https://img.shields.io/discord/1453102384334241856?logo=discord&logoColor=white)

## Installation

### NuGet Package

Install the GameRagKit package from NuGet:

```bash
dotnet add package GameRagKit
```

Or via Package Manager Console:

```powershell
Install-Package GameRagKit
```

**Versioning:** Each push to `main` automatically publishes a new version with an auto-incremented patch number (e.g., 0.1.1, 0.1.2, etc.). To publish a specific version, push a tag like `v0.2.0` which will publish exactly as `0.2.0`.

### From Source

Clone the repository and build from source:

```bash
git clone https://github.com/TowsifAhamed/GameRagKit.git
cd GameRagKit
dotnet build
```

## Repository Structure

```
GameRagKit/
├── src/                    # Source code
│   ├── GameRagKit/        # Core library (config, vector store, routing, providers)
│   └── GameRagKit.Cli/    # CLI tool (`gamerag` command)
│
├── tests/                  # Unit and integration tests
│   └── GameRagKit.Tests/  # Test suite for core library
│
├── samples/                # Integration examples
│   ├── unity/             # Unity integration guide and sample scripts
│   └── unreal/            # Unreal Engine integration (C++/Blueprint examples)
│
├── examples/               # Ready-to-use configurations
│   └── configs/           # Example NPC YAML files for all providers
│       ├── gemini-example.yaml      # Google Gemini (cloud-only)
│       ├── openai-example.yaml      # OpenAI (cloud-only)
│       ├── ollama-local-example.yaml # Ollama (fully offline)
│       ├── hybrid-example.yaml      # Smart routing (local + cloud)
│       └── README.md                # Complete configuration guide
│
├── docs/                   # Documentation
│   └── 2025-11-29/        # Timestamped documentation updates
│       ├── ISSUES_AND_IMPROVEMENTS.md  # Detailed issue analysis
│       ├── QUICK_ISSUE_SUMMARY.md      # Executive summary
│       ├── CHANGELOG_2025-11-29.md     # What changed
│       └── READY_TO_COMMIT.md          # Commit guide
│
├── docker-compose.yml      # Quick database setup (PostgreSQL/Qdrant)
├── .env.example           # Environment variable template
└── README.md              # This file
```

### Where to Find What

- **Getting Started?** → See [Quick Start](#getting-started) below
- **Configuration Examples?** → [`examples/configs/`](examples/configs/)
- **Provider Compatibility?** → [Provider Compatibility Guide](docs/2025-11-29/PROVIDER_COMPATIBILITY.md) - Which cloud providers are supported?
- **Unity Integration?** → [`samples/unity/`](samples/unity/)
- **Unreal Integration?** → [`samples/unreal/`](samples/unreal/)
- **Testing?** → [`tests/GameRagKit.Tests/`](tests/GameRagKit.Tests/)
- **Issue Reports & Improvements?** → [`docs/2025-11-29/`](docs/2025-11-29/)

## Quick Start

**New to GameRagKit?** Check out the [example configurations](examples/configs/) for ready-to-use setups:
- [Gemini (cloud)](examples/configs/gemini-example.yaml) - Latest Gemini 2.0/2.5 models
- [OpenAI (cloud)](examples/configs/openai-example.yaml) - Latest GPT-4.1 models
- [Ollama (local)](examples/configs/ollama-local-example.yaml) - Fully offline
- [Hybrid](examples/configs/hybrid-example.yaml) - Smart routing (local + cloud)

See the [complete configuration guide](examples/configs/README.md) for model details and setup instructions.

## GameRagKit in Action

Want to see GameRagKit in action? Check out the **[GameRagKit Demo](https://github.com/TowsifAhamed/gameragkit-demo)** - a working example application that demonstrates real-world NPC conversations with both cloud and local providers.

### Demo Screenshots

**OpenAI Cloud Provider Demo:**

![OpenAI Demo](https://raw.githubusercontent.com/TowsifAhamed/GameRagKit/main/docs/images/openai-demo.png)

**Ollama Local Provider Demo:**

![Ollama Demo](https://raw.githubusercontent.com/TowsifAhamed/GameRagKit/main/docs/images/ollama-demo.png)

Both demos showcase **"Bram the Blacksmith"** - comparing a basic script-only NPC vs a GameRagKit-powered smart NPC that:
- Provides contextually aware responses using RAG
- Maintains character consistency and medieval tone
- Retrieves relevant lore from knowledge bases
- Works with both cloud (OpenAI) and local (Ollama) providers

The demo repository includes:
- Ready-to-run C# console application
- Minecraft Paper server plugin integration
- Complete setup with sample NPCs and lore
- Examples for multiple AI providers (OpenAI, Gemini, Ollama, Anthropic)

The demo repository is a great way to quickly understand how GameRagKit works before integrating it into your own game.

## Release highlights

- **LLamaSharp in-process inference** lets you skip Ollama entirely, run fully offline inside your game server, and point `model_path`/`embed_model_path` at the GGUF files you own.
- **Token-by-token streaming** is live across SDKs and HTTP: use `NpcAgent.StreamAsync`, `/ask/stream` (SSE), or the CLI chat command to watch cinematic, partial responses roll in.
- **Pack builder CLI** (`gamerag pack`) plus the [shipping guide](docs/shipping-to-players.md) produce deployable Lore + `.gamerag` bundles for consoles or locked-down servers.
- **Runtime state snapshots** (`AskOptions.State`, `WriteSnapshot`) inject transient facts (pressures, morale, timers) so NPCs reason about the current run without polluting the persistent index.
- **Systems assistant example** (`examples/systems-assistant/`) shows supply-chain debugging in action, blending world/region/faction lore with live `RUNTIME STATE` for grounded, actionable answers.

## Systems Assistant ("Rage Chat")

Ship a drop-in "what broke?" assistant for supply chains, happiness loops, or any system-by-system debugging. Try the starter kit in [`examples/systems-assistant/`](examples/systems-assistant/) which includes world/region/faction lore, a live snapshot, and five canned questions.

1. `dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest examples/systems-assistant`
2. `dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- serve --config examples/systems-assistant`
3. POST `{"npc":"systems-guide","question":"Why are citizens angry about food?"}` to `/ask` or stream partial text via `/ask/stream`.

Answers blend world/region/faction lore with the current run's `RUNTIME STATE` so frustrated players get grounded, actionable fixes with citations.

## Getting started

### 1. Install requirements

- .NET 8 SDK
- Database: PostgreSQL 16+ OR Qdrant (use included [`docker-compose.yml`](docker-compose.yml))
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
    chat_model: gpt-4.1              # Latest GPT-4.1 (2025)
    embed_model: text-embedding-3-large  # 3072-dim embeddings
    endpoint: https://api.openai.com/
```

### 3. Set runtime variables (optional)

```
# Local defaults
export OLLAMA_HOST=http://127.0.0.1:11434

# LLamaSharp in-process (no HTTP)
# (Run inside your game server without Ollama; paths point to your .gguf files.)
# Requires the LLamaSharp CPU (or CUDA) backend and a model file on disk.
local:
  engine: llamasharp
  model_path: models/llama-3.2-1b-instruct-q4_K_M.gguf
  embed_model_path: models/nomic-embed-text-v1.5.f16.gguf # optional; defaults to model_path
  context_size: 4096
  embedding_context_size: 1024
  gpu_layer_count: 0        # bump if you ship a CUDA backend
  threads: 8                # optional override; defaults to environment core count
  batch_size: 512
  micro_batch_size: 512
  max_tokens: 256

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

var reply = await npc.AskAsync(
    "Where is the master key?",
    new AskOptions(Importance: 0.8, State: "RunState: gate closed, patrol shift B waiting"));
SubtitleUI.Show(reply.Text);
```

`NpcAgent` also supports:

- `StreamAsync` for token-by-token streaming (HTTP `/ask/stream` mirrors this via SSE).
- `RememberAsync` to append runtime memories into the NPC-specific index.
- `WriteSnapshot(key, state, ttl)` to inject short-lived run state (e.g., supply or morale numbers) without persisting it.

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
| `gamerag pack <dir> [--output <file>]` | Produce a deployable bundle (configs + lore + `.gamerag` indexes). |

## Documentation & Examples

### Configuration Examples
- **[examples/configs/](examples/configs/)** - Complete example configurations for all providers
  - Gemini 2.0/2.5 (latest models, 2025)
  - OpenAI GPT-4.1 (latest models, 2025)
  - Ollama local setup (fully offline)
  - Hybrid routing examples

### Integration Samples
- **[samples/unity/](samples/unity/)** - Unity integration guide and C# scripts
- **[samples/unreal/](samples/unreal/)** - Unreal Engine integration (C++/Blueprint examples)

### Documentation
- **[docs/2025-11-29/](docs/2025-11-29/)** - Latest updates and issue reports
  - [PROVIDER_COMPATIBILITY.md](docs/2025-11-29/PROVIDER_COMPATIBILITY.md) - Which cloud providers are supported?
  - [ISSUES_AND_IMPROVEMENTS.md](docs/2025-11-29/ISSUES_AND_IMPROVEMENTS.md) - Detailed analysis and recommendations
  - [QUICK_ISSUE_SUMMARY.md](docs/2025-11-29/QUICK_ISSUE_SUMMARY.md) - Executive summary
  - [CHANGELOG_2025-11-29.md](docs/2025-11-29/CHANGELOG_2025-11-29.md) - Recent changes
- **[docs/shipping-to-players.md](docs/shipping-to-players.md)** - Build and ship packed indexes without re-ingesting.

### Developer Resources
- **[.env.example](.env.example)** - Environment variable template with all providers
- **[docker-compose.yml](docker-compose.yml)** - One-command database setup

## Roadmap

- Advanced router strategies (latency, budget, dynamic confidence) that balance responsiveness with cloud spend.
- In-memory vector store option for ultra-fast prototyping and scenarios that skip external databases.
- More actionable diagnostics around YAML/provider configuration so errors directly point to the offending section.
- Expanded integration tests (systems assistant, streaming, pack builder) to guard regressions across providers and runtimes.

## License

PolyForm Noncommercial 1.0.0 (community use) + commercial license – see [LICENSE.md](LICENSE.md).

Commercial licensing: email `towsif.kuet.ac.bd@gmail.com` (we keep terms lightweight and primarily ask for recognition/attribution).
