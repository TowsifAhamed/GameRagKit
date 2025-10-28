using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using GameRagKit.Config;
using GameRagKit.Providers;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;

namespace GameRagKit;

public sealed class NpcAgent
{
    private readonly NpcConfig _config;
    private readonly string _configDirectory;
    private readonly TextChunker _chunker;
    private readonly VectorIndexRepository _indexRepository;
    private readonly ChatRouter _router;
    private readonly ProviderRuntimeOptions _runtimeOptions = new();
    private readonly ConcurrentDictionary<IndexScopeKey, VectorIndex> _loadedIndexes = new();
    private IEmbeddingProvider? _embeddingProvider;

    public string PersonaId => _config.Persona.Id;
    public double DefaultImportance => _config.Persona.DefaultImportance ?? _config.Providers.Routing.DefaultImportance;

    internal NpcAgent(
        NpcConfig config,
        string configDirectory,
        TextChunker chunker,
        VectorIndexRepository indexRepository,
        ChatRouter router)
    {
        _config = config;
        _configDirectory = configDirectory;
        _chunker = chunker;
        _indexRepository = indexRepository;
        _router = router;
    }

    public NpcAgent UseEnv()
    {
        _runtimeOptions.CloudProvider = Environment.GetEnvironmentVariable("PROVIDER") ?? _config.Providers.Cloud?.Provider ?? "openai";
        _runtimeOptions.CloudApiKey = Environment.GetEnvironmentVariable("API_KEY");
        _runtimeOptions.CloudEndpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? _config.Providers.Cloud?.Endpoint;
        _runtimeOptions.LocalEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? _config.Providers.Local?.Endpoint;
        _runtimeOptions.LocalEngine = _config.Providers.Local?.Engine ?? "ollama";
        _runtimeOptions.LocalChatModel = _config.Providers.Local?.ChatModel;
        _runtimeOptions.LocalEmbedModel = _config.Providers.Local?.EmbedModel;
        _runtimeOptions.CloudChatModel = _config.Providers.Cloud?.ChatModel;
        _runtimeOptions.CloudEmbedModel = _config.Providers.Cloud?.EmbedModel;

        return this;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await _indexRepository.LoadManifestAsync(_config.Persona.Id, cancellationToken);

        foreach (var source in _config.Rag.Sources)
        {
            var sourcePath = Path.Combine(_configDirectory, source.File);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            var hash = VectorIndexRepository.ComputeHash(text);
            var scope = IndexScopeKey.FromSource(_config.Persona, source);
            var index = await GetOrLoadIndexAsync(scope, cancellationToken);
            if (manifest.TryGetValue(sourcePath, out var existingHash) && existingHash == hash && index.ContainsSource(sourcePath))
            {
                continue;
            }
            index.RemoveBySource(sourcePath);

            var chunks = _chunker.Chunk(text, _config.Rag.ChunkSize, _config.Rag.Overlap)
                .Select((chunk, i) => new ChunkRecord(
                    id: $"{scope.Scope}/{Path.GetFileName(source.File)}#{i}",
                    text: chunk,
                    sourcePath: sourcePath,
                    metadata: BuildMetadata(scope, source)));

            var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken);
            foreach (var chunk in chunks)
            {
                var embedding = await embeddingProvider.EmbedAsync(chunk.Text, cancellationToken);
                index.Upsert(new VectorChunk(chunk.Id, chunk.Text, chunk.SourcePath, chunk.Metadata, embedding));
            }

            await _indexRepository.SaveIndexAsync(scope, index, cancellationToken);
            manifest[sourcePath] = hash;
        }

        await _indexRepository.SaveManifestAsync(_config.Persona.Id, manifest, cancellationToken);
    }

    public async Task<AgentReply> AskAsync(string playerLine, AskOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new AskOptions();
        var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken);
        var queryEmbedding = await embeddingProvider.EmbedAsync(playerLine, cancellationToken);

        var tieredHits = await RetrieveTieredHitsAsync(queryEmbedding, opts.TopK, cancellationToken);

        var contextBuilder = new StringBuilder();
        foreach (var hit in tieredHits)
        {
            contextBuilder.AppendLine($"SOURCE: {Path.GetFileName(hit.SourcePath)}");
            contextBuilder.AppendLine(hit.Text);
            contextBuilder.AppendLine("---");
        }

        var systemPrompt = BuildSystemPrompt(opts);
        var chatProvider = await _router.RouteAsync(_config, _runtimeOptions, opts, cancellationToken);
        var answer = await chatProvider.GetChatResponseAsync(systemPrompt, contextBuilder.ToString(), playerLine, cancellationToken);
        var fromCloud = chatProvider is CloudChatProvider;

        var reply = new AgentReply(
            answer.Text,
            tieredHits.Select(hit => hit.SourcePath).ToArray(),
            tieredHits.Select(hit => hit.Score).ToArray(),
            fromCloud);

        if (_config.Providers.Routing.CloudFallbackOnMiss && !fromCloud && answer.ShouldFallback)
        {
            var cloudProvider = await _router.RouteAsync(_config, _runtimeOptions, opts with { ForceCloud = true }, cancellationToken);
            if (cloudProvider is CloudChatProvider cloud)
            {
                var cloudAnswer = await cloud.GetChatResponseAsync(systemPrompt, contextBuilder.ToString(), playerLine, cancellationToken);
                return reply with
                {
                    Text = cloudAnswer.Text,
                    FromCloud = true
                };
            }
        }

        return reply;
    }

    public async IAsyncEnumerable<string> StreamAsync(string playerLine, AskOptions? opts = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reply = await AskAsync(playerLine, opts, cancellationToken);
        yield return reply.Text;
    }

    public async Task RememberAsync(string fact, CancellationToken cancellationToken = default)
    {
        var scope = IndexScopeKey.ForMemory(_config.Persona);
        var index = await GetOrLoadIndexAsync(scope, cancellationToken);
        var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken);
        var embedding = await embeddingProvider.EmbedAsync(fact, cancellationToken);

        var chunk = new VectorChunk(
            id: $"{scope.Scope}/memory#{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            text: fact,
            sourcePath: "memory",
            metadata: new Dictionary<string, string> { ["scope"] = "memory" },
            embedding: embedding);

        index.Upsert(chunk);
        await _indexRepository.SaveIndexAsync(scope, index, cancellationToken);
    }

    private async Task<IEmbeddingProvider> GetEmbeddingProviderAsync(CancellationToken cancellationToken)
    {
        if (_embeddingProvider != null)
        {
            return _embeddingProvider;
        }

        _embeddingProvider = await _router.ResolveEmbeddingProviderAsync(_config, _runtimeOptions, cancellationToken);
        return _embeddingProvider;
    }

    private async Task<VectorIndex> GetOrLoadIndexAsync(IndexScopeKey scope, CancellationToken cancellationToken)
    {
        if (_loadedIndexes.TryGetValue(scope, out var existing))
        {
            return existing;
        }

        var index = await _indexRepository.LoadIndexAsync(scope, cancellationToken);
        _loadedIndexes[scope] = index;
        return index;
    }

    private async Task<IReadOnlyList<VectorChunk>> RetrieveTieredHitsAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken)
    {
        var hits = new List<VectorChunk>();

        var scopeRequests = new List<(IndexScopeKey Key, int Take)>
        {
            (IndexScopeKey.ForWorld(_config.Persona), 2),
            (IndexScopeKey.ForRegion(_config.Persona), string.IsNullOrWhiteSpace(_config.Persona.RegionId) ? 0 : 1),
            (IndexScopeKey.ForFaction(_config.Persona), string.IsNullOrWhiteSpace(_config.Persona.FactionId) ? 0 : 1),
            (IndexScopeKey.ForPersona(_config.Persona), topK),
            (IndexScopeKey.ForMemory(_config.Persona), 1)
        };

        foreach (var (key, take) in scopeRequests)
        {
            if (take <= 0)
            {
                continue;
            }

            var index = await GetOrLoadIndexAsync(key, cancellationToken);
            var scopeHits = index.Search(queryEmbedding, take);
            hits.AddRange(scopeHits);
        }

        return hits
            .OrderByDescending(hit => hit.Score)
            .ToArray();
    }

    private string BuildSystemPrompt(AskOptions opts)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(_config.Persona.SystemPrompt);
        promptBuilder.AppendLine("Stay in character. Avoid meta-talk.");
        if (!string.IsNullOrWhiteSpace(_config.Persona.Style))
        {
            promptBuilder.AppendLine($"Style: {_config.Persona.Style}");
        }

        if (!opts.InCharacter)
        {
            promptBuilder.AppendLine("You may answer out of character if needed.");
        }

        if (!string.IsNullOrWhiteSpace(opts.SystemOverride))
        {
            promptBuilder.AppendLine(opts.SystemOverride);
        }

        return promptBuilder.ToString();
    }

    private IReadOnlyDictionary<string, string> BuildMetadata(IndexScopeKey scope, SourceConfig source)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scope"] = scope.Scope,
            ["npc"] = scope.NpcId
        };

        if (!string.IsNullOrWhiteSpace(scope.RegionId))
        {
            metadata["region"] = scope.RegionId!;
        }

        if (!string.IsNullOrWhiteSpace(scope.FactionId))
        {
            metadata["faction"] = scope.FactionId!;
        }

        if (!string.IsNullOrWhiteSpace(_config.Persona.WorldId))
        {
            metadata["world"] = _config.Persona.WorldId!;
        }

        if (source.Metadata != null)
        {
            foreach (var pair in source.Metadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        if (_config.Rag.Filters != null)
        {
            foreach (var filter in _config.Rag.Filters)
            {
                metadata[filter.Key] = filter.Value;
            }
        }

        return metadata;
    }

    private readonly record struct ChunkRecord(string Id, string Text, string SourcePath, IReadOnlyDictionary<string, string> Metadata);
}
