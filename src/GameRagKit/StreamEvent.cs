using GameRagKit.Actions;
using GameRagKit.Mood;

namespace GameRagKit;

public abstract record StreamEvent
{
    public sealed record Start(string Npc) : StreamEvent;

    public sealed record Chunk(string Text) : StreamEvent;

    public sealed record End(string[] Sources, IReadOnlyList<ActionCall> Actions, MoodState? Mood = null) : StreamEvent;
}
