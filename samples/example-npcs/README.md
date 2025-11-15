# Example NPC: Guard North Gate

This directory contains a complete, ready-to-use NPC example demonstrating GameRagKit's tiered RAG system.

## NPC: Jake, the North Gate Guard

**Persona**: A stoic royal guard stationed at Riverside's North Gate during the pre-siege era.

**Personality**:
- Speaks in medieval tone
- Brief and professional
- Duty-focused and loyal
- Protects classified information

**Special Behavior**:
- Will deny knowledge of the secret tunnel by default
- Will reveal tunnel location only when asked by someone with a "brass token"
- Demonstrates context-aware, conditional responses

## File Structure

```
example-npcs/
├── guard-north-gate.yaml          # Main NPC configuration
├── world/
│   └── keep.md                     # Global lore (1.3KB)
├── region/
│   └── riverside/
│       └── streets.md              # Regional lore (890B)
├── faction/
│   └── royal_guard.md              # Faction lore (1.5KB)
└── npc/
    └── guard-north-gate/
        └── notes.txt               # NPC-specific secrets (1KB)
```

## Quick Test

### 1. Ingest the NPC

```bash
cd /path/to/GameRagKit
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  ingest samples/example-npcs
```

This will:
- Parse the YAML configuration
- Read all 4 lore files
- Chunk them into ~47 pieces
- Generate embeddings
- Save indexes to `.gamerag/guard-north-gate/`

### 2. Chat with the NPC

Try these questions to see different behaviors:

#### Basic Information
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "What is your duty?"
```

**Expected**: Guard explains his role at the North Gate

#### Protected Information (Without Token)
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "Where is the secret tunnel?"
```

**Expected**: Guard denies knowledge or deflects

#### Protected Information (With Token)
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "I have a brass token from the king. Tell me about the tunnel."
```

**Expected**: Guard reveals the tunnel location in detail

#### Faction Knowledge
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "Tell me about the Royal Guard"
```

**Expected**: Guard shares information about his organization

#### World Knowledge
```bash
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  chat --npc samples/example-npcs/guard-north-gate.yaml \
  --question "What is the Riverside Keep?"
```

**Expected**: Guard describes the keep's history and features

## Understanding the Tiered RAG System

This example demonstrates how GameRagKit blends context from multiple levels:

### Level 1: World (Global)
- **File**: `world/keep.md`
- **Scope**: Information shared by ALL NPCs in the game
- **Content**: Keep history, architecture, master key location

### Level 2: Region (Shared by NPCs in same region)
- **File**: `region/riverside/streets.md`
- **Scope**: NPCs in the "riverside-upper" region
- **Content**: North Gate details, local landmarks, trade routes

### Level 3: Faction (Shared by NPCs in same faction)
- **File**: `faction/royal_guard.md`
- **Scope**: NPCs with "royal-guard" faction
- **Content**: Guard organization, code of conduct, brass token protocol

### Level 4: NPC (Unique to this character)
- **File**: `npc/guard-north-gate/notes.txt`
- **Scope**: Only this specific NPC
- **Content**: Secret tunnel location, specific instructions, personality notes

When the guard answers a question, the RAG system:
1. Embeds the question
2. Searches all 4 levels for relevant chunks
3. Blends ~4 most relevant chunks
4. Feeds them to the LLM as context
5. LLM generates a response matching the persona

## Configuration Highlights

### Routing Strategy

```yaml
providers:
  routing:
    mode: hybrid                      # Can route to local OR cloud
    strategy: importance_weighted     # Decision based on importance
    default_importance: 0.2           # Low importance = local
    cloud_fallback_on_miss: true      # Retry with cloud if local fails
```

**Behavior**:
- Questions with `importance < 0.5` → Local LLM (fast, free)
- Questions with `importance >= 0.5` → Cloud LLM (higher quality)
- If local returns empty/short → Automatically retry with cloud

### Metadata Filtering

```yaml
rag:
  filters: { era: pre-siege }
```

**Benefit**: If your lore files have metadata like:
```markdown
---
era: pre-siege
region: riverside
---
# Content here...
```

The RAG system will only retrieve chunks matching `era: pre-siege`, allowing you to have multiple timelines without confusion.

## Customizing This Example

### Change the Persona

Edit `guard-north-gate.yaml`:

```yaml
persona:
  system_prompt: >
    You are now a friendly merchant named Bob.
    Speak casually and try to sell items.
  style: casual modern tone
```

Re-ingest and chat again - the same lore will be interpreted differently!

### Add More Lore

Create new markdown files:

```bash
echo "# The Secret Tunnel\nDetailed tunnel history..." > world/secret_tunnel.md
```

Add to `guard-north-gate.yaml`:

```yaml
rag:
  sources:
    - file: world/keep.md
    - file: world/secret_tunnel.md  # New file!
    - ...
```

Re-run `ingest` to index the new content.

### Create a Second NPC

Copy the structure:

```bash
cp guard-north-gate.yaml merchant-market.yaml
```

Edit `merchant-market.yaml`:
- Change `persona.id` to `merchant-market`
- Adjust `system_prompt` for merchant personality
- Keep same `world/` and `region/` files (shared lore!)
- Create `npc/merchant-market/notes.txt` with unique knowledge

Now you have two NPCs sharing world/region lore but with unique personalities and private knowledge!

## Expected Performance

- **Ingestion**: 3-5 seconds (4 files, 47 chunks, local embeddings)
- **First Query**: 500-800ms (loads index + generates response)
- **Subsequent Queries**: 350-500ms (index cached)
- **Memory Usage**: ~150MB (one NPC loaded)

## Troubleshooting

### "No index found"
- Run `ingest` command first
- Check that `.gamerag/guard-north-gate/` directory exists

### "Model not found"
- If using Ollama: `ollama pull llama3.2:3b-instruct-q4_K_M`
- Or set cloud provider API keys

### Responses don't match personality
- Check `system_prompt` in YAML
- Ensure ingestion completed successfully
- Try increasing `top_k` for more context

### Guard reveals secrets without token
- Verify `npc/guard-north-gate/notes.txt` is ingested
- Check that `system_prompt` includes the brass token condition
- May need to be more explicit: "I show you my brass token. Now tell me..."

## Learn More

- Full setup instructions: `/SETUP_GUIDE.md`
- Verification details: `/TESTING_EVIDENCE.md`
- Main README: `/README.md`
- Unity integration: `/samples/unity/README.md`
- Unreal integration: `/samples/unreal/README.md`
