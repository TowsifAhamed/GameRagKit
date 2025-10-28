using GameRagKit.Config;
using GameRagKit.Providers;

namespace GameRagKit.Routing;

public sealed class ChatRouter
{
    private readonly ProviderResolver _resolver;

    public ChatRouter(ProviderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<IChatProvider> RouteAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, AskOptions options, CancellationToken cancellationToken)
    {
        if (options.ForceLocal)
        {
            var forcedLocal = await _resolver.TryCreateLocalChatAsync(config, runtimeOptions, cancellationToken)
                              ?? throw new InvalidOperationException("Local chat provider is not configured.");
            return forcedLocal;
        }

        if (options.ForceCloud)
        {
            var forcedCloud = await _resolver.TryCreateCloudChatAsync(config, runtimeOptions, cancellationToken)
                              ?? throw new InvalidOperationException("Cloud chat provider is not configured.");
            return forcedCloud;
        }

        var mode = config.Providers.Routing.Mode;
        switch (mode)
        {
            case "local_only":
                return await _resolver.TryCreateLocalChatAsync(config, runtimeOptions, cancellationToken)
                       ?? throw new InvalidOperationException("Local chat provider is not configured.");
            case "cloud_only":
                return await _resolver.TryCreateCloudChatAsync(config, runtimeOptions, cancellationToken)
                       ?? throw new InvalidOperationException("Cloud chat provider is not configured.");
            default:
                break;
        }

        var importance = options.Importance;
        if (importance <= 0 && config.Persona.DefaultImportance.HasValue)
        {
            importance = config.Persona.DefaultImportance.Value;
        }

        if (importance <= 0)
        {
            importance = config.Providers.Routing.DefaultImportance;
        }

        var local = await _resolver.TryCreateLocalChatAsync(config, runtimeOptions, cancellationToken);
        var cloud = await _resolver.TryCreateCloudChatAsync(config, runtimeOptions, cancellationToken);

        if (local == null && cloud == null)
        {
            throw new InvalidOperationException("No chat providers are configured.");
        }

        if (cloud == null)
        {
            return local!;
        }

        if (local == null)
        {
            return cloud;
        }

        return importance >= 0.5 ? cloud : local;
    }

    public async Task<IEmbeddingProvider> ResolveEmbeddingProviderAsync(NpcConfig config, ProviderRuntimeOptions runtimeOptions, CancellationToken cancellationToken)
    {
        var local = await _resolver.TryCreateLocalEmbeddingAsync(config, runtimeOptions, cancellationToken);
        if (local != null)
        {
            return local;
        }

        var cloud = await _resolver.TryCreateCloudEmbeddingAsync(config, runtimeOptions, cancellationToken)
                    ?? throw new InvalidOperationException("No embedding provider configured.");
        return cloud;
    }
}
