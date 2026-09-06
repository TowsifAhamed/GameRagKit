namespace GameRagKit.Providers;

public interface ITextToSpeech
{
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken);
}
