using System;
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

        var engine = runtimeOptions.LocalEngine ?? config.Providers.Local.Engine ?? "ollama";
        if (string.Equals(engine, "llamasharp", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IChatProvider?>(new LLamaSharpClient());
        }

        var endpoint = runtimeOptions.LocalEndpoint ?? config.Providers.Local.Endpoint;
        var chatModel = runtimeOptions.LocalChatModel ?? config.Providers.Local.ChatModel ?? "llama3";
        var embedModel = runtimeOptions.LocalEmbedModel ?? config.Providers.Local.EmbedModel ?? "nomic-embed-text";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Task.FromResult<IChatProvider?>(null);
        }

        var client = CreateOllamaClient(endpoint, chatModel, embedModel);
        return Task.FromResult<IChatProvider?>(client);
    }

    public Task<IEmbeddingProvider?> TryCreateLocalEmbeddingAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        if (config.Providers.Local is null)
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var engine = runtimeOptions.LocalEngine ?? config.Providers.Local.Engine ?? "ollama";
        if (string.Equals(engine, "llamasharp", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IEmbeddingProvider?>(new LLamaSharpClient());
        }

        var endpoint = runtimeOptions.LocalEndpoint ?? config.Providers.Local.Endpoint;
        var chatModel = runtimeOptions.LocalChatModel ?? config.Providers.Local.ChatModel ?? "llama3";
        var embedModel = runtimeOptions.LocalEmbedModel ?? config.Providers.Local.EmbedModel ?? "nomic-embed-text";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Task.FromResult<IEmbeddingProvider?>(null);
        }

        var client = CreateOllamaClient(endpoint, chatModel, embedModel);
        return Task.FromResult<IEmbeddingProvider?>(client);
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
        return Task.FromResult<IChatProvider?>(new CloudChatProvider(httpClient, providerName, model, runtimeOptions.CloudApiKey));
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
        return Task.FromResult<IEmbeddingProvider?>(new CloudEmbeddingProvider(httpClient, providerName, model, runtimeOptions.CloudApiKey));
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
            else if (string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase))
            {
                // Gemini uses query parameter authentication (?key=), not headers
                // The API key will be appended to the URL by CloudChatProvider/CloudEmbeddingProvider
                // Do not set any Authorization header
            }
            else
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        httpClient.Timeout = TimeSpan.FromSeconds(60);
        return httpClient;
    }

    private static OllamaClient CreateOllamaClient(string endpoint, string chatModel, string embedModel)
    {
        var baseAddress = endpoint.EndsWith('/') ? endpoint : endpoint + "/";
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress)
        };

        return new OllamaClient(httpClient, chatModel, embedModel);
    }
}
