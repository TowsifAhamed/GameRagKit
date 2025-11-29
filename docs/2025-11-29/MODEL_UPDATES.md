# Model Updates - 2025 Latest Versions

This document tracks the model updates made on November 29, 2025 to use the latest available models from each provider.

## Summary of Changes

All example configurations and documentation have been updated to use the latest models available as of November 2025.

---

## Google Gemini Updates

### Previous (Deprecated)
```yaml
cloud:
  provider: gemini
  chat_model: gemini-1.5-flash      # ❌ OLD
  embed_model: embedding-001         # ❌ OLD
```

### Updated (Latest 2025)
```yaml
cloud:
  provider: gemini
  chat_model: gemini-2.0-flash-exp  # ✅ NEW - Fast, cost-effective (Jan 2025)
  # OR: gemini-2.5-flash            # ✅ NEW - Enhanced reasoning (June 2025)
  # OR: gemini-2.5-pro              # ✅ NEW - Most capable (March 2025)
  embed_model: text-embedding-004    # ✅ NEW
```

**What's New:**
- Gemini 2.0 Flash released January 2025 - higher performance, better rate limits
- Gemini 2.5 Flash released June 2025 - enhanced for large-scale processing
- Gemini 2.5 Pro released March 2025 - 1M token context, thinking capabilities
- text-embedding-004 - Latest embedding model

**Sources:**
- [Gemini 2.0 Flash Announcement](https://developers.googleblog.com/en/gemini-2-family-expands/)
- [Gemini 2.5 Updates](https://developers.googleblog.com/en/continuing-to-bring-you-our-latest-models-with-an-improved-gemini-2-5-flash-and-flash-lite-release/)
- [Model Documentation](https://ai.google.dev/gemini-api/docs/models)

---

## OpenAI Updates

### Previous
```yaml
cloud:
  provider: openai
  chat_model: gpt-4o-mini              # ❌ Still valid, but newer available
  embed_model: text-embedding-3-small  # ❌ Smaller model
```

### Updated (Latest 2025)
```yaml
cloud:
  provider: openai
  chat_model: gpt-4.1              # ✅ NEW - Best performance (2025)
  # OR: gpt-4.1-mini               # ✅ NEW - Fast, cost-effective
  # OR: gpt-4o                     # ✅ Still available, very capable
  embed_model: text-embedding-3-large  # ✅ UPGRADED - 3072 dimensions
  # OR: text-embedding-3-small         # ✅ Available - 1536 dimensions (cheaper)
```

**What's New:**
- GPT-4.1 released 2025 - outperforms GPT-4o across the board
- GPT-4.1 mini - cost-effective with major improvements
- 1M token context window
- Refreshed knowledge cutoff (June 2024)
- text-embedding-3-large - Best quality embeddings (3072 dims)

**Sources:**
- [GPT-4.1 Launch](https://openai.com/index/gpt-4-1/)
- [Model Release Notes](https://help.openai.com/en/articles/9624314-model-release-notes)
- [Text Embedding Models](https://platform.openai.com/docs/models/text-embedding-3-large)

---

## Ollama (Local) - No Changes

### Current (Still Recommended)
```yaml
local:
  engine: ollama
  chat_model: llama3.2:3b-instruct-q4_K_M  # ✅ CURRENT - Fast, quantized
  embed_model: nomic-embed-text             # ✅ CURRENT - High quality
```

**Why No Changes:**
- llama3.2:3b-instruct-q4_K_M - Still the best quantized small model
- nomic-embed-text - Still the recommended embedding model for Ollama

---

## Model Comparison (2025)

| Provider | Chat Model | Embedding Model | Context | Cost | Best For |
|----------|-----------|-----------------|---------|------|----------|
| **Gemini** | gemini-2.0-flash-exp | text-embedding-004 | ~1M tokens | 💰 Low | Cost-effective cloud |
| **Gemini** | gemini-2.5-pro | text-embedding-004 | 1M tokens | 💰💰 Medium | High-quality reasoning |
| **OpenAI** | gpt-4.1 | text-embedding-3-large | 1M tokens | 💰💰💰 High | Best performance |
| **OpenAI** | gpt-4.1-mini | text-embedding-3-small | 1M tokens | 💰 Low | Fast, cost-effective |
| **Ollama** | llama3.2:3b | nomic-embed-text | ~8K tokens | 🆓 Free | Fully offline, privacy |

---

## Files Updated

All the following files were updated with the latest model names:

### Configuration Examples
- ✅ `examples/configs/gemini-example.yaml`
- ✅ `examples/configs/openai-example.yaml`
- ✅ `examples/configs/hybrid-example.yaml`
- ✅ `examples/configs/README.md`

### Documentation
- ✅ `.env.example`
- ✅ `README.md`

### Verification
All model names verified against official documentation as of November 29, 2025.

---

## How to Use Latest Models

### Quick Update
Copy the example configs:
```bash
cp examples/configs/gemini-example.yaml NPCs/my-npc.yaml
# OR
cp examples/configs/openai-example.yaml NPCs/my-npc.yaml
```

### Manual Update
Update your existing YAML files:

**For Gemini:**
```yaml
cloud:
  provider: gemini
  chat_model: gemini-2.0-flash-exp
  embed_model: text-embedding-004
```

**For OpenAI:**
```yaml
cloud:
  provider: openai
  chat_model: gpt-4.1
  embed_model: text-embedding-3-large
```

---

## API Key Sources

- **Gemini:** https://aistudio.google.com/apikey
- **OpenAI:** https://platform.openai.com/api-keys

---

**Updated:** November 29, 2025
**Verified:** All model names confirmed from official documentation
