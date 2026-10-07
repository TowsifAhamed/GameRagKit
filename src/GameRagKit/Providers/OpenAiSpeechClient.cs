using System.Net.Http.Headers;

namespace GameRagKit.Providers;

/// <summary>
/// Text-to-speech against any server implementing OpenAI's POST /v1/audio/speech: the
/// bundled scripts/kokoro_server.py (Kokoro-82M, local and far more natural than Piper),
/// Kokoro-FastAPI, or OpenAI's hosted TTS. Unlike Piper, the model stays loaded in that
/// server between requests, so per-line latency is just synthesis time.
/// </summary>
public sealed class OpenAiSpeechClient : ITextToSpeech
{
    // One shared client: TTS is called once per spoken line, and a new HttpClient per call
    // would churn sockets. Per-request timeouts come from OpenAiSpeechOptions instead.
    private static readonly HttpClient SharedHttpClient = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly OpenAiSpeechOptions _options;
    private readonly HttpClient _httpClient;

    public OpenAiSpeechClient(OpenAiSpeechOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public static bool IsEngine(string? engine)
        => string.Equals(engine, "openai_speech", StringComparison.OrdinalIgnoreCase)
           || string.Equals(engine, "kokoro", StringComparison.OrdinalIgnoreCase);

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.Endpoint.TrimEnd('/') + "/"), "v1/audio/speech"))
        {
            // Buffered string content (with Content-Length), not JsonContent: JsonContent
            // streams chunked, which simple local TTS servers often read as an empty body.
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = _options.Model,
                    input = text,
                    voice = _options.Voice,
                    response_format = "wav",
                    speed = _options.Speed ?? 1.0
                }),
                System.Text.Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Text-to-speech server at {_options.Endpoint} timed out after {_options.Timeout}.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Text-to-speech server at {_options.Endpoint} is unreachable ({ex.Message}). Is it running?", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300));
                throw new InvalidOperationException($"Text-to-speech server returned {(int)response.StatusCode}: {detail}");
            }

            return body;
        }
    }
}
