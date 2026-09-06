using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GameRagKit;

namespace GameRagKit.Http;

public sealed class AgentRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpcAgent> _agents;

    public AgentRegistry(IEnumerable<KeyValuePair<string, NpcAgent>> agents)
    {
        _agents = new ConcurrentDictionary<string, NpcAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in agents)
        {
            _agents[pair.Key] = pair.Value;
        }
    }

    public bool TryGetAgent(string key, out NpcAgent agent)
        => _agents.TryGetValue(key, out agent!);

    /// <summary>
    /// Replaces every alias currently pointing at the NPC identified by newAgent.PersonaId
    /// (e.g. both the persona id and file-stem alias a single NPC is typically registered
    /// under) with newAgent. Used by the studio to hot-reload an NPC after a YAML edit
    /// without restarting the server. New lookups see the replacement immediately;
    /// ConcurrentDictionary key updates are atomic. The old agent is disposed after a
    /// grace period rather than immediately, since a request that called TryGetAgent just
    /// before the swap may still be mid-flight against it -- disposing out from under that
    /// call (e.g. closing its vector store connection) would break it.
    /// </summary>
    public Task ReplaceAgentAsync(NpcAgent newAgent, CancellationToken cancellationToken = default)
    {
        var keysToUpdate = _agents
            .Where(pair => string.Equals(pair.Value.PersonaId, newAgent.PersonaId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList();

        NpcAgent? oldAgent = null;
        foreach (var key in keysToUpdate)
        {
            if (_agents.TryGetValue(key, out var existing) && !ReferenceEquals(existing, newAgent))
            {
                oldAgent = existing;
            }

            _agents[key] = newAgent;
        }

        if (oldAgent is IAsyncDisposable disposable)
        {
            _ = DisposeAfterGracePeriodAsync(disposable);
        }

        return Task.CompletedTask;
    }

    private static async Task DisposeAfterGracePeriodAsync(IAsyncDisposable disposable)
    {
        await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup of a replaced agent; a failure here shouldn't surface
            // to the caller that triggered the hot-reload.
        }
    }

    public async ValueTask DisposeAsync()
    {
        var uniqueAgents = new HashSet<NpcAgent>(ReferenceEqualityComparer.Instance);
        foreach (var pair in _agents)
        {
            if (uniqueAgents.Add(pair.Value) && pair.Value is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<NpcAgent>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(NpcAgent? x, NpcAgent? y) => ReferenceEquals(x, y);

        public int GetHashCode(NpcAgent obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
