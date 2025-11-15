# Unreal Engine Integration for GameRagKit

This directory contains ready-to-use C++ components for integrating GameRagKit NPCs into your Unreal Engine game.

## Quick Start

### 1. Start GameRagKit Server

```bash
# In your GameRagKit directory
dotnet run --project src/GameRagKit.Cli/GameRagKit.Cli.csproj -- \
  serve --config samples/example-npcs --port 5280
```

### 2. Add Files to Unreal Project

1. Copy `NpcDialogueComponent.h` to `Source/YourGame/Public/`
2. Copy `NpcDialogueComponent.cpp` to `Source/YourGame/Private/`
3. Replace `YOURGAME_API` with your module's API macro
4. Add HTTP module to your `YourGame.Build.cs`:

```csharp
PublicDependencyModuleNames.AddRange(new string[] {
    "Core", "CoreUObject", "Engine", "Http", "Json", "JsonUtilities"
});
```

### 3. Add Component to Actor

1. Create a Blueprint based on Actor (e.g., `BP_NpcGuard`)
2. Add `NpcDialogueComponent` in Components panel
3. Set `Server Url` to `http://localhost:5280`

### 4. Use in Blueprints

The component exposes Blueprint-callable functions and events.

## Features

- ✅ Blueprint-friendly interface
- ✅ Non-streaming and streaming responses
- ✅ Event-driven architecture
- ✅ Automatic JSON parsing
- ✅ API key authentication support

## C++ Usage Example

```cpp
void AMyNpcActor::BeginPlay()
{
    NpcDialogue = FindComponentByClass<UNpcDialogueComponent>();
    NpcDialogue->OnResponseReceived.AddDynamic(this, &AMyNpcActor::HandleResponse);
    NpcDialogue->AskNpc("guard-north-gate", "What is your duty?", 0.3f);
}

void AMyNpcActor::HandleResponse(const FNpcResponse& Response)
{
    DialogueWidget->SetText(FText::FromString(Response.Answer));
}
```

See full documentation in this README for complete examples, Blueprint usage, and API reference.
