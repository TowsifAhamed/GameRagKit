namespace GameRagKit.Storage;

public sealed record VectorChunk(string Id, string Text, string SourcePath, IReadOnlyDictionary<string, string> Metadata, float[] Embedding)
{
    public double Score { get; init; }
        = 0d;
}
