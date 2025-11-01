namespace GameRagKit.Providers;

public interface IEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
