using GameRagKit.Config;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;

namespace GameRagKit;

public static class GameRAGKit
{
    public static async Task<NpcAgent> Load(string configPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"NPC config not found: {configPath}");
        }

        var yaml = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = NpcConfig.LoadFromYaml(yaml);
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var storageRoot = Path.Combine(configDirectory, ".gamerag");
        Directory.CreateDirectory(storageRoot);

        var chunker = new TextChunker();
        var indexRepository = new VectorIndexRepository(storageRoot);
        var providerResolver = new ProviderResolver();
        var router = new ChatRouter(providerResolver);

        return new NpcAgent(config, configDirectory, chunker, indexRepository, router);
    }
}
