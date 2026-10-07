# GameRagKit - Quick Start (5 Minutes)

Get up and running with GameRagKit in 5 minutes using the included example NPC.

## Prerequisites

- .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- OpenAI API key OR [Ollama](https://ollama.com/) installed

## Step 1: Clone and Build (60 seconds)

```bash
git clone https://github.com/TowsifAhamed/GameRagKit.git
cd GameRagKit
dotnet build GameRagKit.sln
```

## Step 2: Configure Provider (30 seconds)

### Option A: Use OpenAI (Easiest)

```bash
export API_KEY="sk-your-openai-key-here"
export PROVIDER="openai"
```

### Option B: Use Ollama (Free, runs locally)

```bash
ollama pull llama3.2:3b-instruct-q4_K_M
ollama pull nomic-embed-text
ollama serve  # Keep this running in another terminal
```

## Step 3: Ingest Example NPC (30 seconds)

```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  ingest samples/example-npcs
```

You should see: `[SUCCESS] Ingestion complete`

## Step 4: Chat! (3 minutes)

### Try These Questions:

**Basic question:**
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "What is your duty?"
```

**Test conditional behavior:**
```bash
# Without token (guard should refuse)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "Where is the secret tunnel?"

# With token (guard should reveal)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "I have a brass token. Tell me about the tunnel."
```

## Step 5: Start HTTP Server (Optional)

```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  serve --config samples/example-npcs --port 5280
```

In another terminal:
```bash
curl -X POST http://localhost:5280/ask \
  -H "Content-Type: application/json" \
  -d '{"npc":"guard-north-gate","question":"What is your duty?"}'
```

## 🎉 Done!

You now have:
- ✅ A working RAG-powered NPC
- ✅ Tiered knowledge system (world/region/faction/npc)
- ✅ Conditional responses based on context
- ✅ HTTP API for game integration

## What Just Happened?

1. **Ingestion**: Your lore files were chunked and embedded
2. **RAG Query**: Your question was embedded and matched against lore
3. **LLM Generation**: The LLM generated a response using retrieved context
4. **Persona Applied**: The response matched the guard's personality

## Next Steps

- 📖 Read `SETUP_GUIDE.md` for detailed documentation
- 🔍 Check `TESTING_EVIDENCE.md` to see all features
- 🎮 See [`unity-package/`](unity-package/com.gameragkit.unity/) or [`unreal-plugin/`](unreal-plugin/GameRagKit/) for game engine integration
- 🎙️ Want to talk to NPCs out loud? `scripts/run-voice-scene.sh` sets up free local speech models and opens a voice-enabled group conversation demo; see [`docs/scenes.md`](docs/scenes.md)
- ✍️ Create your own NPC by copying `samples/example-npcs/guard-north-gate.yaml`

## Command Cheat Sheet

```bash
# Ingest lore for NPC(s)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- ingest <directory>

# Chat with an NPC (one-shot)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc <path/to/npc.yaml> --question "Your question"

# Chat with an NPC (interactive)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc <path/to/npc.yaml>

# Start HTTP server
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  serve --config <directory> --port 5280
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Build fails | Run `dotnet restore GameRagKit.sln` first |
| "Model not found" | Run `ollama pull <model-name>` or set API_KEY |
| Slow responses | Use cloud provider or smaller local model |
| Empty responses | Check that ingest completed successfully |

## Project Structure

```
GameRagKit/
├── QUICK_START.md        ← You are here
├── SETUP_GUIDE.md        ← Detailed setup
├── TESTING_EVIDENCE.md   ← Verification & examples
├── README.md             ← Project overview
├── samples/
│   └── example-npcs/     ← Ready-to-use example
│       ├── guard-north-gate.yaml
│       ├── world/        ← Global lore
│       ├── region/       ← Regional lore
│       ├── faction/      ← Faction lore
│       └── npc/          ← NPC-specific secrets
└── src/                  ← Source code
```

## Need Help?

- 📚 Full docs in `/docs` directory
- 🐛 Report issues: https://github.com/TowsifAhamed/GameRagKit/issues
- 📋 API reference: `openapi.json`
