namespace GameRagKit.Providers;

public interface ISpeechToText
{
    Task<string> TranscribeAsync(byte[] audioWavBytes, CancellationToken cancellationToken);
}
