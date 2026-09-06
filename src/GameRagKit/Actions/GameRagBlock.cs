namespace GameRagKit.Actions;

/// <summary>
/// A raw structured-data block extracted from a model reply, before tag-specific JSON
/// validation. See <see cref="GameRagBlockParser"/> for the wire format.
/// </summary>
public sealed record GameRagBlock(string Tag, string RawJson);

public sealed record GameRagBlockParseResult(string CleanedText, IReadOnlyList<GameRagBlock> Blocks);
