# GameRAGKit Roadmap

This roadmap captures the prioritized milestones for the GameRAGKit core server. Each milestone lists the concrete steps that need to ship before moving on so teams across engines can track progress at a glance.

## Quick reference

1. **Lock the API** – freeze the HTTP contract and request model.
2. **Vector store & persistence** – land the storage abstraction and pgvector path.
3. **Providers & routing** – wire local-first LLMs with cloud fallback.
4. **HTTP runtime polish** – ship the production-ready controllers and OpenAPI.
5. **Packaging & CI** – publish the toolchain and automated builds.
6. **Docs & samples** – hand users a five-minute quickstart and engine guides.
7. **Observability & safety** – add metrics, logging, and guardrails.

The detailed steps below expand each item so contributors can implement them sequentially.

---

## Milestone 0 — Lock the API (today)

**Goal:** Freeze the server contract so Java/Unreal/Unity can build against it.

- **Endpoints**
  - `POST /ask` → `{ answer, sources, scores, fromCloud }`
  - `POST /ask/stream` (SSE) → `data: <token>` lines
  - `POST /ingest` (optional hot ingest) → `{ added: <id> }`
  - `GET /health` → `{ ok: true }`
  - `GET /metrics` (Prometheus) (optional after M1)
- **Headers**
  - `X-GameRAG-Protocol: 1` (required)
  - `Authorization: Bearer <token>` or `X-Api-Key: <key>` (optional)
- **Request model** (used by both `/ask` and `/ask/stream`)

  ```json
  {
    "npc": "villager-uuid-or-any-id",
    "question": "Where is the key?",
    "options": { "topK": 4, "inCharacter": true, "importance": 0.2, "forceLocal": false, "forceCloud": false }
  }
  ```

---

## Milestone 1 — Vector store & persistence (prod-ready default)

**Goal:** Move off in-memory; support thousands of NPCs.

1. **Vector store abstraction**
   - `src/GameRagKit/VectorStores/IVectorStore.cs`

     ```csharp
     public interface IVectorStore {
       Task UpsertAsync(IEnumerable<RagRecord> records, CancellationToken ct = default);
       Task<IReadOnlyList<RagHit>> SearchAsync(ReadOnlyMemory<float> query, int topK,
            IReadOnlyDictionary<string,string>? filters = null, CancellationToken ct = default);
     }
     public sealed record RagRecord(string Key, string Collection, string Text, float[] Embedding, Dictionary<string,string>? Tags=null);
     public sealed record RagHit(string Key, string Text, double? Score, Dictionary<string,string> Tags);
     ```

2. **pgvector (default)**
   - Add `PgVectorStore.cs` using `Npgsql` (+ `pgvector-dotnet` or `SK` connector).
   - Create bootstrap SQL:

     ```sql
     CREATE EXTENSION IF NOT EXISTS vector;
     CREATE TABLE IF NOT EXISTS rag_chunks(
       key UUID PRIMARY KEY DEFAULT gen_random_uuid(),
       collection TEXT NOT NULL,
       tags JSONB DEFAULT '{}',
       text TEXT NOT NULL,
       embedding VECTOR(1536) NOT NULL
     );
     CREATE INDEX IF NOT EXISTS rag_chunks_collection_idx ON rag_chunks(collection);
     CREATE INDEX IF NOT EXISTS rag_chunks_embedding_hnsw ON rag_chunks USING HNSW (embedding vector_l2_ops);
     ```

   - Add `/docker/docker-compose.postgres.yml` (Postgres + pgvector).

3. **Qdrant (OSS)**
   - `QdrantStore.cs` using official .NET client.
   - `/docker/docker-compose.qdrant.yml` with a default collection.
4. *(Optional after M2)* Pinecone, Milvus, Elasticsearch adapters behind `IVectorStore`.
5. **Content hashing & manifest**
   - Compute SHA-256 per source; skip re-embedding unchanged.
   - Persist a manifest at `.gamerag/manifest.json` per collection.

---

## Milestone 2 — Providers & routing (local by default)

**Goal:** Cost-friendly local LLMs with cloud upgrade when needed.

1. **Provider interfaces**
   - `Providers/IChatModel.cs`, `Providers/IEmbedder.cs`

     ```csharp
     public interface IChatModel { IAsyncEnumerable<string> StreamAsync(string system, string context, string user, CancellationToken ct); Task<string> InvokeAsync(string system, string context, string user, CancellationToken ct); }
     public interface IEmbedder { Task<float[]> EmbedAsync(string text, CancellationToken ct); }
     ```

2. **Local**
   - `OllamaClient.cs` for chat + embeddings (`LLM` + `nomic-embed-text`).
   - `LLamaSharpClient.cs` (pure in-process fallback) for fully offline servers.
3. **Cloud**
   - Keep OpenAI/Azure/Mistral/Gemini/HF wired via your SK builder.
   - Router: importance + mode (`local_only | cloud_only | hybrid`) to choose local vs cloud; optional low-confidence fallback.
4. **Config**
   - YAML fields already defined; add env fallbacks:
     - `PROVIDER`, `API_KEY`, `ENDPOINT`
     - `LOCAL_ENGINE=ollama|llamasharp`, `OLLAMA_HOST`
     - `DB=pgvector|qdrant|pinecone|milvus|elastic`, `CONNECTION_STRING`, etc.

---

## Milestone 3 — HTTP runtime polish

**Goal:** First-class engine integration experience.

1. **SSE streaming**
   - `Http/AskStreamController.cs`:
     - `Content-Type: text/event-stream`
     - Flush per token: `await response.WriteAsync($"data: {token}\n\n");`
2. **Non-streaming**
   - `Http/AskController.cs` returns the final aggregated answer.
3. **Hot ingest (optional)**
   - `POST /ingest { id, text, tags }` → `UpsertAsync` into the active store.
4. **OpenAPI**
   - Add Swashbuckle or NSwag and emit `openapi.json` on build; commit it so `gameragkit-java` can validate.
5. **CORS**
   - Enable configurable CORS for local tools/UI.

---

## Milestone 4 — Packaging & CI

**Goal:** Install and run in 2 commands.

- **NuGet:** publish `GameRagKit` + `GameRagKit.Cli` (dotnet tool `gamerag`).
- **Docker**
  - `Dockerfile` (self-contained) to run `gamerag serve`.
  - Compose stacks: postgres, qdrant.
- **GitHub Actions**
  - `.github/workflows/ci.yml`: build, test, pack, docker build.
  - `.github/workflows/release.yml`: on tag → publish NuGet + Docker image.
- **.env + appsettings**
  - Sample `.env.example` for provider & DB creds.

---

## Milestone 5 — Docs & samples

**Goal:** Five-minute quickstart + engine guides.

- `docs/quickstart.md` (Ollama path)
  1. Install Ollama
  2. `ollama pull llama3.2:3b-instruct-q4_K_M + nomic-embed-text`
  3. Create `npc_guard.yaml`
  4. `gamerag ingest ./NPCs`
  5. `gamerag serve`
  6. `curl -X POST http://localhost:5280/ask …`
- `docs/vectordb-postgres.md` (compose + schema)
- `docs/vectordb-qdrant.md` (compose + collection setup)
- `docs/minecraft.md`
  - How to run sidecar next to Paper/Fabric
  - Endpoint examples (sync + SSE)
  - Link to `gameragkit-java` and Paper sample
- `samples/unreal/` (HTTP + SSE)
- `samples/unity/` (UnityWebRequest + SSE)
- `samples/minecraft-paper/README.md` (just points to Java repo sample)

---

## Milestone 6 — Observability & safety (nice-to-have but valuable)

- **Metrics:** request counts, latency, token counts, vector search time, fallback rate.
- **Logging:** structured logs (Serilog), correlation id per request.
- **Rate limiting:** per API key/IP.
- **Safety rails:** persona "do/don't" rules; optional profanity filter hook.

---

## Concrete file adds/changes (paths you can create now)

```
/src/GameRagKit/
  VectorStores/IVectorStore.cs
  VectorStores/PgVectorStore.cs
  VectorStores/QdrantStore.cs
  Providers/IChatModel.cs
  Providers/IEmbedder.cs
  Providers/OllamaClient.cs
  Providers/LLamaSharpClient.cs
  Http/AskController.cs
  Http/AskStreamController.cs
  Http/HealthController.cs
  Config/Options.cs                # bind YAML/env to POCOs
  Pipeline/Retriever.cs            # tiered retrieval (world/region/faction/memory)
  Pipeline/Router.cs               # local/cloud routing

/src/GameRagKit.Cli/
  Commands/IngestCommand.cs
  Commands/ServeCommand.cs
  Commands/ChatCommand.cs

/docs/
  quickstart.md
  vectordb-postgres.md
  vectordb-qdrant.md
  minecraft.md

/samples/
  unreal/
  unity/
  minecraft-paper/README.md

/docker/
  docker-compose.postgres.yml
  docker-compose.qdrant.yml

Dockerfile
openapi.json (generated at build)
.env.example
```

---

## Priority order (shortest path to “usable everywhere”)

1. pgvector adapter + compose + content hashing
2. SSE streaming + OpenAPI
3. Ollama + LLamaSharp providers behind `IChatModel`/`IEmbedder`
4. Docs (quickstart/minecraft) + samples (Unreal/Unity curl-level is fine first)
5. Qdrant adapter
6. Packaging (NuGet, Docker) + CI
7. Pinecone / Milvus / Elasticsearch (as demand appears)

## Execution checklist

- [ ] Milestone 0 complete — API locked and `/ask` endpoints stable.
- [ ] Milestone 1 complete — vector store abstraction implemented with pgvector defaults.
- [ ] Milestone 2 complete — local and cloud providers routed with configuration fallbacks.
- [ ] Milestone 3 complete — HTTP runtime polished with SSE streaming and OpenAPI artifact.
- [ ] Milestone 4 complete — packaging and CI pipelines delivering NuGet + Docker outputs.
- [ ] Milestone 5 complete — docs and samples deliver the five-minute quickstart.
- [ ] Milestone 6 complete — observability and safety guardrails in place.

---

## Integration notes (with gameragkit-java)

- Keep `/ask` response fields stable; ignore unknown fields in Java (already handled if using Jackson defaults).
- Ensure SSE line format is exactly: `data: <chunk>\n\n`.
- Bump `X-GameRAG-Protocol` if you remove or rename fields; add fields without breaking.
- If you add auth, accept both `Authorization: Bearer` and `X-Api-Key`.
