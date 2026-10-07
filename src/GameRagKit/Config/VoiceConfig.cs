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
    // "piper" (local CLI, per-request subprocess) or "openai_speech" (any server with an
    // OpenAI-compatible POST /v1/audio/speech -- scripts/kokoro_server.py, Kokoro-FastAPI,
    // OpenAI itself). "kokoro" is accepted as an alias for "openai_speech".
    public string Engine { get; init; } = "piper";

    // openai_speech only.
    public string? Endpoint { get; init; }
        = null;
    public string? Voice { get; init; }
        = null;
    public string? Model { get; init; }
        = null;
    public double? Speed { get; init; }
        = null;

    public string? VoiceModelPath { get; init; }
        = null;
    public string? ExecutablePath { get; init; }
        = null;
    public int TimeoutSeconds { get; init; } = 60;

    // Optional Piper synthesis tuning; unset values keep Piper's own defaults.
    // volume < 1 leaves headroom (Piper output can peak at full scale and clip once a
    // client adds gain or spatial processing); lower noise_scale/noise_w give steadier,
    // less "warbly" speech; length_scale > 1 speaks slower.
    public double? Volume { get; init; }
        = null;
    public double? NoiseScale { get; init; }
        = null;
    public double? NoiseW { get; init; }
        = null;
    public double? LengthScale { get; init; }
        = null;
}
