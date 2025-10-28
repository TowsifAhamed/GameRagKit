using System.Net.Http.Json;
using System.Text.Json;

namespace GameRagKit.Providers;

public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaEmbeddingProvider(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            input = text
        };

        using var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var values = document.RootElement.GetProperty("embedding").EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
        return values;
    }
}
