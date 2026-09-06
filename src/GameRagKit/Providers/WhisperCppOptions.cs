namespace GameRagKit.Providers;

public sealed record WhisperCppOptions
{
    public required string ModelPath { get; init; }

    // Path to the whisper.cpp CLI binary. Defaults to "whisper-cli" (resolved via PATH),
    // matching the Homebrew package's installed binary name.
    public string ExecutablePath { get; init; } = "whisper-cli";
    public string Language { get; init; } = "en";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
