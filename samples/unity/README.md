# Unity Integration for GameRagKit

This directory contains ready-to-use C# scripts for integrating GameRagKit NPCs into your Unity game.

## Quick Start

### 1. Start GameRagKit Server

```bash
# In your GameRagKit directory
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  serve --config samples/example-npcs --port 5280
```

### 2. Add Scripts to Unity Project

1. Copy `NpcDialogueManager.cs` to your Unity project's `Assets/Scripts/` folder
2. Copy `ExampleNpcInteraction.cs` to see a working example

### 3. Create Dialogue UI

Create a GameObject with:
- **TMP_InputField** - For player to type questions
- **TextMeshProUGUI** - To display NPC responses
- **Button** - To submit questions

### 4. Attach Scripts

1. Create an empty GameObject named "GameRagKitManager"
2. Add the `NpcDialogueManager` component
3. Set `Server Url` to `http://localhost:5280`
4. Add `ExampleNpcInteraction` to your NPC GameObject or UI panel
5. Link the UI elements in the Inspector

## Components

### NpcDialogueManager.cs

The core component that handles HTTP communication with the GameRagKit server.

**Features:**
- ✅ Non-streaming responses (instant display)
- ✅ Streaming responses (typewriter effect)
- ✅ Error handling
- ✅ API key authentication support
- ✅ Server health checks

**Methods:**
```csharp
// Ask a question and get complete response
manager.AskNpc(
    npcId: "guard-north-gate",
    question: "What is your duty?",
    importance: 0.3f,
    onSuccess: response => {
        dialogueText.text = response.answer;
    },
    onError: error => {
        Debug.LogError(error);
    }
);

// Ask with streaming for typewriter effect
manager.AskNpcStreaming(
    npcId: "guard-north-gate",
    question: "Tell me about the keep",
    importance: 0.3f,
    onChunk: chunk => {
        dialogueText.text += chunk; // Append each chunk
    },
    onComplete: sources => {
        Debug.Log("Done!");
    }
);
```

### ExampleNpcInteraction.cs

A complete example showing how to create an NPC dialogue UI.

**Features:**
- Player input field
- NPC response display
- Streaming support with typewriter effect
- Importance-based routing
- Server health check on start
- Trigger-based dialogue initiation

## Usage Examples

### Basic Dialogue

```csharp
using GameRagKit.Unity;

public class MyNpcController : MonoBehaviour
{
    private NpcDialogueManager manager;

    void Start()
    {
        manager = FindObjectOfType<NpcDialogueManager>();

        // Ask a question
        manager.AskNpc("guard-north-gate", "Hello", 0.3f,
            response => {
                Debug.Log($"Guard says: {response.answer}");
            }
        );
    }
}
```

### Quest Dialogue with High Importance

```csharp
// Important story moment - use cloud for better quality
manager.AskNpc(
    npcId: "quest-giver",
    question: "What happened to the ancient artifact?",
    importance: 0.9f, // Routes to cloud
    onSuccess: response => {
        questLog.AddEntry(response.answer);
    }
);
```

### Proximity-Based Dialogue

```csharp
void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Player"))
    {
        manager.AskNpc("merchant", "Hello", 0.2f,
            response => ShowDialogueBubble(response.answer)
        );
    }
}
```

### Streaming Typewriter Effect

```csharp
string fullText = "";

manager.AskNpcStreaming(
    npcId: "storyteller",
    question: "Tell me the legend",
    importance: 0.5f,
    onChunk: chunk => {
        fullText += chunk;
        dialogueText.text = fullText;
        // Optionally add typing sound effect here
    },
    onComplete: sources => {
        Debug.Log("Story complete!");
    }
);
```

## API Reference

### Request Format

```json
{
  "npc": "guard-north-gate",
  "question": "What is your duty?",
  "importance": 0.3
}
```

### Response Format

```json
{
  "answer": "My duty is to guard the North Gate...",
  "sources": ["faction:royal_guard.md#1"],
  "scores": [0.87],
  "fromCloud": false,
  "responseTimeMs": 412
}
```

### Streaming Format (SSE)

```
data: {"type":"start","npc":"guard-north-gate"}

data: {"type":"chunk","text":"The keep "}

data: {"type":"chunk","text":"stands tall."}

data: {"type":"end","sources":["world:keep.md#0"]}
```

## Configuration

### Server URL

In Editor: Set via Inspector on `NpcDialogueManager`
```
http://localhost:5280
```

For builds, you may want to use a remote server:
```
https://your-game-server.com:5280
```

### API Key (Optional)

If your server requires authentication:
```csharp
manager.apiKey = "your-secret-key";
```

### Importance Levels

- **0.0 - 0.3**: Casual dialogue, local model (fast, free)
- **0.4 - 0.6**: Standard dialogue, may use cloud
- **0.7 - 1.0**: Critical quest moments, cloud model (best quality)

## Testing

### Test Server Connection

```csharp
manager.CheckHealth(healthy => {
    if (healthy) {
        Debug.Log("Server is ready!");
    } else {
        Debug.LogError("Cannot connect to GameRagKit server");
    }
});
```

### Example Output

```
[GameRagKit] Server health check: OK
[GameRagKit] Asking guard-north-gate: What is your duty?
[GameRagKit] NPC 'guard-north-gate' responded: My duty is to guard the North Gate...
[GameRagKit] Sources: faction:royal_guard.md#1, npc:guard-north-gate/notes.txt#0
[GameRagKit] From Cloud: False
```

## Requirements

- Unity 2020.3 or later
- TextMeshPro (for UI examples)
- GameRagKit server running

## Troubleshooting

### "Cannot connect to server"
- Verify server is running: `curl http://localhost:5280/health`
- Check firewall settings
- Ensure correct URL in Inspector

### Empty responses
- Check server logs for errors
- Verify NPC ID matches configuration
- Ensure lore was ingested successfully

### Streaming not working
- SSE requires persistent connection
- Some Unity versions have WebRequest limitations
- Consider polling `/ask` endpoint instead

## Performance Tips

- Cache the `NpcDialogueManager` reference
- Use lower importance for frequent/casual dialogue
- Consider pooling dialogue UI elements
- Implement request throttling for rapid interactions

## Next Steps

- See `ExampleNpcInteraction.cs` for a complete working example
- Check `/samples/example-npcs/` for NPC configuration examples
- Read main `/SETUP_GUIDE.md` for server configuration
