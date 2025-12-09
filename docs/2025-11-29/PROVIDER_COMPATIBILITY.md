# Cloud Provider Compatibility Guide

**Last Updated:** 2025-12-09
**GameRagKit Version:** 0.1.0 (stable) / 0.1.0-ci.* (CI builds)

---

## Overview

GameRagKit supports multiple cloud LLM providers through a unified configuration interface. This document clarifies which providers are fully supported, partially supported, or not yet implemented.

---

## Fully Supported Providers ✅

These providers have been tested and work out-of-the-box:

### 1. OpenAI
- **Provider value:** `openai`
- **API Format:** OpenAI-compatible
- **Endpoints:**
  - Chat: `/v1/chat/completions`
  - Embeddings: `/v1/embeddings`
- **Endpoint URL:** `https://api.openai.com/`
- **Latest Models (2025):**
  - Chat: `gpt-4.1`, `gpt-4.1-mini`, `gpt-4o`
  - Embeddings: `text-embedding-3-large` (3072 dims), `text-embedding-3-small` (1536 dims)
- **Get API Key:** https://platform.openai.com/api-keys

**Example Configuration:**
```yaml
cloud:
  provider: openai
  chat_model: gpt-4.1
  embed_model: text-embedding-3-large
  endpoint: https://api.openai.com/
```

---

### 2. Azure OpenAI
- **Provider value:** `azure`
- **API Format:** OpenAI-compatible (Azure variant)
- **Endpoints:**
  - Chat: `/openai/deployments/{model}/chat/completions?api-version=2024-05-01-preview`
  - Embeddings: `/openai/deployments/{model}/embeddings?api-version=2024-05-01-preview`
- **Endpoint URL:** `https://<your-resource>.openai.azure.com/`
- **Models:** Depends on your Azure deployment
  - Chat: `gpt-4`, `gpt-35-turbo`
  - Embeddings: `text-embedding-ada-002`
- **Get Credentials:** Azure Portal → OpenAI Resource → Keys and Endpoint

**Example Configuration:**
```yaml
cloud:
  provider: azure
  chat_model: gpt-4  # Your Azure deployment name
  embed_model: text-embedding-ada-002
  endpoint: https://your-resource.openai.azure.com/
```

---

### 3. Google Gemini
- **Provider value:** `gemini`
- **API Format:** Gemini-specific (custom implementation)
- **Endpoints:**
  - Chat: `/v1beta/models/{model}:generateContent`
  - Embeddings: `/v1beta/models/{model}:embedContent`
- **Endpoint URL:** `https://generativelanguage.googleapis.com/`
- **Latest Models (2025):**
  - Chat: `gemini-2.0-flash-exp`, `gemini-2.5-flash`, `gemini-2.5-pro`
  - Embeddings: `text-embedding-004`
- **Get API Key:** https://aistudio.google.com/apikey
- **Added:** Commit edb1577 (2025-11-29)

**Example Configuration:**
```yaml
cloud:
  provider: gemini
  chat_model: gemini-2.0-flash-exp
  embed_model: text-embedding-004
  endpoint: https://generativelanguage.googleapis.com/
```

**Special Notes:**
- Gemini uses a different request/response format than OpenAI
- System prompts are combined with user messages (Gemini doesn't support separate system messages)
- Finish reason mapping: `MAX_TOKENS` → triggers fallback

---

### 4. Groq
- **Provider value:** `groq`
- **API Format:** OpenAI-compatible
- **Endpoints:** Standard `/v1/chat/completions` (embeddings not offered)
- **Endpoint URL:** `https://api.groq.com/openai/v1/`
- **Models:**
  - Chat: `llama-3.1-70b-versatile`, `mixtral-8x7b-32768`
  - Embeddings: Not available
- **Get API Key:** https://console.groq.com/

**Example Configuration:**
```yaml
cloud:
  provider: groq
  chat_model: llama-3.1-70b-versatile
  embed_model: null  # Use local embeddings or different provider
  endpoint: https://api.groq.com/openai/v1/
```

---

### 5. OpenRouter
- **Provider value:** `openrouter`
- **API Format:** OpenAI-compatible
- **Endpoints:** Standard `/v1/chat/completions`
- **Endpoint URL:** `https://openrouter.ai/api/v1/`
- **Models:** Any model supported by OpenRouter
  - Examples: `anthropic/claude-3-opus`, `google/gemini-pro`, `openai/gpt-4`
- **Get API Key:** https://openrouter.ai/keys

**Example Configuration:**
```yaml
cloud:
  provider: openrouter
  chat_model: anthropic/claude-3-opus  # Note: Uses OpenRouter's proxy, not native Anthropic API
  embed_model: null
  endpoint: https://openrouter.ai/api/v1/
```

---

### 6. Mistral AI
- **Provider value:** `mistral`
- **API Format:** OpenAI-compatible
- **Endpoints:** Standard `/v1/chat/completions` and `/v1/embeddings`
- **Endpoint URL:** `https://api.mistral.ai/`
- **Models:**
  - Chat: `mistral-large-latest`, `mistral-medium`, `mistral-small`
  - Embeddings: `mistral-embed`
- **Get API Key:** https://console.mistral.ai/

**Example Configuration:**
```yaml
cloud:
  provider: mistral
  chat_model: mistral-large-latest
  embed_model: mistral-embed
  endpoint: https://api.mistral.ai/
```

---

## Local Providers ✅

### 7. Ollama
- **Provider value:** `ollama` (in `local` config section)
- **API Format:** Ollama-specific
- **Endpoint URL:** `http://localhost:11434` (default)
- **Models:**
  - Chat: `llama3.2:3b-instruct-q4_K_M`, `llama3.1:8b`, `mistral:7b`
  - Embeddings: `nomic-embed-text`, `mxbai-embed-large`
- **Setup:** https://ollama.ai/download
- **No API Key Required**

**Example Configuration:**
```yaml
local:
  engine: ollama
  chat_model: llama3.2:3b-instruct-q4_K_M
  embed_model: nomic-embed-text
  endpoint: http://localhost:11434
```

---

## Not Supported ❌

### Anthropic/Claude (Direct API)
- **Status:** ❌ Not supported
- **Reason:** Uses a different API format:
  - Different endpoint structure (`/v1/messages`)
  - Different request schema (roles, max_tokens_to_sample)
  - Different response format (content blocks)
- **Workaround:** Use OpenRouter as a proxy (see OpenRouter section above)
- **Future:** Would require dedicated `AnthropicChatProvider` implementation

**Why it doesn't work:**
```json
// Anthropic expects this format:
{
  "model": "claude-3-opus-20240229",
  "max_tokens": 1024,
  "messages": [{"role": "user", "content": "Hello"}]
}

// GameRagKit sends OpenAI format:
{
  "model": "claude-3-opus",
  "messages": [{"role": "system", "content": "..."}, {"role": "user", "content": "..."}],
  "temperature": 0.6,
  "max_tokens": 512
}
```

---

### Cohere
- **Status:** ❌ Not supported
- **Reason:** Uses a different API format:
  - Different endpoint structure (`/v1/generate`, `/v1/embed`)
  - Different request schema (prompt instead of messages)
  - Different response format
- **Future:** Would require dedicated `CohereChatProvider` implementation

**Why it doesn't work:**
```json
// Cohere expects this format:
{
  "model": "command-r",
  "prompt": "Hello",
  "max_tokens": 512
}

// GameRagKit sends OpenAI format with messages array
```

---

## Implementation Architecture

### How Provider Routing Works

GameRagKit uses two provider implementations:

1. **`CloudChatProvider`** and **`CloudEmbeddingProvider`**
   - Support OpenAI-compatible APIs (OpenAI, Azure, Groq, OpenRouter, Mistral)
   - Special handling for Gemini (different endpoints and request/response format)
   - Code location: [src/GameRagKit/Providers/CloudChatProvider.cs](../../src/GameRagKit/Providers/CloudChatProvider.cs)

2. **`OllamaChatProvider`** and **`OllamaEmbeddingProvider`**
   - Support local Ollama inference
   - Code location: [src/GameRagKit/Providers/OllamaChatProvider.cs](../../src/GameRagKit/Providers/OllamaChatProvider.cs)

### Adding New Providers

To add support for a new provider (e.g., Anthropic, Cohere):

1. Check if it's OpenAI-compatible:
   - Uses `/v1/chat/completions` endpoint
   - Accepts `{"model": "...", "messages": [...], "temperature": ...}` format
   - Returns `{"choices": [{"message": {"content": "..."}}]}`
   - **If yes:** Just configure it with the correct endpoint URL
   - **If no:** Needs custom implementation

2. For custom implementations:
   - Create new provider classes (e.g., `AnthropicChatProvider`)
   - Implement `IChatProvider` and `IEmbeddingProvider` interfaces
   - Add provider string detection in `NpcAgent.cs`
   - Update `CloudProviderConfig` validation

---

## Testing Provider Compatibility

To test if a provider works:

```bash
# 1. Set environment variables
export PROVIDER=your-provider
export API_KEY=your-api-key
export ENDPOINT=https://api.example.com/

# 2. Create test config
cat > test-npc.yaml <<EOF
persona:
  id: test
  system_prompt: "You are a helpful assistant."

rag:
  sources: []

providers:
  routing:
    mode: cloud_only
  cloud:
    provider: $PROVIDER
    chat_model: your-model-name
    endpoint: $ENDPOINT
EOF

# 3. Run chat test
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- chat --npc test-npc.yaml --question "Hello"
```

---

## Environment Variables

Required environment variables by provider:

| Provider | `PROVIDER` | `API_KEY` | `ENDPOINT` |
|----------|------------|-----------|------------|
| OpenAI | `openai` | Required | `https://api.openai.com/` |
| Azure | `azure` | Required | `https://<resource>.openai.azure.com/` |
| Gemini | `gemini` | Required | `https://generativelanguage.googleapis.com/` |
| Groq | `groq` | Required | `https://api.groq.com/openai/v1/` |
| OpenRouter | `openrouter` | Required | `https://openrouter.ai/api/v1/` |
| Mistral | `mistral` | Required | `https://api.mistral.ai/` |
| Ollama | `ollama` | Not needed | `http://localhost:11434` |

---

## Frequently Asked Questions

### Can I use Claude/Anthropic models?
Not directly. You can use OpenRouter as a proxy to access Claude models through an OpenAI-compatible interface. Direct Anthropic API support would require a separate implementation.

### Can I use multiple cloud providers in one config?
Not currently. Each NPC config can specify one local provider and one cloud provider. For multiple cloud providers, create separate NPC configs.

### What happens if I specify an unsupported provider?
The library will attempt to use the OpenAI-compatible format. This will fail with HTTP 404 or 400 errors if the provider doesn't support that format.

### How do I know which model names are valid?
Check the provider's documentation:
- OpenAI: https://platform.openai.com/docs/models
- Gemini: https://ai.google.dev/gemini-api/docs/models
- Azure: Check your Azure deployment names
- Groq: https://console.groq.com/docs/models
- Mistral: https://docs.mistral.ai/getting-started/models/

---

## Version History

### CI Build Versions (0.0.0-ci.*)
- **v0.0.0-ci.8 (d160fa7)** - 2025-12-08: Added demo screenshots to README
- **v0.0.0-ci.7 (694eca0)** - 2025-12-07: Fixed multiple provider and database bugs
- **v0.0.0-ci.6 (ae15659)** - 2025-11-29: Gemini API Key Authentication Bug - FIXED
- **v0.0.0-ci.5 (a012466)** - 2025-11-29: Updates on CloudProviderConfig and documents
- **v0.0.0-ci.4 (edb1577)** - 2025-11-29: Add Gemini provider support
- **v0.0.0-ci.3 (a6ebc12)** - 2025-11-29: Fix YAML deserialization for collection types
- **v0.0.0-ci.2 (9bb33d7)** - 2025-11-29: Fix critical YAML deserialization bug (records to classes)
- **v0.0.0-ci.1 (93f34ac)** - 2025-11-29: README NuGet update

### Stable Releases
- **v0.1.0** - 2025-12-09 (pending): First stable release; CI builds continue as `0.1.0-ci.*`

### Pre-CI Milestones
- **2025-11-29 (6bf7d0b):** Configure NuGet publishing with automatic CI/CD
- **2025-11-15:** Unity and Unreal Engine integration documentation
- **2025-11-09:** Streaming endpoint `/ask/stream` and importance-based routing
- **2025-11-01:** Qdrant filter fixes for collection scopes
- **2025-10-28:** Initial release with OpenAI, Azure, Groq, OpenRouter, Mistral support

---

## Contributing

To add support for a new provider, please:

1. Check if it's OpenAI-compatible (test with existing code first)
2. If custom implementation needed, create an issue describing the API format
3. Submit a PR with the new provider implementation
4. Update this compatibility guide

---

**Questions or Issues?** https://github.com/TowsifAhamed/GameRagKit/issues
