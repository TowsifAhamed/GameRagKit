# GameRAGKit Quickstart (Ollama Path)

This walkthrough gets a local GameRAGKit server online with Ollama and the built-in CLI.

## 1. Install Ollama

Download Ollama from [ollama.com](https://ollama.com/download) and ensure the daemon is running. The server listens on `http://localhost:11434` by default.

## 2. Pull the required models

```bash
ollama pull llama3.2:3b-instruct-q4_K_M
ollama pull nomic-embed-text
```

## 3. Prepare your NPC config

Create `npc_guard.yaml` next to your lore files. A minimal example:

```yaml
persona:
  id: guard-01
  system_prompt: |
    You are the town guard. Answer questions about patrols and visitors.
rag:
  sources:
    - file: lore/guard_notes.txt
providers:
  routing:
    mode: hybrid
  local:
    engine: ollama
    chat_model: llama3.2:3b-instruct-q4_K_M
    embed_model: nomic-embed-text
```

## 4. Ingest your knowledge base

```bash
gamerag ingest ./NPCs
```

The CLI hashes each file and only re-embeds content that changed. Manifests are stored in `.gamerag/manifest.json` per persona.

## 5. Serve the API

```bash
gamerag serve --config ./NPCs --port 5280
```

The HTTP service exposes:

- `POST /ask`
- `POST /ask/stream`
- `POST /ingest` *(optional, see roadmap)*
- `GET /health`

All requests must include `X-GameRAG-Protocol: 1`.

## 6. Ask a question

```bash
curl -X POST http://localhost:5280/ask \
  -H 'Content-Type: application/json' \
  -H 'X-GameRAG-Protocol: 1' \
  -d '{
        "npc": "guard-01",
        "question": "Where is the spare gate key?",
        "options": { "topK": 4, "inCharacter": true }
      }'
```

The response body contains the answer, the referenced sources, cosine scores, and whether the cloud provider handled the reply.

To receive a streaming response, call `/ask/stream` with the same payload and read the `data:` lines from the SSE stream.
