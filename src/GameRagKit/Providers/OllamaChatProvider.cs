using System.Net.Http.Json;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class OllamaChatProvider : IChatProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatProvider(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"CONTEXT:\n{context}\n\nPLAYER: {question}" }
            },
            stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var message = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var shouldFallback = string.IsNullOrWhiteSpace(message) || message.Length < 6;
        return new ChatResponse(message.Trim(), shouldFallback);
    }
}
