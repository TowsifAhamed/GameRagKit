using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace GameRagKit.Providers;

public sealed class LLamaSharpClient : IChatProvider, IEmbeddingProvider
{
    public async IAsyncEnumerable<string> StreamAsync(string system, string context, string user, [EnumeratorCancellation] CancellationToken ct)
    {
        var reply = await InvokeAsync(system, context, user, ct).ConfigureAwait(false);
        yield return reply;
    }

    public Task<string> InvokeAsync(string system, string context, string user, CancellationToken ct)
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
        return Task.FromResult(builder.ToString());
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha.ComputeHash(bytes);
        var vector = new float[1536];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = hash[i % hash.Length] / 255f;
        }

        return Task.FromResult(vector);
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var reply = await InvokeAsync(systemPrompt, context, question, cancellationToken).ConfigureAwait(false);
        return new ChatResponse(reply);
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
}
