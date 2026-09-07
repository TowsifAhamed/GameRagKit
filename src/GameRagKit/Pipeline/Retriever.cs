using System.Collections.Generic;
using System.Linq;
using GameRagKit.Config;
using GameRagKit.Storage;
using GameRagKit.VectorStores;

namespace GameRagKit.Pipeline;

public sealed class Retriever
{
    private readonly IVectorStore _vectorStore;
    private readonly PersonaConfig _persona;
    private readonly IReadOnlyDictionary<string, string>? _filters;

    public Retriever(IVectorStore vectorStore, PersonaConfig persona, IReadOnlyDictionary<string, string>? filters)
    {
        _vectorStore = vectorStore;
        _persona = persona;
        _filters = filters;
    }

    public async Task<IReadOnlyList<RagHit>> RetrieveAsync(float[] embedding, int topK, CancellationToken ct)
    {
        var scopes = BuildScopes(topK);
        var results = new List<RagHit>();

        foreach (var scope in scopes)
        {
            if (scope.Limit <= 0)
            {
                continue;
            }

            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["collection"] = scope.Collection
            };

            if (_filters != null)
            {
                foreach (var pair in _filters)
                {
                    filters[pair.Key] = pair.Value;
                }
            }

            var hits = await _vectorStore.SearchAsync(embedding, scope.Limit, filters, ct).ConfigureAwait(false);
            results.AddRange(hits);
        }

        return results
            .OrderByDescending(hit => hit.Score ?? double.MinValue)
            .Take(topK)
            .ToArray();
    }

    private IReadOnlyList<(string Collection, int Limit)> BuildScopes(int topK)
    {
        var scopes = new List<(string Collection, int Limit)>();
        var tiers = _persona.TierPath;

        for (var i = 0; i < tiers.Count; i++)
        {
            // Outermost tier (e.g. world) contributes least, tiers closer to the NPC
            // contribute more -- matches the original world=2/region=1/faction=1 weighting,
            // generalized to any depth: the tier closest to npc gets 2, everything above it 1.
            var limit = i == tiers.Count - 1 ? 2 : 1;
            scopes.Add((IndexScopeKey.ForTier(_persona, tiers[i].Name).Scope, limit));
        }

        scopes.Add((IndexScopeKey.ForPersona(_persona).Scope, topK));
        scopes.Add((IndexScopeKey.ForMemory(_persona).Scope, 1));

        return scopes;
    }
}
