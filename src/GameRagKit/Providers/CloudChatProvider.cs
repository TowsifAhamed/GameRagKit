using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class CloudChatProvider : IChatProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _provider;
    private readonly string? _apiKey;

    public CloudChatProvider(HttpClient httpClient, string provider, string model, string? apiKey = null)
    {
        _httpClient = httpClient;
        _provider = provider;
        _model = model;
        _apiKey = apiKey;
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var request = BuildRequest(systemPrompt, context, question, stream: false);
        using var response = await _httpClient.PostAsJsonAsync(GetPath(), request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (_provider == "gemini")
        {
            var geminiContent = document.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;
            var geminiFinishReason = document.RootElement.GetProperty("candidates")[0]
                .TryGetProperty("finishReason", out var geminiFinish) ? geminiFinish.GetString() : null;
            var geminiShouldFallback = string.Equals(geminiFinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase);
            return new ChatResponse(geminiContent.Trim(), geminiShouldFallback);
        }

        // OpenAI-compatible format (openai, azure, anthropic, groq, openrouter, mistral, etc.)
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var finishReason = document.RootElement.GetProperty("choices")[0].TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
        var shouldFallback = string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase);
        return new ChatResponse(content.Trim(), shouldFallback);
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string context, string question, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.Equals(_provider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            var geminiResponse = await GetChatResponseAsync(systemPrompt, context, question, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(geminiResponse.Text))
            {
                yield return geminiResponse.Text;
            }
            yield break;
        }

        var request = BuildRequest(systemPrompt, context, question, stream: true);
        using var message = new HttpRequestMessage(HttpMethod.Post, GetPath())
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line.Substring("data:".Length).Trim();
            if (string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            var maybeText = TryExtractDelta(payload);
            if (!string.IsNullOrEmpty(maybeText))
            {
                yield return maybeText;
            }
        }
    }

    private static string? TryExtractDelta(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed chunks
        }

        return null;
    }

    private object BuildRequest(string systemPrompt, string context, string question, bool stream)
    {
        if (_provider == "gemini")
        {
            var fullPrompt = $"{systemPrompt}\n\nCONTEXT:\n{context}\n\nPLAYER: {question}";
            return new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = fullPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.6,
                    maxOutputTokens = 512
                }
            };
        }

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = $"CONTEXT:\n{context}\n\nPLAYER: {question}" }
        };

        return new
        {
            model = _model,
            messages,
            temperature = 0.6,
            max_tokens = 512,
            stream
        };
    }

    async IAsyncEnumerable<string> IChatModel.StreamAsync(string system, string context, string user, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var token in StreamAsync(system, context, user, ct).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    private string GetPath()
    {
        return _provider switch
        {
            "azure" => "openai/deployments/{model}/chat/completions?api-version=2024-05-01-preview".Replace("{model}", _model),
            "gemini" => $"v1beta/models/{_model}:generateContent?key={_apiKey}",
            _ => "v1/chat/completions"
        };
    }
}
