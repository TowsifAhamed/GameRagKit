namespace GameRagKit.Providers;

public sealed class ProviderRuntimeOptions
{
    public string? CloudApiKey { get; set; }
    public string? CloudProvider { get; set; }
    public string? CloudEndpoint { get; set; }
    public string? CloudChatModel { get; set; }
    public string? CloudEmbedModel { get; set; }
    public string? LocalEndpoint { get; set; }
    public string? LocalEngine { get; set; }
    public string? LocalChatModel { get; set; }
    public string? LocalEmbedModel { get; set; }
    public string? LocalModelPath { get; set; }
    public string? LocalEmbedModelPath { get; set; }
    public int? LocalContextSize { get; set; }
    public int? LocalEmbeddingContextSize { get; set; }
    public int? LocalGpuLayerCount { get; set; }
    public int? LocalThreads { get; set; }
    public int? LocalBatchThreads { get; set; }
    public uint? LocalBatchSize { get; set; }
    public uint? LocalMicroBatchSize { get; set; }
    public int? LocalMaxTokens { get; set; }
}
