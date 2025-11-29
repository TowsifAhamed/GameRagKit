using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class CloudEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _provider;
    private readonly string? _apiKey;

    public CloudEmbeddingProvider(HttpClient httpClient, string provider, string model, string? apiKey = null)
    {
        _httpClient = httpClient;
        _provider = provider;
        _model = model;
        _apiKey = apiKey;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var request = _provider == "gemini"
            ? (object)new { content = new { parts = new[] { new { text } } } }
            : new { model = _model, input = text };

        using var response = await _httpClient.PostAsJsonAsync(GetPath(), request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (_provider == "gemini")
        {
            var values = root.GetProperty("embedding").GetProperty("values")
                .EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
            return values;
        }

        var dataElement = root.TryGetProperty("data", out var data)
            ? data[0].GetProperty("embedding")
            : root.GetProperty("embedding");
        var embeddingValues = dataElement.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
        return embeddingValues;
    }

    private string GetPath()
    {
        return _provider switch
        {
            "azure" => "openai/deployments/{model}/embeddings?api-version=2024-05-01-preview".Replace("{model}", _model),
            "gemini" => $"v1beta/models/{_model}:embedContent?key={_apiKey}",
            _ => "v1/embeddings"
        };
    }
}
