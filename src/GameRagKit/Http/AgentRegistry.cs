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
