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
}
