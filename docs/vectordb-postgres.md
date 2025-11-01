# pgvector Setup

Use the bundled compose stack for a ready-to-go Postgres + pgvector instance.

## 1. Start Postgres

```bash
cd docker
docker compose -f docker-compose.postgres.yml up -d
```

The container exposes port `5433` locally to avoid conflicts with an existing Postgres install.

## 2. Configure GameRAGKit

Create a `.env` file (or edit `.env.example`):

```env
DB=pgvector
CONNECTION_STRING=Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=gamerag
```

The CLI and server read these values automatically. When the server starts it will ensure the `vector` and `pgcrypto` extensions exist and create the `rag_chunks` table with the necessary HNSW index.

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
