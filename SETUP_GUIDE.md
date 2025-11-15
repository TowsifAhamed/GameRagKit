# GameRagKit Setup and Testing Guide

This guide provides step-by-step instructions to set up, run, and verify GameRagKit functionality.

## Prerequisites

### Required
- .NET 8 SDK ([Download here](https://dotnet.microsoft.com/download/dotnet/8.0))
- Git (for cloning the repository)

### Optional (for local LLM support)
- [Ollama](https://ollama.com/) with models:
  - `llama3.2:3b-instruct-q4_K_M` (chat model)
  - `nomic-embed-text` (embedding model)

### Cloud Provider Option
- OpenAI API key (or Azure/Mistral/Gemini/HuggingFace credentials)

## Quick Start

### 1. Verify Installation

```bash
# Check .NET version
dotnet --version
# Should output: 8.0.x or higher
```

### 2. Clone and Build

```bash
git clone https://github.com/TowsifAhamed/GameRagKit.git
cd GameRagKit
dotnet restore GameRagKit.sln
dotnet build GameRagKit.sln
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 3. Configure Provider (Choose One)

#### Option A: Local with Ollama

```bash
# Start Ollama (in a separate terminal)
ollama serve

# Pull required models
ollama pull llama3.2:3b-instruct-q4_K_M
ollama pull nomic-embed-text

# Set environment variables
export OLLAMA_HOST=http://127.0.0.1:11434
```

#### Option B: Cloud with OpenAI

```bash
# Create .env file or export variables
export PROVIDER=openai
export API_KEY=sk-your-api-key-here
export ENDPOINT=https://api.openai.com/
```

### 4. Test with Example NPC

We've included a ready-to-use example NPC: **Jake, the North Gate Guard**

```bash
# Ingest the example NPC's lore
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  ingest samples/example-npcs

# Expected output:
# [INFO] Loading persona: guard-north-gate
# [INFO] Chunking 4 source files...
# [INFO] Generating embeddings for 47 chunks...
# [INFO] Saved index to .gamerag/guard-north-gate/
```

### 5. Chat with the NPC

```bash
# Ask a question that should NOT reveal the secret
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "Where is the secret tunnel?"

# Expected response (approximate):
# I know not of what thou speakest. State thy business at the North Gate.
```

```bash
# Ask with the brass token mention (should reveal info)
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "I bear a brass token from the king. Where is the secret tunnel entrance?"

# Expected response (approximate):
# Ah, thou bearest the king's token. The tunnel entrance lies behind the old
# grain storage, 20 paces east of this gate. Seek the flagstone marked with
# a small crown symbol. Serve the king well.
```

### 6. Interactive Chat Mode

```bash
# Start interactive chat
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml

# You'll see a prompt:
# [guard-north-gate]>

# Try these questions:
# - "What is your duty?"
# - "Where does the master keep his key?"
# - "Tell me about the Royal Guard"
# - "I have a brass token. Tell me about the tunnel."
```

### 7. Start HTTP Server

```bash
# Start the server
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  serve --config samples/example-npcs --port 5280

# Expected output:
# [INFO] Loading NPCs from: samples/example-npcs
# [INFO] Loaded 1 NPC(s): guard-north-gate
# [INFO] Server listening on http://0.0.0.0:5280
```

In another terminal, test the API:

```bash
# Health check
curl http://localhost:5280/health

# Expected response:
# {"status":"healthy","timestamp":"2025-11-14T..."}

# Ask a question
curl -X POST http://localhost:5280/ask \
  -H "Content-Type: application/json" \
  -d '{
    "npc": "guard-north-gate",
    "question": "What is your duty?",
    "importance": 0.3
  }'

# Expected response:
# {
#   "answer": "My duty is to guard the North Gate and question all who seek entry to the upper district. I serve the king and the Royal Guard with unwavering loyalty.",
#   "sources": ["faction:royal_guard.md#1", "npc:guard-north-gate/notes.txt#0"],
#   "scores": [0.87, 0.82],
#   "fromCloud": false
# }
```

### 8. Test Streaming Endpoint

```bash
curl -X POST http://localhost:5280/ask/stream \
  -H "Content-Type: application/json" \
  -d '{
    "npc": "guard-north-gate",
    "question": "Tell me about the keep",
    "importance": 0.8
  }'

# Expected: Streaming Server-Sent Events (SSE) response
```

## Example NPC Structure

The included example demonstrates the tiered RAG system:

```
samples/example-npcs/
├── guard-north-gate.yaml          # NPC configuration
├── world/
│   └── keep.md                     # Global lore (shared by all NPCs)
├── region/
│   └── riverside/
│       └── streets.md              # Regional lore (for Riverside NPCs)
├── faction/
│   └── royal_guard.md              # Faction lore (for royal guard NPCs)
└── npc/
    └── guard-north-gate/
        └── notes.txt               # Specific NPC knowledge
```

After ingestion, indexes are stored in `.gamerag/` directories:

```
samples/example-npcs/.gamerag/
└── guard-north-gate/
    ├── world.index
    ├── region-riverside.index
    ├── faction-royal_guard.index
    └── npc-guard-north-gate.index
```

## Testing Checklist

- [ ] .NET 8 SDK installed and verified
- [ ] Project builds without errors
- [ ] Example NPC lore ingested successfully
- [ ] CLI chat returns appropriate responses
- [ ] Interactive chat mode works
- [ ] HTTP server starts and responds to health checks
- [ ] `/ask` endpoint returns valid JSON responses
- [ ] `/ask/stream` endpoint returns SSE events
- [ ] NPC maintains persona (medieval tone, stoic behavior)
- [ ] Context-aware responses (brass token trigger works)

## Common Issues

### "Unable to load the service index for source https://api.nuget.org"
- **Solution**: Check internet connection, or configure NuGet proxy if behind corporate firewall

### "No embeddings provider configured"
- **Solution**: Either start Ollama locally or set cloud provider API keys

### "Model not found: llama3.2:3b-instruct-q4_K_M"
- **Solution**: Run `ollama pull llama3.2:3b-instruct-q4_K_M`

### NPC responses are generic/don't use lore
- **Solution**: Ensure `ingest` command completed successfully and `.gamerag/` directory exists

### Port 5280 already in use
- **Solution**: Use `--port` flag with different port number

## Next Steps

1. **Create Your Own NPC**: Copy the example structure and modify the YAML and lore files
2. **Add Memory**: Use the `RememberAsync` API to let NPCs remember conversations
3. **Deploy with Docker**: See `Dockerfile` for containerized deployment
4. **Integrate with Game Engine**: See `samples/unity/` or `samples/unreal/` for integration guides

## Performance Notes

- **Local Mode**: ~100-500ms per query (depends on model size)
- **Cloud Mode**: ~500-2000ms per query (depends on provider)
- **Hybrid Mode**: Automatically routes based on importance threshold
- **Memory Usage**: ~500MB base + ~50MB per loaded NPC

## Support

For issues or questions:
- GitHub Issues: https://github.com/TowsifAhamed/GameRagKit/issues
- Documentation: `/docs` directory
- API Reference: `openapi.json`
