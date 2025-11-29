# GameRagKit Configuration Examples

This directory contains example YAML configurations for different use cases and providers.

## Quick Reference

| File | Provider | Mode | Best For |
|------|----------|------|----------|
| [gemini-example.yaml](gemini-example.yaml) | Google Gemini | Cloud-only | Cost-effective cloud solution |
| [openai-example.yaml](openai-example.yaml) | OpenAI | Cloud-only | High-quality responses, established API |
| [ollama-local-example.yaml](ollama-local-example.yaml) | Ollama (local) | Local-only | Fully offline, no API costs, privacy |
| [hybrid-example.yaml](hybrid-example.yaml) | Gemini + Ollama | Hybrid | Best of both: fast local + smart cloud routing |

## Configuration Structure

All NPC configs follow this structure:

```yaml
persona:           # NPC personality and characteristics
  id: unique_name
  system_prompt: "..."
  traits: [...]
  style: concise|conversational|formal|terse
  region_id: location
  faction_id: group
  world_id: game_world
  default_importance: 0.0-1.0

rag:              # Retrieval-augmented generation settings
  sources: [...]  # Knowledge base files
  chunk_size: 450
  overlap: 60
  top_k: 4

providers:        # LLM and embedding providers
  routing:
    mode: local_only|cloud_only|hybrid
  local:
    engine: ollama|llamasharp
    chat_model: "..."
    embed_model: "..."
  cloud:
    provider: openai|gemini|azure|groq|openrouter|mistral
    chat_model: "..."
    embed_model: "..."
```

## Supported Cloud Providers

GameRagKit supports the following cloud providers:

| Provider | Status | API Format | Notes |
|----------|--------|------------|-------|
| **OpenAI** | ✅ Fully Supported | OpenAI-compatible | gpt-4.1, gpt-4o, etc. |
| **Azure OpenAI** | ✅ Fully Supported | OpenAI-compatible | Enterprise deployments |
| **Google Gemini** | ✅ Fully Supported | Gemini-specific | gemini-2.0-flash-exp, gemini-2.5-pro |
| **Groq** | ✅ Fully Supported | OpenAI-compatible | Fast inference |
| **OpenRouter** | ✅ Fully Supported | OpenAI-compatible | Multi-provider aggregator |
| **Mistral AI** | ✅ Fully Supported | OpenAI-compatible | mistral-large, etc. |
| **Anthropic/Claude** | ❌ Not Supported | Different format | Requires separate implementation |
| **Cohere** | ❌ Not Supported | Different format | Requires separate implementation |

### Implementation Details

- **OpenAI-compatible providers** (OpenAI, Azure, Groq, OpenRouter, Mistral) use the standard `/v1/chat/completions` and `/v1/embeddings` endpoints
- **Gemini** uses Google's custom `/v1beta/models/{model}:generateContent` and `:embedContent` endpoints
- **Anthropic and Cohere** use different API structures and are not currently supported

## Provider Details

### Google Gemini
**Best for:** Cost-effective cloud solution with good quality

**Latest Models (2025):**
- Chat: `gemini-2.0-flash-exp` (fast), `gemini-2.5-flash` (enhanced), `gemini-2.5-pro` (most capable)
- Embeddings: `text-embedding-004`

```yaml
cloud:
  provider: gemini
  chat_model: gemini-2.0-flash-exp  # Fast, cost-effective (Jan 2025)
  # OR: gemini-2.5-flash            # Enhanced reasoning (June 2025)
  # OR: gemini-2.5-pro              # Most capable with thinking (March 2025)
  embed_model: text-embedding-004
  endpoint: https://generativelanguage.googleapis.com/
```

**Get API key:** https://aistudio.google.com/apikey

**Environment variables:**
```bash
export PROVIDER=gemini
export API_KEY=AIzaSy...
export ENDPOINT=https://generativelanguage.googleapis.com/
```

### OpenAI
**Best for:** Established API, excellent quality, wide model selection

**Latest Models (2025):**
- Chat: `gpt-4.1` (best), `gpt-4.1-mini` (fast), GPT-5 (coming soon)
- Embeddings: `text-embedding-3-large` (3072 dims), `text-embedding-3-small` (1536 dims)

```yaml
cloud:
  provider: openai
  chat_model: gpt-4.1              # Latest GPT-4.1 (2025) - best performance
  # OR: gpt-4.1-mini               # Fast, cost-effective
  # OR: gpt-4o                     # Still available, very capable
  embed_model: text-embedding-3-large   # Best quality (3072 dims)
  # OR: text-embedding-3-small          # Faster, cheaper (1536 dims)
  endpoint: https://api.openai.com/
```

**Get API key:** https://platform.openai.com/api-keys

**Environment variables:**
```bash
export PROVIDER=openai
export API_KEY=sk-proj-...
export ENDPOINT=https://api.openai.com/
```

### Ollama (Local)
**Best for:** Fully offline operation, no API costs, data privacy

```yaml
local:
  engine: ollama
  chat_model: llama3.2:3b-instruct-q4_K_M  # Fast, quantized
  # OR: llama3.1:8b                        # Better quality
  embed_model: nomic-embed-text
  endpoint: http://localhost:11434
```

**Setup:**
```bash
# Install Ollama: https://ollama.ai/download
ollama pull llama3.2:3b-instruct-q4_K_M
ollama pull nomic-embed-text
```

**Environment variables:**
```bash
export PROVIDER=ollama
# No API_KEY needed!
```

### Azure OpenAI
**Best for:** Enterprise deployments, compliance requirements

```yaml
cloud:
  provider: azure
  chat_model: gpt-4              # Deployed model name
  embed_model: text-embedding-ada-002
  endpoint: https://YOUR-RESOURCE.openai.azure.com/
```

**Get credentials:** Azure Portal → OpenAI Resource → Keys and Endpoint

**Environment variables:**
```bash
export PROVIDER=azure
export API_KEY=your-azure-key
export ENDPOINT=https://YOUR-RESOURCE.openai.azure.com/
```

## Routing Modes

### `local_only`
- All requests go to local Ollama
- **Pros:** Free, fast, private, offline
- **Cons:** Lower quality than cloud models
- **Use when:** Cost/privacy critical, or no internet

### `cloud_only`
- All requests go to cloud provider (OpenAI/Gemini/etc.)
- **Pros:** Highest quality responses
- **Cons:** Costs API credits, requires internet
- **Use when:** Quality is critical, budget allows

### `hybrid` (Recommended)
- Routes requests based on `importance` score
- Low-importance → local (free, fast)
- High-importance → cloud (paid, high quality)
- Falls back to cloud if local fails
- **Pros:** Best of both worlds, cost-effective
- **Cons:** Requires both local and cloud setup
- **Use when:** You want to optimize cost vs. quality

**How importance routing works:**
```yaml
routing:
  default_importance: 0.3  # Threshold: >= 0.3 goes to cloud

# In your game code:
npc.Ask("What time do you close?", importance: 0.1)  # → Local
npc.Ask("Tell me about the prophecy", importance: 0.8)  # → Cloud
```

## Database Setup

All modes require a vector database for RAG. Use the included `docker-compose.yml`:

```bash
# Start PostgreSQL (recommended)
docker-compose up -d

# Or start Qdrant instead
docker-compose --profile qdrant up -d qdrant
```

**Environment variable:**
```bash
export DB_CONNECTION_STRING="Server=localhost;Port=5432;Database=gamerag;User Id=gamerag;Password=gamerag123;"
```

## Usage

1. **Choose a config file** based on your needs
2. **Copy it** to your NPC directory (e.g., `NPCs/blacksmith.yaml`)
3. **Customize** the persona, traits, and knowledge sources
4. **Set environment variables** for your chosen provider
5. **Load in your code:**
   ```csharp
   var npc = await GameRAGKit.Load("NPCs/blacksmith.yaml");
   var response = await npc.Ask("What's the latest gossip?");
   ```

## Customization Tips

### Adjusting Personality
- **`system_prompt`:** Core personality and background
- **`traits`:** Short descriptors that guide behavior
- **`style`:** Speech pattern (concise, conversational, formal, terse)

### Tuning RAG
- **`chunk_size`:** Larger = more context per chunk (default: 450)
- **`overlap`:** Prevents splitting related info (default: 60)
- **`top_k`:** Number of relevant chunks to retrieve (default: 4)
- **`filters`:** Narrow retrieval by metadata (region, faction, etc.)

### Cost Optimization
- Use `hybrid` mode with `default_importance: 0.4` to send only ~40% to cloud
- Use cheaper models: `gpt-4o-mini` or `gemini-1.5-flash`
- Reduce `top_k` to retrieve fewer chunks (less embedding costs)

## Troubleshooting

**"Cannot create an instance of type NpcConfig"**
- Update GameRagKit to version `0.0.0-ci.3` or later

**"Connection refused" on database**
- Ensure Docker is running: `docker ps`
- Start database: `docker-compose up -d`

**"Invalid API key"**
- Verify environment variable: `echo $API_KEY`
- Check key is valid for your provider

**"Ollama not found"**
- Install Ollama: https://ollama.ai/download
- Verify running: `curl http://localhost:11434/api/tags`

## More Information

- Main README: [../../README.md](../../README.md)
- Issues & Improvements: [../../ISSUES_AND_IMPROVEMENTS.md](../../ISSUES_AND_IMPROVEMENTS.md)
- GitHub: https://github.com/TowsifAhamed/GameRagKit
