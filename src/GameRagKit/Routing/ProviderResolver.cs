using System.Net.Http.Headers;
using GameRagKit.Config;
using GameRagKit.Providers;

namespace GameRagKit.Routing;

public sealed class ProviderResolver
{
    public Task<IChatProvider?> TryCreateLocalChatAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        if (config.Providers.Local is null)
        {
            return Task.FromResult<IChatProvider?>(null);
        }

        var endpoint = runtimeOptions.LocalEndpoint ?? config.Providers.Local.Endpoint;
        var model = runtimeOptions.LocalChatModel ?? config.Providers.Local.ChatModel;
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            return Task.FromResult<IChatProvider?>(null);
        }

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };

        return Task.FromResult<IChatProvider?>(new OllamaChatProvider(httpClient, model));
    }

    public Task<IEmbeddingProvider?> TryCreateLocalEmbeddingAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        if (config.Providers.Local is null)
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var endpoint = runtimeOptions.LocalEndpoint ?? config.Providers.Local.Endpoint;
        var model = runtimeOptions.LocalEmbedModel ?? config.Providers.Local.EmbedModel;
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };

        return Task.FromResult<IEmbeddingProvider?>(new OllamaEmbeddingProvider(httpClient, model));
    }

    public Task<IChatProvider?> TryCreateCloudChatAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        if (config.Providers.Cloud is null)
        {
            return Task.FromResult<IChatProvider?>(null);
        }

        var endpoint = runtimeOptions.CloudEndpoint ?? config.Providers.Cloud.Endpoint ?? "https://api.openai.com/";
        var model = runtimeOptions.CloudChatModel ?? config.Providers.Cloud.ChatModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Task.FromResult<IChatProvider?>(null);
        }

        var providerName = runtimeOptions.CloudProvider ?? "openai";
        var httpClient = CreateHttpClient(endpoint, runtimeOptions.CloudApiKey, providerName);
        return Task.FromResult<IChatProvider?>(new CloudChatProvider(httpClient, providerName, model));
    }

    public Task<IEmbeddingProvider?> TryCreateCloudEmbeddingAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        if (config.Providers.Cloud is null)
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var endpoint = runtimeOptions.CloudEndpoint ?? config.Providers.Cloud.Endpoint ?? "https://api.openai.com/";
        var model = runtimeOptions.CloudEmbedModel ?? config.Providers.Cloud.EmbedModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var providerName = runtimeOptions.CloudProvider ?? "openai";
        var httpClient = CreateHttpClient(endpoint, runtimeOptions.CloudApiKey, providerName);
        return Task.FromResult<IEmbeddingProvider?>(new CloudEmbeddingProvider(httpClient, providerName, model));
    }

    private static HttpClient CreateHttpClient(string endpoint, string? apiKey, string provider)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
            {
                httpClient.DefaultRequestHeaders.Remove("api-key");
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            }
            else
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        httpClient.Timeout = TimeSpan.FromSeconds(60);
        return httpClient;
    }
}
