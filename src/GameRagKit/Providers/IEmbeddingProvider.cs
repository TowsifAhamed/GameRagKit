namespace GameRagKit.Providers;

public interface IEmbeddingProvider : IEmbedder
{
    new Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);

    Task<float[]> IEmbedder.EmbedAsync(string text, CancellationToken ct)
        => EmbedAsync(text, ct);
}
