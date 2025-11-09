using System;
using GameRagKit.Config;
using GameRagKit.Providers;
using GameRagKit.Routing;

namespace GameRagKit.Pipeline;

public sealed class Router
{
    private readonly ProviderResolver _resolver;

    public Router(ProviderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<IEmbeddingProvider> ResolveEmbedderAsync(NpcConfig config, ProviderRuntimeOptions runtime, CancellationToken ct)
    {
        var local = await _resolver.TryCreateLocalEmbeddingAsync(config, runtime, ct).ConfigureAwait(false);
        if (local != null)
        {
            return local;
        }

        var cloud = await _resolver.TryCreateCloudEmbeddingAsync(config, runtime, ct).ConfigureAwait(false);
        return cloud ?? throw new InvalidOperationException("No embedding provider configured.");
    }

    public async Task<IChatProvider> ResolveChatAsync(NpcConfig config, ProviderRuntimeOptions runtime, AskOptions options, CancellationToken ct)
    {
        if (options.ForceLocal)
        {
            var forcedLocal = await _resolver.TryCreateLocalChatAsync(config, runtime, ct).ConfigureAwait(false);
            return Require(forcedLocal, "Local chat provider is not configured.");
        }

        if (options.ForceCloud)
        {
            var forcedCloud = await _resolver.TryCreateCloudChatAsync(config, runtime, ct).ConfigureAwait(false);
            return Require(forcedCloud, "Cloud chat provider is not configured.");
        }

        var mode = config.Providers.Routing.Mode;
        if (string.Equals(mode, "local_only", StringComparison.OrdinalIgnoreCase))
        {
            var localOnly = await _resolver.TryCreateLocalChatAsync(config, runtime, ct).ConfigureAwait(false);
            return Require(localOnly, "Local chat provider is not configured.");
        }

        if (string.Equals(mode, "cloud_only", StringComparison.OrdinalIgnoreCase))
        {
            var cloudOnly = await _resolver.TryCreateCloudChatAsync(config, runtime, ct).ConfigureAwait(false);
            return Require(cloudOnly, "Cloud chat provider is not configured.");
        }

        var importance = options.Importance;
        if (double.IsNaN(importance))
        {
            importance = config.Persona.DefaultImportance ?? config.Providers.Routing.DefaultImportance;
        }
        else
        {
            importance = Math.Clamp(importance, 0d, 1d);
        }

        var local = await _resolver.TryCreateLocalChatAsync(config, runtime, ct).ConfigureAwait(false);
        var cloud = await _resolver.TryCreateCloudChatAsync(config, runtime, ct).ConfigureAwait(false);

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

    private static IChatProvider Require(IChatProvider? provider, string message)
        => provider ?? throw new InvalidOperationException(message);
}
