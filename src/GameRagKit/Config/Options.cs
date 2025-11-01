namespace GameRagKit.Config;

public sealed class GameRagOptions
{
    public DatabaseOptions Database { get; init; } = new();
    public ProviderOptions Providers { get; init; } = new();

    public static GameRagOptions FromEnvironment()
    {
        return new GameRagOptions
        {
            Database = DatabaseOptions.FromEnvironment(),
            Providers = ProviderOptions.FromEnvironment()
        };
    }
}

public sealed class DatabaseOptions
{
    public string Kind { get; init; } = "pgvector";
    public string? ConnectionString { get; init; }
        = null;
    public string? QdrantCollection { get; init; }
        = "rag";
    public string? QdrantEndpoint { get; init; }
        = "http://localhost:6333";

    public static DatabaseOptions FromEnvironment()
    {
        return new DatabaseOptions
        {
            Kind = Environment.GetEnvironmentVariable("DB") ?? "pgvector",
            ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING"),
            QdrantCollection = Environment.GetEnvironmentVariable("QDRANT_COLLECTION") ?? "rag",
            QdrantEndpoint = Environment.GetEnvironmentVariable("QDRANT_ENDPOINT") ?? Environment.GetEnvironmentVariable("ENDPOINT") ?? "http://localhost:6333"
        };
    }
}

public sealed class ProviderOptions
{
    public string Mode { get; init; } = "hybrid";
    public string? ApiKey { get; init; }
        = null;
    public string? Endpoint { get; init; }
        = null;
    public string? Provider { get; init; }
        = "openai";
    public LocalProviderOptions Local { get; init; } = new();
    public CloudProviderOptions Cloud { get; init; } = new();

    public static ProviderOptions FromEnvironment()
    {
        return new ProviderOptions
        {
            Mode = Environment.GetEnvironmentVariable("MODE") ?? "hybrid",
            ApiKey = Environment.GetEnvironmentVariable("API_KEY"),
            Endpoint = Environment.GetEnvironmentVariable("ENDPOINT"),
            Provider = Environment.GetEnvironmentVariable("PROVIDER") ?? "openai",
            Local = LocalProviderOptions.FromEnvironment(),
            Cloud = CloudProviderOptions.FromEnvironment()
        };
    }
}

public sealed class LocalProviderOptions
{
    public string Engine { get; init; } = "ollama";
    public string? Endpoint { get; init; }
        = "http://localhost:11434";
    public string? ChatModel { get; init; }
        = "llama3";
    public string? EmbedModel { get; init; }
        = "nomic-embed-text";

    public static LocalProviderOptions FromEnvironment()
    {
        return new LocalProviderOptions
        {
            Engine = Environment.GetEnvironmentVariable("LOCAL_ENGINE") ?? "ollama",
            Endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? Environment.GetEnvironmentVariable("LOCAL_ENDPOINT") ?? "http://localhost:11434",
            ChatModel = Environment.GetEnvironmentVariable("LOCAL_CHAT_MODEL") ?? Environment.GetEnvironmentVariable("CHAT_MODEL"),
            EmbedModel = Environment.GetEnvironmentVariable("LOCAL_EMBED_MODEL") ?? Environment.GetEnvironmentVariable("EMBED_MODEL")
        };
    }
}

public sealed class CloudProviderOptions
{
    public string Provider { get; init; } = "openai";
    public string? Endpoint { get; init; }
        = null;
    public string? ChatModel { get; init; }
        = "gpt-4o-mini";
    public string? EmbedModel { get; init; }
        = "text-embedding-3-small";

    public static CloudProviderOptions FromEnvironment()
    {
        return new CloudProviderOptions
        {
            Provider = Environment.GetEnvironmentVariable("PROVIDER") ?? "openai",
            Endpoint = Environment.GetEnvironmentVariable("CLOUD_ENDPOINT") ?? Environment.GetEnvironmentVariable("ENDPOINT"),
            ChatModel = Environment.GetEnvironmentVariable("CLOUD_CHAT_MODEL"),
            EmbedModel = Environment.GetEnvironmentVariable("CLOUD_EMBED_MODEL")
        };
    }
}
