using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameRagKit.Storage;

public sealed class VectorIndexRepository
{
    private readonly string _root;
    private readonly ConcurrentDictionary<IndexScopeKey, SemaphoreSlim> _locks = new();

    public VectorIndexRepository(string root)
    {
        _root = root;
    }

    public async Task<VectorIndex> LoadIndexAsync(IndexScopeKey scope, CancellationToken cancellationToken)
    {
        var path = GetIndexPath(scope);
        if (!File.Exists(path))
        {
            return new VectorIndex();
        }

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken);
        return VectorIndex.FromJson(json);
    }

    public async Task SaveIndexAsync(IndexScopeKey scope, VectorIndex index, CancellationToken cancellationToken)
    {
        var path = GetIndexPath(scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = index.ToJson();
        var semaphore = _locks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<Dictionary<string, string>> LoadManifestAsync(string personaId, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(personaId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, string>(manifest, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveManifestAsync(string personaId, Dictionary<string, string> manifest, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(personaId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
    }

    public static string ComputeHash(string value)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private string GetIndexPath(IndexScopeKey scope)
    {
        var safe = scope.Scope.Replace(':', '_');
        return Path.Combine(_root, "indexes", safe + ".json");
    }

    private string GetManifestPath(string personaId)
    {
        return Path.Combine(_root, "manifests", personaId + ".json");
    }
}
