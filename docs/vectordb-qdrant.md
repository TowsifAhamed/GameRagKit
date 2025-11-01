# Qdrant Setup

Use the OSS compose stack to boot Qdrant locally.

## 1. Start Qdrant

```bash
cd docker
docker compose -f docker-compose.qdrant.yml up -d
```

Qdrant listens on `http://localhost:6333` by default.

## 2. Create a collection

The compose file bootstraps a `gamerag` collection sized for 1,536-d vectors. To create it manually:

```bash
curl -X PUT "http://localhost:6333/collections/gamerag" \
  -H 'Content-Type: application/json' \
  -d '{
        "vectors": {
          "size": 1536,
          "distance": "Cosine"
        }
      }'
```

## 3. Configure GameRAGKit

```env
DB=qdrant
QDRANT_ENDPOINT=http://localhost:6333
QDRANT_COLLECTION=gamerag
```

When you run `gamerag serve`, the HTTP client uses the REST API to upsert points and perform filtered searches. Payload metadata mirrors the manifest tags so you can filter by world, region, faction, or custom labels.
