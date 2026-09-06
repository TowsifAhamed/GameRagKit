namespace GameRagKit.Actions;

public sealed record ActionCall(string Name, IReadOnlyDictionary<string, string> Args);

public sealed record ActionParseResult(IReadOnlyList<ActionCall> Actions, IReadOnlyList<string> Errors);
