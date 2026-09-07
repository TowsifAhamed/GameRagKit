# Deploying GameRagKit for free (Google Cloud Run + Neon)

This deploys your own GameRagKit instance to Google Cloud Run's Always Free tier, backed
by Neon's free Postgres (pgvector) tier. Both are durable free tiers (no 30-day expiry,
no credit-card-triggered surprise charges within the free quota) — not trials.

**Cost to you: $0** within each platform's free limits. You bring your own LLM API key
(OpenAI, Gemini, etc.) and pay that provider directly for whatever you use — GameRagKit
itself never sees or bills for your key.

This is a "deploy your own instance" template, not a shared public GameRagKit server.
Each deployer gets their own isolated instance, their own database, their own API costs.

## What you'll need

- A Google Cloud account (Cloud Run's Always Free tier requires no ongoing cost within
  its quota: 2M requests/month, 360,000 GiB-seconds compute/month).
- A [Neon](https://neon.tech) account (free tier: 0.5GB storage, pgvector included, no
  credit card, never expires).
- An API key from a supported cloud LLM provider (OpenAI, Azure, Gemini, Groq,
  OpenRouter, or Mistral — see [Provider Compatibility](2025-11-29/PROVIDER_COMPATIBILITY.md)).
- `gcloud` CLI installed and authenticated (`gcloud auth login`).

## 1. Set up Neon (vector storage)

1. Create a free project at [neon.tech](https://neon.tech).
2. In the Neon SQL editor, enable pgvector: `CREATE EXTENSION IF NOT EXISTS vector;`
3. From your project's connection details, note the **host**, **database name**,
   **role/username**, and **password** individually — you'll need them in keyword-value
   form below, not the `postgres://...` URI Neon shows by default.

   Npgsql (the .NET Postgres driver GameRagKit uses) expects a keyword-value connection
   string, not a URI. Convert Neon's default format:

   ```
   # Neon shows you this:
   postgres://alex:AbCdEf123@ep-cool-name-12345.us-east-2.aws.neon.tech/neondb?sslmode=require

   # GameRagKit's CONNECTION_STRING needs this instead:
   Host=ep-cool-name-12345.us-east-2.aws.neon.tech;Port=5432;Username=alex;Password=AbCdEf123;Database=neondb;SSL Mode=Require;Trust Server Certificate=true
   ```

## 2. Fork and customize (optional)

The Cloud Run image bakes its NPC config into the container (Cloud Run doesn't support
mounted volumes), using [`deploy/cloudrun-config/`](../deploy/cloudrun-config/) as the
default. To deploy your own NPC instead of the bundled example:

1. Fork this repository.
2. Replace the contents of `deploy/cloudrun-config/` with your own NPC YAML + lore files.
3. Build from your fork in step 3 below.

To just try the bundled example NPC first, skip this step.

## 3. Deploy to Cloud Run

```bash
gcloud run deploy gameragkit \
  --source . \
  --dockerfile Dockerfile.cloudrun \
  --region us-central1 \
  --allow-unauthenticated \
  --set-env-vars "DB=pgvector" \
  --set-env-vars "CONNECTION_STRING=Host=...;Port=5432;Username=...;Password=...;Database=...;SSL Mode=Require;Trust Server Certificate=true" \
  --set-env-vars "PROVIDER=openai" \
  --set-env-vars "API_KEY=sk-..." \
  --set-env-vars "CLOUD_CHAT_MODEL=gpt-4o-mini" \
  --set-env-vars "CLOUD_EMBED_MODEL=text-embedding-3-small"
```

`gcloud` builds the image from `Dockerfile.cloudrun` and deploys it. On success it prints
your service URL (`https://gameragkit-xxxxx-uc.a.run.app`).

> An embedding model is required even for an NPC with no lore sources — every `/ask` call
> embeds the player's question to search the vector store, regardless of whether anything
> is indexed yet.

## 4. Test it

```bash
curl -X POST https://gameragkit-xxxxx-uc.a.run.app/ask \
  -H "X-GameRAG-Protocol: 1" \
  -H "Content-Type: application/json" \
  -d '{"npc":"guard-north-gate","question":"What is your duty?"}'
```

## Securing your deployment

`--allow-unauthenticated` above makes the service publicly reachable — anyone with the
URL can call it and consume your LLM API quota. For anything beyond a quick test:

- Set `SERVICE_API_KEY` as another `--set-env-vars` entry and require `X-API-Key` on every
  request (see the [Authentication section](../README.md#authentication--metrics) in the
  main README).
- Or drop `--allow-unauthenticated` and use [Cloud Run's IAM-based
  authentication](https://cloud.google.com/run/docs/authenticating/overview) instead if
  only your own backend should call it.

## Free tier limits to know about

- **Cloud Run**: scales to zero when idle (no cost while idle), cold start of a few
  seconds on the next request after idling. 2M requests/month and 360,000 GiB-seconds
  compute/month are free; usage beyond that is billed.
- **Neon**: 0.5GB storage and 100 compute-hours/month per project, free forever, no
  expiry. Sufficient for a modest amount of NPC lore; a large multi-world deployment with
  many long lore documents may need to upgrade.
- **Your LLM provider**: billed by your provider directly, based on actual usage. This is
  the main ongoing cost of running a deployed instance — GameRagKit's local-model routing
  (Ollama) isn't available on Cloud Run since there's no local GPU/model runtime there,
  so every request goes to your cloud provider.

## Updating your deployment

```bash
gcloud run deploy gameragkit --source . --dockerfile Dockerfile.cloudrun --region us-central1
```

Re-running the deploy command rebuilds from your current `deploy/cloudrun-config/` (or
`main` branch, if deploying from a fork) and replaces the running revision.
