using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GameRagKit.Actions;
using GameRagKit.Config;
using GameRagKit.Mood;
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
    private readonly MoodRepository _moodRepository;
    private readonly IVectorStore _vectorStore;
    private readonly Router _router;
    private readonly Retriever _retriever;
    private readonly ProviderRuntimeOptions _runtimeOptions = new();
    private readonly ConcurrentDictionary<string, string> _sourceHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SnapshotEntry> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _moodLock = new(1, 1);
    private IEmbeddingProvider? _embeddingProvider;
    private MoodState? _currentMood;

    public string PersonaId => _config.Persona.Id;
    public double DefaultImportance => _config.Persona.DefaultImportance ?? _config.Providers.Routing.DefaultImportance;

    internal NpcAgent(
        NpcConfig config,
        string configDirectory,
        TextChunker chunker,
        VectorIndexRepository manifestRepository,
        MoodRepository moodRepository,
        IVectorStore vectorStore,
        Router router)
    {
        _config = config;
        _configDirectory = configDirectory;
        _chunker = chunker;
        _manifestRepository = manifestRepository;
        _moodRepository = moodRepository;
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
        _runtimeOptions.SttModelPath = Environment.GetEnvironmentVariable("STT_MODEL_PATH") ?? _config.Providers.Voice?.SpeechToText?.ModelPath;
        _runtimeOptions.SttExecutablePath = Environment.GetEnvironmentVariable("STT_EXECUTABLE_PATH") ?? _config.Providers.Voice?.SpeechToText?.ExecutablePath;
        _runtimeOptions.TtsVoiceModelPath = Environment.GetEnvironmentVariable("TTS_VOICE_MODEL_PATH") ?? _config.Providers.Voice?.TextToSpeech?.VoiceModelPath;
        _runtimeOptions.TtsExecutablePath = Environment.GetEnvironmentVariable("TTS_EXECUTABLE_PATH") ?? _config.Providers.Voice?.TextToSpeech?.ExecutablePath;

        return this;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await _manifestRepository.LoadManifestAsync(_config.Persona.Id, cancellationToken).ConfigureAwait(false);
        foreach (var pair in manifest)
        {
            _sourceHashes[pair.Key] = pair.Value;
        }

        var indexCache = new Dictionary<IndexScopeKey, VectorIndex>();

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
            if (!indexCache.TryGetValue(scope, out var vectorIndex))
            {
                vectorIndex = await _manifestRepository.LoadIndexAsync(scope, cancellationToken).ConfigureAwait(false);
                indexCache[scope] = vectorIndex;
            }

            var embeddingProvider = await GetEmbeddingProviderAsync(cancellationToken).ConfigureAwait(false);
            var chunks = _chunker.Chunk(text, _config.Rag.ChunkSize, _config.Rag.Overlap)
                .Select((chunk, index) => new ChunkRecord(index, chunk, sourcePath, scope));

            vectorIndex.RemoveBySource(sourcePath);

            var records = new List<RagRecord>();
            foreach (var chunk in chunks)
            {
                var embedding = await embeddingProvider.EmbedAsync(chunk.Text, cancellationToken).ConfigureAwait(false);
                var metadata = BuildMetadata(chunk.Scope, source, chunk.SourcePath);
                var record = CreateRecord(chunk, embedding, metadata);
                records.Add(record);
                vectorIndex.Upsert(new VectorChunk(record.Key, chunk.Text, sourcePath, metadata, embedding));
            }

            await _vectorStore.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
            _sourceHashes[sourcePath] = hash;
            manifest[sourcePath] = hash;
        }

        await _manifestRepository.SaveManifestAsync(_config.Persona.Id, manifest, cancellationToken).ConfigureAwait(false);

        foreach (var pair in indexCache)
        {
            await _manifestRepository.SaveIndexAsync(pair.Key, pair.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AgentReply> AskAsync(string playerLine, AskOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new AskOptions();
        var (context, hits) = await BuildContextAsync(playerLine, opts, cancellationToken).ConfigureAwait(false);
        var systemPrompt = await BuildSystemPromptAsync(opts, cancellationToken).ConfigureAwait(false);
        var chatProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts, cancellationToken).ConfigureAwait(false);
        var response = await chatProvider.GetChatResponseAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false);
        var fromCloud = chatProvider is CloudChatProvider;
        var (cleanedText, actions, mood) = await ProcessReplyAsync(response.Text, cancellationToken).ConfigureAwait(false);

        var reply = new AgentReply(
            cleanedText,
            hits.Select(hit => hit.Tags.TryGetValue("source", out var source) ? source : string.Empty).ToArray(),
            hits.Select(hit => hit.Score ?? 0d).ToArray(),
            fromCloud)
        {
            Actions = actions,
            Mood = mood
        };

        if (_config.Providers.Routing.CloudFallbackOnMiss && !fromCloud && response.ShouldFallback)
        {
            var cloudProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts with { ForceCloud = true }, cancellationToken).ConfigureAwait(false);
            if (cloudProvider is CloudChatProvider cloud)
            {
                var cloudResponse = await cloud.GetChatResponseAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false);
                var (cloudText, cloudActions, cloudMood) = await ProcessReplyAsync(cloudResponse.Text, cancellationToken).ConfigureAwait(false);
                return reply with
                {
                    Text = cloudText,
                    FromCloud = true,
                    Actions = cloudActions,
                    Mood = cloudMood
                };
            }
        }

        return reply;
    }

    /// <summary>
    /// Extracts action/mood blocks from a raw model reply, validates them, persists any
    /// new mood, and returns the player-visible cleaned text alongside the validated
    /// action calls and the NPC's mood after this reply (unchanged from before if no valid
    /// mood block was present).
    /// </summary>
    private async Task<(string CleanedText, IReadOnlyList<ActionCall> Actions, MoodState? Mood)> ProcessReplyAsync(string rawText, CancellationToken cancellationToken)
    {
        var parsed = GameRagBlockParser.Parse(rawText);
        var actionResult = ActionParser.Validate(parsed.Blocks, _config.Persona.Actions);
        var moodResult = MoodParser.Validate(parsed.Blocks, DateTimeOffset.UtcNow);

        var mood = _currentMood;
        if (moodResult.Mood != null)
        {
            mood = await UpdateMoodAsync(moodResult.Mood, cancellationToken).ConfigureAwait(false);
        }

        return (parsed.CleanedText, actionResult.Actions, mood);
    }

    /// <summary>
    /// Transcribes player speech, asks the NPC exactly as AskAsync would, and optionally
    /// synthesizes the NPC's reply back to speech. Requires providers.voice.speech_to_text
    /// (and, if <paramref name="synthesizeReply"/> is true, providers.voice.text_to_speech)
    /// to be configured.
    /// </summary>
    public async Task<VoiceReply> AskVoiceAsync(
        byte[] playerAudioWavBytes,
        AskOptions? opts = null,
        bool synthesizeReply = true,
        CancellationToken cancellationToken = default)
    {
        var speechToText = _router.ResolveSpeechToText(_config, _runtimeOptions);
        var transcript = await speechToText.TranscribeAsync(playerAudioWavBytes, cancellationToken).ConfigureAwait(false);

        var reply = await AskAsync(transcript, opts, cancellationToken).ConfigureAwait(false);

        byte[]? replyAudio = null;
        if (synthesizeReply)
        {
            var textToSpeech = _router.ResolveTextToSpeech(_config, _runtimeOptions);
            replyAudio = await textToSpeech.SynthesizeAsync(reply.Text, cancellationToken).ConfigureAwait(false);
        }

        return new VoiceReply(transcript, reply, replyAudio);
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(string playerLine, AskOptions? opts = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        opts ??= new AskOptions();
        var (context, hits) = await BuildContextAsync(playerLine, opts, cancellationToken).ConfigureAwait(false);
        var systemPrompt = await BuildSystemPromptAsync(opts, cancellationToken).ConfigureAwait(false);
        var chatProvider = await _router.ResolveChatAsync(_config, _runtimeOptions, opts, cancellationToken).ConfigureAwait(false);

        yield return new StreamEvent.Start(_config.Persona.Id);

        var raw = new StringBuilder();
        var fenceFilter = new ActionFenceStreamFilter();

        await foreach (var token in chatProvider.StreamAsync(systemPrompt, context, playerLine, cancellationToken).ConfigureAwait(false))
        {
            raw.Append(token);

            foreach (var visibleChunk in fenceFilter.Push(token))
            {
                if (visibleChunk.Length > 0)
                {
                    yield return new StreamEvent.Chunk(visibleChunk);
                }
            }
        }

        var trailing = fenceFilter.Flush();
        if (trailing.Length > 0)
        {
            yield return new StreamEvent.Chunk(trailing);
        }

        var (_, actions, mood) = await ProcessReplyAsync(raw.ToString(), cancellationToken).ConfigureAwait(false);
        var sources = hits.Select(hit => hit.Tags.TryGetValue("source", out var source) ? source : string.Empty).ToArray();
        yield return new StreamEvent.End(sources, actions, mood);
    }

    public void WriteSnapshot(string key, object state, TimeSpan? ttl = null)
    {
        var payload = state switch
        {
            string s => s,
            _ => JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        var expiry = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10));
        _snapshots[key] = new SnapshotEntry(payload, expiry);
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
        builder.Append(WorldStateFormatter.Format(opts.WorldState));
        AppendRuntimeState(builder, opts.State);
        foreach (var hit in hits)
        {
            var source = hit.Tags.TryGetValue("source", out var s) ? Path.GetFileName(s) : "unknown";
            builder.AppendLine($"SOURCE: {source}");
            builder.AppendLine(hit.Text);
            builder.AppendLine("---");
        }

        return (builder.ToString(), hits);
    }

    private RagRecord CreateRecord(ChunkRecord chunk, float[] embedding, Dictionary<string, string> metadata)
    {
        var keySeed = $"{chunk.Scope.Scope}:{chunk.SourcePath}:{chunk.Index}";
        var key = CreateDeterministicGuid(keySeed);
        return new RagRecord(key.ToString(), chunk.Scope.Scope, chunk.Text, embedding, metadata);
    }

    private async Task<string> BuildSystemPromptAsync(AskOptions opts, CancellationToken cancellationToken)
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

        promptBuilder.AppendLine("If the provided sources do not contain the answer, say you cannot determine it from available evidence.");

        if (_config.Persona.Actions.Count > 0)
        {
            AppendActionInstructions(promptBuilder, _config.Persona.Actions);
        }

        if (_config.Persona.MoodTracking)
        {
            var mood = await GetCurrentMoodAsync(cancellationToken).ConfigureAwait(false);
            AppendMoodInstructions(promptBuilder, mood);
        }

        if (!string.IsNullOrWhiteSpace(opts.SystemOverride))
        {
            promptBuilder.AppendLine(opts.SystemOverride);
        }

        return promptBuilder.ToString();
    }

    private static void AppendActionInstructions(StringBuilder builder, IReadOnlyList<Config.ActionDefinition> actions)
    {
        builder.AppendLine("You may perform game actions when appropriate. Available actions:");
        foreach (var action in actions)
        {
            var argsDescription = action.Args.Count == 0
                ? "no args"
                : string.Join(", ", action.Args.Select(a => $"{a.Name}: {a.Type}{(a.Required ? "" : ", optional")}"));
            builder.AppendLine($"- {action.Name}({argsDescription}){(string.IsNullOrWhiteSpace(action.Description) ? "" : $" — {action.Description}")}");
        }

        builder.AppendLine("To call an action, include exactly one block per action using this exact format:");
        builder.AppendLine("[[gamerag:action]]");
        builder.AppendLine("{\"name\":\"<action_name>\",\"args\":{...}}");
        builder.AppendLine("[[/gamerag]]");
        builder.AppendLine("Only call an action when the situation clearly warrants it. Continue your in-character reply as normal text outside the block.");
    }

    private static void AppendMoodInstructions(StringBuilder builder, MoodState? currentMood)
    {
        var moodDescription = currentMood != null && currentMood.Value != "neutral"
            ? $"Your current mood is \"{currentMood.Value}\" (intensity {currentMood.Intensity:0.0})."
            : "Your current mood is neutral.";

        builder.AppendLine(moodDescription);
        builder.AppendLine("If this exchange meaningfully changes how you feel, report your new mood using this exact format:");
        builder.AppendLine("[[gamerag:mood]]");
        builder.AppendLine("{\"value\":\"<one or two word mood, e.g. wary, delighted, hostile>\",\"intensity\":<0.0-1.0>}");
        builder.AppendLine("[[/gamerag]]");
        builder.AppendLine("Only report a mood change when it's genuinely warranted, not after every reply. Continue your in-character reply as normal text outside the block.");
    }

    private async Task<MoodState?> GetCurrentMoodAsync(CancellationToken cancellationToken)
    {
        if (_currentMood != null)
        {
            return _currentMood;
        }

        await _moodLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentMood ??= await _moodRepository.LoadAsync(_config.Persona.Id, cancellationToken).ConfigureAwait(false);
            return _currentMood;
        }
        finally
        {
            _moodLock.Release();
        }
    }

    private async Task<MoodState> UpdateMoodAsync(MoodState mood, CancellationToken cancellationToken)
    {
        await _moodLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentMood = mood;
        }
        finally
        {
            _moodLock.Release();
        }

        await _moodRepository.SaveAsync(_config.Persona.Id, mood, cancellationToken).ConfigureAwait(false);
        return mood;
    }

    private void AppendRuntimeState(StringBuilder builder, string? transientState)
    {
        var now = DateTimeOffset.UtcNow;
        var hasState = false;
        if (!string.IsNullOrWhiteSpace(transientState))
        {
            builder.AppendLine("RUNTIME STATE:");
            builder.AppendLine(transientState!.Trim());
            builder.AppendLine("---");
            hasState = true;
        }

        foreach (var pair in _snapshots.ToArray())
        {
            if (pair.Value.Expiry <= now)
            {
                _snapshots.TryRemove(pair.Key, out _);
                continue;
            }

            if (!hasState)
            {
                builder.AppendLine("RUNTIME STATE:");
                hasState = true;
            }

            builder.AppendLine($"{pair.Key}: {pair.Value.Payload}");
        }

        if (hasState)
        {
            builder.AppendLine("---");
        }
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

    private sealed record SnapshotEntry(string Payload, DateTimeOffset Expiry);
}
