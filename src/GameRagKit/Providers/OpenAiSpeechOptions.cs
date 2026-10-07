namespace GameRagKit.Providers;

public sealed record OpenAiSpeechOptions
{
    // scripts/kokoro_server.py's default; Kokoro-FastAPI uses the same port.
    public const string DefaultEndpoint = "http://127.0.0.1:8880";

    public string Endpoint { get; init; } = DefaultEndpoint;
    public required string Voice { get; init; }
    public string Model { get; init; } = "kokoro";
    public double? Speed { get; init; }
    public string? ApiKey { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
