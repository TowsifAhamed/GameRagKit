using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GameRagKit.Config;
using GameRagKit.Pipeline;
using GameRagKit.Providers;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;
using GameRagKit.VectorStores;

namespace GameRagKit;

public sealed class NpcAgent : IAsyncDisposable
{
    private readonly NpcConfig _config;
    private readonly string _configDirectory;
    private readonly TextChunker _chunker;
    private readonly VectorIndexRepository _manifestRepository;
    private readonly IVectorStore _vectorStore;
    private readonly Router _router;
    private readonly Retriever _retriever;
    private readonly ProviderRuntimeOptions _runtimeOptions = new();
    private readonly ConcurrentDictionary<string, string> _sourceHashes = new(StringComparer.OrdinalIgnoreCase);
    private IEmbeddingProvider? _embeddingProvider;

    public string PersonaId => _config.Persona.Id;
    public double DefaultImportance => _config.Persona.DefaultImportance ?? _config.Providers.Routing.DefaultImportance;

    internal NpcAgent(
        NpcConfig config,
        string configDirectory,
        TextChunker chunker,
        VectorIndexRepository manifestRepository,
        IVectorStore vectorStore,
        Router router)
    {
        _config = config;
        _configDirectory = configDirectory;
        _chunker = chunker;
        _manifestRepository = manifestRepository;
        _vectorStore = vectorStore;
        _router = router;
        var filters = config.Rag.Filters != null
            ? new Dictionary<string, string>(config.Rag.Filters, StringComparer.OrdinalIgnoreCase)
            : null;
        _retriever = new Retriever(vectorStore, config.Persona, filters);
    }

    public NpcAgent UseEnv()
    {
        _runtimeOptions.CloudProvider = Environment.GetEnvironmentVariable("PROVIDER") ?? _config.Providers.Cloud?.Provider ?? "openai";
        _runtimeOptions.CloudApiKey = Environment.GetEnvironmentVariable("API_KEY");
        _runtimeOptions.CloudEndpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? _config.Providers.Cloud?.Endpoint;
        _runtimeOptions.LocalEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? _config.Providers.Local?.Endpoint;
        _runtimeOptions.LocalEngine = Environment.GetEnvironmentVariable("LOCAL_ENGINE") ?? _config.Providers.Local?.Engine ?? "ollama";
        _runtimeOptions.LocalChatModel = Environment.GetEnvironmentVariable("LOCAL_CHAT_MODEL") ?? _config.Providers.Local?.ChatModel;
        _runtimeOptions.LocalEmbedModel = Environment.GetEnvironmentVariable("LOCAL_EMBED_MODEL") ?? _config.Providers.Local?.EmbedModel;
        _runtimeOptions.CloudChatModel = Environment.GetEnvironmentVariable("CLOUD_CHAT_MODEL") ?? _config.Providers.Cloud?.ChatModel;
        _runtimeOptions.CloudEmbedModel = Environment.GetEnvironmentVariable("CLOUD_EMBED_MODEL") ?? _config.Providers.Cloud?.EmbedModel;

        return this;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await _manifestRepository.LoadManifestAsync(_config.Persona.Id, cancellationToken).ConfigureAwait(false);
        foreach (var pair in manifest)
        {
            _sourceHashes[pair.Key] = pair.Value;
        }

        foreach (var source in _config.Rag.Sources)
        {
            var sourcePath = Path.Combine(_configDirectory, source.File);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var hash = VectorIndexRepository.ComputeHash(text);
            if (_sourceHashes.TryGetValue(sourcePath, out var existing) && existing == hash)
            {
                continue;
            }

            var scope = IndexScopeKey.FromSource(_config.Persona, source);
            var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false);
            var chunks = _chunker.Chunk(text, _config.Rag.ChunkSize, _config.Rag.Overlap)
                .Select((chunk, index) => new ChunkRecord(index, chunk, sourcePath, scope));

            var records = new List<RagRecord>();
            foreach (var chunk in chunks)
            {
                var embedding = await embeddingProvider.EmbedAsync(chunk.Text, cancellationToken).ConfigureAwait(false);
                var record = CreateRecord(chunk, embedding, source);
                records.Add(record);
            }

            await _vectorStore.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
            _sourceHashes[sourcePath] = hash;
            manifest[sourcePath] = hash;
        }

        await _manifestRepository.SaveManifestAsync(_config.Persona.Id, manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentReply> AskAsync(string playerLine, AskOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new AskOptions();
        var (context, hits) = await BuildContextAsync(playerLine, opts, cancellationToken).ConfigureAwait(false);
        var systemPrompt = BuildSystemPrompt(opts);
        var chatProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts, cancellationToken).ConfigureAwait(false);
        var response = await chatProvider.GetChatResponseAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false);
        var fromCloud = chatProvider is CloudChatProvider;

        var reply = new AgentReply(
            response.Text,
            hits.Select(hit => hit.Tags.TryGetValue("source", out var source) ? source : string.Empty).ToArray(),
            hits.Select(hit => hit.Score ?? 0d).ToArray(),
            fromCloud);

        if (_config.Providers.Routing.CloudFallbackOnMiss && !fromCloud && response.ShouldFallback)
        {
            var cloudProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts with { ForceCloud = true }, cancellationToken).ConfigureAwait(false);
            if (cloudProvider is CloudChatProvider cloud)
            {
                var cloudResponse = await cloud.GetChatResponseAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false);
                return reply with
                {
                    Text = cloudResponse.Text,
                    FromCloud = true
                };
            }
        }

        return reply;
    }

    public async IAsyncEnumerable<string> StreamAsync(string playerLine, AskOptions? opts = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= new AskOptions();
        var (context, _) = await BuildContextAsync(playerLine, opts, cancellationToken).ConfigureAwait(false);
        var systemPrompt = BuildSystemPrompt(opts);
        var chatProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts, cancellationToken).ConfigureAwait(false);
        await foreach (var token in chatProvider.StreamAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public async Task RememberAsync(string fact, CancellationToken cancellationToken = default)
    {
        var scope = IndexScopeKey.ForMemory(_config.Persona);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scope"] = scope.Scope,
            ["npc"] = _config.Persona.Id,
            ["source"] = "memory"
        };

        var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false);
        var embedding = await embeddingProvider.EmbedAsync(fact, cancellationToken).ConfigureAwait(false);
        var key = CreateDeterministicGuid($"{scope.Scope}:{fact}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        var record = new RagRecord(key.ToString(), scope.Scope, fact, embedding, metadata);
        await _vectorStore.UpsertAsync(new[] { record }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> HotIngestAsync(string text, IReadOnlyDictionary<string, string>? tags, CancellationToken cancellationToken)
    {
        var scope = IndexScopeKey.ForPersona(_config.Persona);
        var metadata = new Dictionary<string, string>(BuildMetadata(scope, new SourceConfig(), "hot_ingest"), StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = tags != null && tags.TryGetValue("source", out var explicitSource)
                ? explicitSource
                : "hot_ingest"
        };

        if (tags != null)
        {
            foreach (var pair in tags)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false);
        var embedding = await embeddingProvider.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
        var key = CreateDeterministicGuid($"{scope.Scope}:{metadata["source"]}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        var record = new RagRecord(key.ToString(), scope.Scope, text, embedding, metadata);
        await _vectorStore.UpsertAsync(new[] { record }, cancellationToken).ConfigureAwait(false);
        return key.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_vectorStore is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IEmbeddingProvider> GetEmbeddingProviderAsync(CancellationToken cancellationToken)
    {
        if (_embeddingProvider != null)
        {
            return _embeddingProvider;
        }

        _embeddingProvider = await _router.ResolveEmbedderAsync(_config, _runtimeOptions, cancellationToken).ConfigureAwait(false);
        return _embeddingProvider;
    }

    private async Task<(string Context, IReadOnlyList<RagHit> Hits)> BuildContextAsync(string question, AskOptions opts, CancellationToken cancellationToken)
    {
        var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false);
        var queryEmbedding = await embeddingProvider.EmbedAsync(question, cancellationToken).ConfigureAwait(false);
        var hits = await _retriever.RetrieveAsync(queryEmbedding, opts.TopK, cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        foreach (var hit in hits)
        {
            var source = hit.Tags.TryGetValue("source", out var s) ? Path.GetFileName(s) : "unknown";
            builder.AppendLine($"SOURCE: {source}");
            builder.AppendLine(hit.Text);
            builder.AppendLine("---");
        }

        return (builder.ToString(), hits);
    }

    private RagRecord CreateRecord(ChunkRecord chunk, float[] embedding, SourceConfig source)
    {
        var metadata = BuildMetadata(chunk.Scope, source, chunk.SourcePath);
        var keySeed = $"{chunk.Scope.Scope}:{chunk.SourcePath}:{chunk.Index}";
        var key = CreateDeterministicGuid(keySeed);
        return new RagRecord(key.ToString(), chunk.Scope.Scope, chunk.Text, embedding, metadata);
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

    private Dictionary<string, string> BuildMetadata(IndexScopeKey scope, SourceConfig source, string sourcePath)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["scope"] = scope.Scope,
            ["npc"] = scope.NpcId,
            ["source"] = sourcePath
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

    private static Guid CreateDeterministicGuid(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private sealed record ChunkRecord(int Index, string Text, string SourcePath, IndexScopeKey Scope);
}
