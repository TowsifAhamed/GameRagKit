namespace GameRagKit.Providers;

public interface IChatModel
{
    IAsyncEnumerable<string> StreamAsync(string system, string context, string user, CancellationToken ct);

    Task<string> InvokeAsync(string system, string context, string user, CancellationToken ct);
}
