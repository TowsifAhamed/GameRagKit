# GameRagKit - Critical Bug Fix & Documentation Gaps

## TL;DR for Maintainer

**Critical Bug FIXED:** YamlDotNet couldn't deserialize `NpcConfig` because it was a `record` without a parameterless constructor. Changed to `class` with init properties.

**What was broken:** Library completely unusable - couldn't load ANY configuration files
**What's fixed:** Changed one file ([NpcConfig.cs](src/GameRagKit/Config/NpcConfig.cs))
**Test status:** ✅ All 16 unit tests pass

---

## The Bug (Now Fixed)

### Before (Broken)
```csharp
public sealed record NpcConfig(PersonaConfig Persona, RagConfig Rag, ProvidersConfig Providers)
```

**Error:**
```
YamlDotNet.Core.YamlException: Cannot create an instance of type 'GameRagKit.Config.NpcConfig'.
Reason: No parameterless constructor defined.
```

### After (Fixed)
```csharp
public sealed class NpcConfig
{
    public PersonaConfig Persona { get; init; } = new();
    public RagConfig Rag { get; init; } = new();
    public ProvidersConfig Providers { get; init; } = new();

    // ... rest of code unchanged
}
```

**Why it works:** YamlDotNet can now create instances with parameterless constructor.

---

## Missing Documentation (Actionable List)

### 1. Add `docker-compose.yml` to Repository Root
Users need a database but don't know how to set it up.

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: gamerag
      POSTGRES_USER: gamerag
      POSTGRES_PASSWORD: gamerag123
    ports:
      - "5432:5432"
```

### 2. Add `.env.example` to Samples
No one knows what environment variables to set.

```bash
PROVIDER=gemini
API_KEY=your-key-here
ENDPOINT=https://generativelanguage.googleapis.com/
DB_CONNECTION_STRING="Server=localhost;Port=5432;Database=gamerag;User Id=gamerag;Password=gamerag123;"
```

### 3. Update README - Provider Configuration Table
README only shows OpenAI. Add this table:

| Provider | `provider` | Example `chat_model` | Example `embed_model` | `endpoint` |
|----------|------------|---------------------|---------------------|------------|
| Gemini   | `gemini`   | `gemini-1.5-flash`  | `embedding-001`     | `https://generativelanguage.googleapis.com/` |
| OpenAI   | `openai`   | `gpt-4o`            | `text-embedding-3-small` | `https://api.openai.com/` |
| Ollama   | `ollama`   | `llama3`            | `nomic-embed-text`  | `http://localhost:11434` |

### 4. Update README - Prerequisites Section
Add upfront:
```markdown
## Prerequisites
- .NET 8.0 SDK
- PostgreSQL 16+ OR Qdrant 1.x+ (for vector storage)
- API key from: OpenAI, Google (Gemini), Azure, Mistral, or HuggingFace
```

### 5. Add Troubleshooting Section
Common errors with fixes:
- "Cannot create instance" → Update to latest version
- "Connection refused" → Check database is running
- "Invalid API key" → Verify `API_KEY` env var

---

## What I Tested

✅ Built library successfully
✅ All 16 unit tests pass
✅ YAML deserialization works
✅ Tested with Gemini provider configuration
✅ PostgreSQL database connection works

---

## Files Changed

1. [src/GameRagKit/Config/NpcConfig.cs](src/GameRagKit/Config/NpcConfig.cs) - Changed record to class
2. [tests/GameRagKit.Tests/Routing/RouterTests.cs](tests/GameRagKit.Tests/Routing/RouterTests.cs) - Updated instantiation syntax

---

## Recommendation for Next Release

1. ✅ **Merge the fix** (already done, ready to push)
2. Add the 5 documentation items above
3. Create example config files in `examples/` directory:
   - `examples/gemini.yaml`
   - `examples/openai.yaml`
   - `examples/ollama-local.yaml`
4. Publish as stable `0.1.0` instead of `ci.*` versions

---

## Note on CI/CD

This repo has automated CI/CD - pushing these changes will auto-publish to NuGet. No manual packing needed.

---

**Full detailed analysis:** See [ISSUES_AND_IMPROVEMENTS.md](ISSUES_AND_IMPROVEMENTS.md)
