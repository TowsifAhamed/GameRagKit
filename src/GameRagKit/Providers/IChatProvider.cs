using System.Runtime.CompilerServices;

namespace GameRagKit.Providers;

public interface IChatProvider : IChatModel
{
    Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken);

    async IAsyncEnumerable<string> IChatModel.StreamAsync(string system, string context, string user, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await GetChatResponseAsync(system, context, user, ct).ConfigureAwait(false);
        yield return response.Text;
    }

    async Task<string> IChatModel.InvokeAsync(string system, string context, string user, CancellationToken ct)
    {
        var response = await GetChatResponseAsync(system, context, user, ct).ConfigureAwait(false);
        return response.Text;
    }
}

public sealed record ChatResponse(string Text, bool ShouldFallback = false);
