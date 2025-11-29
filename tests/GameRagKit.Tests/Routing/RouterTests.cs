using System.Threading;
using FluentAssertions;
using GameRagKit;
using GameRagKit.Config;
using GameRagKit.Pipeline;
using GameRagKit.Providers;
using GameRagKit.Routing;

namespace GameRagKit.Tests.Routing;

public sealed class RouterTests
{
    private static readonly PersonaConfig Persona = new()
    {
        Id = "npc",
        SystemPrompt = "system"
    };

    private static readonly RagConfig Rag = new();

    [Fact]
    public async Task ForceLocal_Returns_Local_Provider()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(ForceLocal: true);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<LLamaSharpClient>();
    }

    [Fact]
    public async Task ForceCloud_Returns_Cloud_Provider()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(ForceCloud: true);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<CloudChatProvider>();
    }

    [Fact]
    public async Task Hybrid_Importance_Chooses_Cloud_When_Above_Threshold()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: 0.9);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<CloudChatProvider>();
    }

    [Fact]
    public async Task Hybrid_Importance_Uses_Local_When_Under_Threshold()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: 0.1);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<LLamaSharpClient>();
    }

    [Fact]
    public async Task Hybrid_Importance_Clamps_Above_One_To_Cloud()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: 7);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<CloudChatProvider>();
    }

    [Fact]
    public async Task Hybrid_Importance_Below_Zero_Clamps_To_Local()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4", routingDefaultImportance: 0.8);
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: -5);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<LLamaSharpClient>();
    }

    [Fact]
    public async Task Hybrid_Importance_NaN_Uses_Config_Default()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4", routingDefaultImportance: 0.9);
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: double.NaN);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<CloudChatProvider>();
    }

    [Fact]
    public async Task Hybrid_Importance_Defaults_To_Persona_When_Unspecified()
    {
        var config = CreateConfig(
            localEngine: "llamasharp",
            cloudModel: "gpt-4",
            personaDefaultImportance: 0.8);
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions();

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<CloudChatProvider>();
    }

    [Fact]
    public async Task ResolveEmbedderAsync_Prefers_Local_When_Available()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4");
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();

        var provider = await router.ResolveEmbedderAsync(config, runtime, CancellationToken.None);

        provider.Should().BeOfType<LLamaSharpClient>();
    }

    [Fact]
    public async Task Hybrid_Importance_Zero_Uses_Local_When_Config_Default_Is_High()
    {
        var config = CreateConfig(localEngine: "llamasharp", cloudModel: "gpt-4", routingDefaultImportance: 0.8);
        var router = new Router(new ProviderResolver());
        var runtime = new ProviderRuntimeOptions();
        var options = new AskOptions(Importance: 0);

        var provider = await router.ResolveChatAsync(config, runtime, options, CancellationToken.None);

        provider.Should().BeOfType<LLamaSharpClient>();
    }

    private static NpcConfig CreateConfig(
        string localEngine,
        string cloudModel,
        double routingDefaultImportance = 0.2,
        double? personaDefaultImportance = null)
    {
        var persona = Persona with { DefaultImportance = personaDefaultImportance };

        return new NpcConfig
        {
            Persona = persona,
            Rag = Rag,
            Providers = new ProvidersConfig
            {
                Routing = new RoutingConfig
                {
                    DefaultImportance = routingDefaultImportance
                },
                Local = new LocalProviderConfig
                {
                    Engine = localEngine,
                    ChatModel = "llama3",
                    EmbedModel = "nomic",
                    Endpoint = "http://localhost:11434"
                },
                Cloud = new CloudProviderConfig
                {
                    Provider = "openai",
                    ChatModel = cloudModel,
                    EmbedModel = "text-embedding-3-small",
                    Endpoint = "https://api.openai.com/"
                }
            }
        };
    }
}
