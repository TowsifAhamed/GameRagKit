namespace GameRagKit.Actions;

public sealed record ActionCall(string Name, IReadOnlyDictionary<string, string> Args);

public sealed record ActionParseResult(string CleanedText, IReadOnlyList<ActionCall> Actions, IReadOnlyList<string> Errors);
