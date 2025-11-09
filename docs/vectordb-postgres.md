# pgvector Setup

Use the bundled compose stack for a ready-to-go Postgres + pgvector instance.

## 1. Start Postgres

```bash
cd docker
docker compose -f docker-compose.postgres.yml up -d
```

The container exposes port `5432` locally so the default connection string works out of the box.

## 2. Configure GameRAGKit

Create a `.env` file (or edit `.env.example`):

```env
DB=pgvector
CONNECTION_STRING=Host=localhost;Port=5432;Username=rag;Password=rag;Database=gamerag
```

The server ensures the `vector` extension exists, creates the `rag_chunks` table, and wires up the HNSW index when it first runs.

## 3. (Optional) Inspect the schema

```sql
\d+ rag_chunks
```

You should see:

- `key uuid primary key`
- `collection text`
- `tags jsonb`
- `text text`
- `embedding vector(1536)`

The `rag_chunks_embedding_hnsw` index accelerates similarity search using `vector_l2_ops`.
