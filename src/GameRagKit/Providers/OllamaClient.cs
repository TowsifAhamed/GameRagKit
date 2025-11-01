using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class OllamaClient : IChatProvider, IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _chatModel;
    private readonly string _embedModel;

    public OllamaClient(HttpClient httpClient, string chatModel, string embedModel)
    {
        _httpClient = httpClient;
        _chatModel = chatModel;
        _embedModel = embedModel;
    }

    public async IAsyncEnumerable<string> StreamAsync(string system, string context, string user, [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = new
        {
            model = _chatModel,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = $"CONTEXT:\n{context}\n\nPLAYER: {user}" }
            },
            stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParseChunk(line, out var chunk))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk))
            {
                yield return chunk;
            }
        }
    }

    public async Task<string> InvokeAsync(string system, string context, string user, CancellationToken ct)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in StreamAsync(system, context, user, ct).ConfigureAwait(false))
        {
            builder.Append(chunk);
        }

        if (builder.Length == 0)
        {
            var payload = new
            {
                model = _chatModel,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = $"CONTEXT:\n{context}\n\nPLAYER: {user}" }
                },
                stream = false
            };

            using var response = await _httpClient.PostAsJsonAsync("/api/chat", payload, SerializerOptions, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            return document.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }

        return builder.ToString();
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var payload = new
        {
            model = _embedModel,
            input = text
        };

        using var response = await _httpClient.PostAsJsonAsync("/api/embeddings", payload, SerializerOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        if (!document.TryGetProperty("embedding", out var embeddingNode) || embeddingNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<float>();
        }

        var buffer = new float[embeddingNode.GetArrayLength()];
        var index = 0;
        foreach (var value in embeddingNode.EnumerateArray())
        {
            buffer[index++] = (float)value.GetDouble();
        }

        return buffer;
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var text = await InvokeAsync(systemPrompt, context, question, cancellationToken).ConfigureAwait(false);
        var shouldFallback = string.IsNullOrWhiteSpace(text) || text.Length < 4;
        return new ChatResponse(text, shouldFallback);
    }

    private static bool TryParseChunk(string line, out string chunk)
    {
        chunk = string.Empty;
        try
        {
            var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line.Substring("data:".Length).Trim()
                : line;

            if (string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var element = JsonSerializer.Deserialize<JsonElement>(payload, SerializerOptions);
            if (element.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                chunk = content.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
