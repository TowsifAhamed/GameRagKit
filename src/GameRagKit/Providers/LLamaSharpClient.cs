using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;

namespace GameRagKit.Providers;

public sealed class LLamaSharpClient : IChatProvider, IEmbeddingProvider, IDisposable
{
    private readonly bool _mockMode;
    private readonly LLamaWeights? _chatWeights;
    private readonly LLamaWeights? _embeddingWeights;
    private readonly ModelParams? _chatParams;
    private readonly ModelParams? _embeddingParams;
    private readonly InferenceParams? _inferenceParams;
    private bool _disposed;

    public LLamaSharpClient()
    {
        _mockMode = true;
    }

    public LLamaSharpClient(LLamaSharpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new ArgumentException("ModelPath is required for LLamaSharp local inference", nameof(options));
        }

        _mockMode = false;
        _chatParams = CreateChatParams(options);
        _chatWeights = LLamaWeights.LoadFromFile(_chatParams);

        var embedModelPath = options.EmbedModelPath ?? options.ModelPath;
        _embeddingParams = CreateEmbeddingParams(options, embedModelPath);
        _embeddingWeights = string.Equals(embedModelPath, options.ModelPath, StringComparison.Ordinal)
            ? _chatWeights
            : LLamaWeights.LoadFromFile(_embeddingParams);

        _inferenceParams = CreateInferenceParams(options);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string system,
        string context,
        string user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_mockMode)
        {
            yield return await InvokeAsync(system, context, user, ct).ConfigureAwait(false);
            yield break;
        }

        await foreach (var token in StreamWithLlamaAsync(system, context, user, ct).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public async Task<string> InvokeAsync(string system, string context, string user, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_mockMode)
        {
            var summary = BuildSummary(context);
            var builder = new StringBuilder();
            builder.AppendLine(system.Trim());
            if (!string.IsNullOrWhiteSpace(summary))
            {
                builder.AppendLine($"Key facts: {summary}");
            }

            builder.Append("Reply: ");
            builder.Append(user);
            builder.Append('.');
            return builder.ToString();
        }

        var response = new StringBuilder();
        await foreach (var token in StreamWithLlamaAsync(system, context, user, ct).ConfigureAwait(false))
        {
            response.Append(token);
        }

        return response.ToString();
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_mockMode)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            var vector = new float[1536];
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = hash[i % hash.Length] / 255f;
            }

            return vector;
        }

        if (_embeddingWeights is null || _embeddingParams is null)
        {
            throw new InvalidOperationException("LLamaSharp embeddings are not configured. Ensure a model path is provided.");
        }

        using var embedder = new LLamaEmbedder(_embeddingWeights, _embeddingParams);
        var vectors = await embedder.GetEmbeddings(text, cancellationToken).ConfigureAwait(false);
        return vectors.Count > 0 ? vectors[0] : Array.Empty<float>();
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var reply = await InvokeAsync(systemPrompt, context, question, cancellationToken).ConfigureAwait(false);
        return new ChatResponse(reply);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _chatWeights?.Dispose();
        if (!ReferenceEquals(_chatWeights, _embeddingWeights))
        {
            _embeddingWeights?.Dispose();
        }

        _disposed = true;
    }

    private async IAsyncEnumerable<string> StreamWithLlamaAsync(
        string system,
        string context,
        string user,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_chatWeights is null || _chatParams is null || _inferenceParams is null)
        {
            throw new InvalidOperationException("LLamaSharp chat is not configured. Ensure a model path is provided.");
        }

        using var chatContext = _chatWeights.CreateContext(_chatParams);
        var executor = new InteractiveExecutor(chatContext);
        var history = BuildHistory(system, context);
        var session = new ChatSession(executor, history);

        await foreach (var token in session.ChatAsync(
            new ChatHistory.Message(AuthorRole.User, user),
            applyInputTransformPipeline: false,
            _inferenceParams,
            cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    private static ChatHistory BuildHistory(string systemPrompt, string context)
    {
        var history = new ChatHistory();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            history.AddMessage(AuthorRole.System, systemPrompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            history.AddMessage(AuthorRole.System, $"Context:\n{context.Trim()}");
        }

        return history;
    }

    private static ModelParams CreateChatParams(LLamaSharpOptions options)
    {
        var parameters = new ModelParams(options.ModelPath)
        {
            ContextSize = (uint)options.ContextSize,
            GpuLayerCount = options.GpuLayerCount,
            Threads = options.Threads,
            BatchThreads = options.BatchThreads,
            BatchSize = options.BatchSize,
            UBatchSize = options.MicroBatchSize,
            Embeddings = false
        };

        return parameters;
    }

    private static ModelParams CreateEmbeddingParams(LLamaSharpOptions options, string embedModelPath)
    {
        var parameters = new ModelParams(embedModelPath)
        {
            ContextSize = (uint)options.EmbeddingContextSize,
            GpuLayerCount = options.GpuLayerCount,
            Threads = options.Threads,
            BatchThreads = options.BatchThreads,
            BatchSize = options.BatchSize,
            UBatchSize = options.MicroBatchSize,
            Embeddings = true
        };

        return parameters;
    }

    private static InferenceParams CreateInferenceParams(LLamaSharpOptions options)
    {
        return new InferenceParams
        {
            MaxTokens = options.MaxTokens,
            AntiPrompts = options.StopSequences
        };
    }

    private static string BuildSummary(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return string.Empty;
        }

        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", lines.Take(3));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LLamaSharpClient));
        }
    }
}
