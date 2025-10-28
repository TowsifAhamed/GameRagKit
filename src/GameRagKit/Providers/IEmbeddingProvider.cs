namespace GameRagKit.Providers;

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}
