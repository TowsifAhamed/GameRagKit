namespace GameRagKit.Config;

public sealed record VoiceConfig
{
    public SpeechToTextConfig? SpeechToText { get; init; }
        = null;
    public TextToSpeechConfig? TextToSpeech { get; init; }
        = null;
}

public sealed record SpeechToTextConfig
{
    // Currently only "whisper_cpp" is supported.
    public string Engine { get; init; } = "whisper_cpp";
    public string? ModelPath { get; init; }
        = null;
    public string? ExecutablePath { get; init; }
        = null;
    public string Language { get; init; } = "en";
    public int TimeoutSeconds { get; init; } = 60;
}

public sealed record TextToSpeechConfig
{
    // Currently only "piper" is supported.
    public string Engine { get; init; } = "piper";
    public string? VoiceModelPath { get; init; }
        = null;
    public string? ExecutablePath { get; init; }
        = null;
    public int TimeoutSeconds { get; init; } = 60;
}
