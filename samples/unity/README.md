# Unity sample

This folder contains a lightweight Unity integration outline. The core steps:

1. Drop the `GameRagKit` library into a Unity project (via UPM Git dependency or direct DLL).
2. Create a `NpcBootstrapper` script that calls `GameRAGKit.Load`, `UseEnv`, and `EnsureIndexAsync` on `Start`.
3. Wire an async method that forwards player lines to `AskAsync` and updates UI (subtitle text, VO triggers).
4. Optionally buffer responses and display their `Sources` for QA.

See `NpcAgentExample.cs` for a simple behaviour script that can be attached to an NPC prefab.
