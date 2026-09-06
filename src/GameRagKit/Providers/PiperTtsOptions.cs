namespace GameRagKit.Providers;

public sealed record PiperTtsOptions
{
    public required string VoiceModelPath { get; init; }

    // Path to the piper CLI binary. Defaults to "piper" (resolved via PATH),
    // matching `pip install piper-tts`'s installed console script name.
    public string ExecutablePath { get; init; } = "piper";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
