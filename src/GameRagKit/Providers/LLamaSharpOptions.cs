using System.Collections.Generic;

namespace GameRagKit.Providers;

public sealed record LLamaSharpOptions
{
    public required string ModelPath { get; init; }
    public string? EmbedModelPath { get; init; }
    public int ContextSize { get; init; } = 4096;
    public int EmbeddingContextSize { get; init; } = 1024;
    public int GpuLayerCount { get; init; } = 0;
    public int? Threads { get; init; }
        = null;
    public int? BatchThreads { get; init; }
        = null;
    public uint BatchSize { get; init; } = 512;
    public uint MicroBatchSize { get; init; } = 512;
    public int MaxTokens { get; init; } = 256;
    public IReadOnlyList<string> StopSequences { get; init; } = new List<string>();
}
