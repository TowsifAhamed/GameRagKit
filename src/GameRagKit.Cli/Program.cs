using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using GameRagKit;
using GameRagKit.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;

var root = new RootCommand("GameRAGKit command-line tools");

var ingestConfigArgument = new Argument<DirectoryInfo>("config", description: "Folder containing NPC YAML configs");
var ingestCleanOption = new Option<bool>("--clean", "Force a rebuild by clearing manifests");
var ingestCommand = new Command("ingest", "Build or rebuild indexes for a folder of NPC configs")
{
    ingestConfigArgument,
    ingestCleanOption
};
ingestCommand.SetHandler(async (DirectoryInfo configDir, bool clean) =>
{
    if (!configDir.Exists)
    {
        Console.Error.WriteLine($"Config directory not found: {configDir.FullName}");
        return;
    }

    if (clean)
    {
        var cleanupPath = Path.Combine(configDir.FullName, ".gamerag");
        if (Directory.Exists(cleanupPath))
        {
            Directory.Delete(cleanupPath, recursive: true);
        }
    }

    var yamlFiles = configDir.EnumerateFiles("*.yaml", SearchOption.AllDirectories).ToArray();
    foreach (var file in yamlFiles)
    {
        Console.WriteLine($"Ingesting {file.FullName}...");
        await using var agent = await GameRAGKit.Load(file.FullName);
        agent.UseEnv();
        await agent.EnsureIndexAsync();
    }

    Console.WriteLine("Ingestion complete.");
}, ingestConfigArgument, ingestCleanOption);

var npcOption = new Option<FileInfo>("--npc", description: "Path to NPC YAML file") { IsRequired = true };
var questionOption = new Option<string?>("--question", description: "Optional question to ask");
var chatCommand = new Command("chat", "Chat with an NPC via the console")
{
    npcOption,
    questionOption
};
chatCommand.SetHandler(async (FileInfo configFile, string? question) =>
{
    if (!configFile.Exists)
    {
        Console.Error.WriteLine($"NPC config not found: {configFile.FullName}");
        return;
    }

    await using var agent = await GameRAGKit.Load(configFile.FullName);
    agent.UseEnv();
    await agent.EnsureIndexAsync();

    if (!string.IsNullOrWhiteSpace(question))
    {
        var reply = await agent.AskAsync(question);
        Console.WriteLine(reply.Text);
        return;
    }

    Console.WriteLine("Type a question (Ctrl+C to exit):");
    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var reply = await agent.AskAsync(line);
        Console.WriteLine(reply.Text);
    }
}, npcOption, questionOption);

var serveConfigOption = new Option<DirectoryInfo>("--config", description: "Directory containing NPC YAML configs")
{ IsRequired = true };
var servePortOption = new Option<int>("--port", () => 5280, "Port to listen on");
var serveCommand = new Command("serve", "Host GameRAGKit as a lightweight HTTP service")
{
    serveConfigOption,
    servePortOption
};
serveCommand.SetHandler(async (DirectoryInfo configDir, int port) =>
{
    if (!configDir.Exists)
    {
        Console.Error.WriteLine($"Config directory not found: {configDir.FullName}");
        return;
    }

    var registryEntries = new List<KeyValuePair<string, NpcAgent>>();
    foreach (var file in configDir.EnumerateFiles("*.yaml", SearchOption.AllDirectories))
    {
        var agent = await GameRAGKit.Load(file.FullName);
        agent.UseEnv();
        await agent.EnsureIndexAsync();
        registryEntries.Add(new KeyValuePair<string, NpcAgent>(agent.PersonaId, agent));
        registryEntries.Add(new KeyValuePair<string, NpcAgent>(Path.GetFileNameWithoutExtension(file.Name), agent));
    }

    var registry = new AgentRegistry(registryEntries);

    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(port);
    });

    var corsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS");
    builder.Services.AddCors(options =>
    {
        if (!string.IsNullOrWhiteSpace(corsOrigins))
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        }
        else
        {
            options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        }
    });

    var authOptions = ApiAuthOptions.FromEnvironment();

    builder.Services.AddSingleton(registry);
    builder.Services.AddSingleton(authOptions);
    builder.Services.AddControllers().AddApplicationPart(typeof(GameRagKit.Http.AskController).Assembly);

    var app = builder.Build();
    app.UseCors();
    app.UseMiddleware<ApiAuthenticationMiddleware>();
    app.UseHttpMetrics();
    app.MapControllers();
    app.MapMetrics("/metrics");
    await app.RunAsync();
}, serveConfigOption, servePortOption);

root.AddCommand(ingestCommand);
root.AddCommand(chatCommand);
root.AddCommand(serveCommand);

return await root.InvokeAsync(args);
