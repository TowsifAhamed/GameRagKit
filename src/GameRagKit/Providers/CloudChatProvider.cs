using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class CloudChatProvider : IChatProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _provider;

    public CloudChatProvider(HttpClient httpClient, string provider, string model)
    {
        _httpClient = httpClient;
        _provider = provider;
        _model = model;
    }

    public async Task<ChatResponse> GetChatResponseAsync(string systemPrompt, string context, string question, CancellationToken cancellationToken)
    {
        var request = BuildRequest(systemPrompt, context, question);
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

    private object BuildRequest(string systemPrompt, string context, string question)
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
            max_tokens = 512
        };
    }

    private string GetPath()
    {
        return _provider switch
        {
            "azure" => "openai/deployments/{model}/chat/completions?api-version=2024-05-01-preview".Replace("{model}", _model),
            "gemini" => $"v1beta/models/{_model}:generateContent",
            _ => "v1/chat/completions"
        };
    }
}
