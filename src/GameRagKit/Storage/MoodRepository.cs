using System.Collections.Concurrent;
using System.Text.Json;
using GameRagKit.Mood;

namespace GameRagKit.Storage;

/// <summary>
/// Persists each NPC's current mood as a small JSON file under the same .gamerag storage
/// root used for vector index manifests. Mood is a single small value, not something that
/// benefits from vector search, so it gets its own lightweight file rather than living in
/// the vector store.
/// </summary>
public sealed class MoodRepository
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public MoodRepository(string root)
    {
        _root = root;
    }

    public async Task<MoodState?> LoadAsync(string npcId, CancellationToken cancellationToken)
    {
        var path = GetPath(npcId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MoodState>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAsync(string npcId, MoodState mood, CancellationToken cancellationToken)
    {
        var path = GetPath(npcId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var semaphore = _locks.GetOrAdd(npcId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, mood, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string GetPath(string npcId)
    {
        return Path.Combine(_root, "mood", npcId + ".json");
    }
}
