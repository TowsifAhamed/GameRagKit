# Qdrant Setup

Use the OSS compose stack to boot Qdrant locally.

## 1. Start Qdrant

```bash
cd docker
docker compose -f docker-compose.qdrant.yml up -d
```

Qdrant exposes the HTTP API on `http://localhost:6333` and the gRPC API on `http://localhost:6334` (forwarded in the compose file).

## 2. Configure GameRAGKit

```env
DB=qdrant
QDRANT_ENDPOINT=http://localhost:6333
QDRANT_COLLECTION=gamerag
QDRANT_GRPC_PORT=6334
```

When you run `gamerag serve`, the runtime uses the official gRPC client to create the collection on first use (if it doesn't already exist), validate the vector dimensionality, and upsert/search points. Payload metadata mirrors the manifest tags so you can filter by world, region, faction, or custom labels.
