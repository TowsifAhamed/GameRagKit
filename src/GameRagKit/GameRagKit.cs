using System;
using System.Net.Http;
using GameRagKit.Config;
using GameRagKit.Pipeline;
using GameRagKit.Routing;
using GameRagKit.Storage;
using GameRagKit.Text;
using GameRagKit.VectorStores;

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

        var options = GameRagOptions.FromEnvironment();
        var vectorStore = CreateVectorStore(options.Database);
        var chunker = new TextChunker();
        var manifestRepository = new VectorIndexRepository(storageRoot);
        var providerResolver = new ProviderResolver();
        var router = new Router(providerResolver);

        return new NpcAgent(config, configDirectory, chunker, manifestRepository, vectorStore, router);
    }

    private static IVectorStore CreateVectorStore(DatabaseOptions database)
    {
        var kind = string.IsNullOrWhiteSpace(database.Kind) ? "pgvector" : database.Kind;

        return kind.ToLowerInvariant() switch
        {
            "pgvector" => new PgVectorStore(database.ConnectionString ?? throw new InvalidOperationException("CONNECTION_STRING is required for pgvector.")),
            "qdrant" => new QdrantStore(CreateQdrantClient(database), database.QdrantCollection ?? "rag"),
            _ => throw new NotSupportedException($"Unknown vector store: {database.Kind}")
        };
    }

    private static HttpClient CreateQdrantClient(DatabaseOptions database)
    {
        var endpoint = database.QdrantEndpoint ?? "http://localhost:6333";
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/")
        };

        var apiKey = Environment.GetEnvironmentVariable("API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Remove("api-key");
            httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        return httpClient;
    }
}
