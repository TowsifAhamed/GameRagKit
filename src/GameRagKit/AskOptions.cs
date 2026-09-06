using GameRagKit.Actions;

namespace GameRagKit;

public sealed record AskOptions(
    int TopK = 4,
    bool InCharacter = true,
    string? SystemOverride = null,
    double Importance = double.NaN,
    bool ForceLocal = false,
    bool ForceCloud = false,
    string? State = null,
    WorldState? WorldState = null);

public sealed record AgentReply(string Text, string[] Sources, double[] Scores, bool FromCloud)
{
    public IReadOnlyList<ActionCall> Actions { get; init; } = Array.Empty<ActionCall>();
}
