namespace GameRagKit.Providers;

public interface IChatProvider
{
    Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken);
}

public sealed record ChatResponse(string Text, bool ShouldFallback = false);
