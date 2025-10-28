namespace GameRagKit;

public sealed record AskOptions(
    int TopK = 4,
    bool InCharacter = true,
    string? SystemOverride = null,
    double Importance = 0.2,
    bool ForceLocal = false,
    bool ForceCloud = false);

public sealed record AgentReply(string Text, string[] Sources, double[] Scores, bool FromCloud);
