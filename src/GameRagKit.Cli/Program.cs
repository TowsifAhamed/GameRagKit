using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GameRagKit;
using GameRagKit.Cli;
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

var packConfigArgument = new Argument<DirectoryInfo>("config", description: "Folder containing NPC YAML configs");
var packOutputOption = new Option<FileInfo?>("--output", description: "Path to the generated pack (zip)");
var packCommand = new Command("pack", "Bundle configs, lore, and indexes into a deployable archive")
{
    packConfigArgument,
    packOutputOption
};
packCommand.SetHandler(async (DirectoryInfo configDir, FileInfo? output) =>
{
    var destination = output ?? new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "gamerag-pack.zip"));
    await PackBuilder.BuildAsync(configDir, destination, CancellationToken.None);
    Console.WriteLine($"Pack written to {destination.FullName}");
}, packConfigArgument, packOutputOption);

var studioConfigOption = new Option<DirectoryInfo>("--config", description: "Directory containing NPC YAML configs")
{ IsRequired = true };
var studioPortOption = new Option<int>("--port", () => 5290, "Port to listen on");
var studioCommand = new Command("studio", "Launch a local web UI for editing NPC personas and testing chat")
{
    studioConfigOption,
    studioPortOption
};
studioCommand.SetHandler(async (DirectoryInfo configDir, int port) =>
{
    if (!configDir.Exists)
    {
        Console.Error.WriteLine($"Config directory not found: {configDir.FullName}");
        return;
    }

    var registryEntries = new List<KeyValuePair<string, NpcAgent>>();
    var catalogEntries = new List<StudioNpcEntry>();

    async Task LoadNpcsFromAsync(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles("*.yaml", SearchOption.AllDirectories))
        {
            var agent = await GameRAGKit.Load(file.FullName);
            agent.UseEnv();
            await agent.EnsureIndexAsync();
            registryEntries.Add(new KeyValuePair<string, NpcAgent>(agent.PersonaId, agent));
            registryEntries.Add(new KeyValuePair<string, NpcAgent>(Path.GetFileNameWithoutExtension(file.Name), agent));
            catalogEntries.Add(new StudioNpcEntry(agent.PersonaId, file.FullName));
        }
    }

    await LoadNpcsFromAsync(configDir);

    // The landing page's 3 WebGL demo scenes (StudioWeb/demos/*.html) call /ask against
    // this same server, so their NPCs (StudioWeb/demo-npcs/*.yaml -- single-npc/city's
    // named actors plus metropolis's 14 shared archetypes) are always loaded alongside
    // whatever project --config points at. Indexing ~20 small personas at startup is a
    // few seconds, not a real cost, so this stays simple rather than lazy-loading per demo.
    var demoNpcsDir = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "StudioWeb", "demo-npcs"));
    if (demoNpcsDir.Exists)
    {
        await LoadNpcsFromAsync(demoNpcsDir);
    }

    var registry = new AgentRegistry(registryEntries);
    var catalog = new StudioNpcCatalog(catalogEntries);

    var builder = WebApplication.CreateBuilder();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(port);
    });

    var authOptions = ApiAuthOptions.FromEnvironment();

    builder.Services.AddSingleton(registry);
    builder.Services.AddSingleton(catalog);
    builder.Services.AddSingleton(authOptions);
    builder.Services.AddControllers().AddApplicationPart(typeof(GameRagKit.Http.AskController).Assembly);

    var app = builder.Build();

    var webRoot = Path.Combine(AppContext.BaseDirectory, "StudioWeb");
    if (Directory.Exists(webRoot))
    {
        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });

        // The demo scenes' glTF/GLB model assets (StudioWeb/demos/assets/**/*.glb) 404'd
        // under plain UseStaticFiles(): ASP.NET Core's default FileExtensionContentTypeProvider
        // only serves extensions it recognizes (ServeUnknownFileTypes defaults to false), and
        // .glb/.gltf/.bin aren't in its built-in map. Registering them here is required for
        // the demo pages' THREE.GLTFLoader fetches to succeed at all -- verified by curl
        // returning 404 for a .glb even when the exact same bytes under a .css/.js name in
        // the same directory served fine.
        var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
        contentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
        contentTypeProvider.Mappings[".bin"] = "application/octet-stream";
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider, ContentTypeProvider = contentTypeProvider });
    }
    else
    {
        Console.Error.WriteLine($"Warning: studio web assets not found at {webRoot}; only the API will be available.");
    }

    app.UseMiddleware<ApiAuthenticationMiddleware>();
    app.MapControllers();

    Console.WriteLine($"GameRagKit Studio running at http://localhost:{port}");
    await app.RunAsync();
}, studioConfigOption, studioPortOption);

root.AddCommand(ingestCommand);
root.AddCommand(chatCommand);
root.AddCommand(serveCommand);
root.AddCommand(packCommand);
root.AddCommand(studioCommand);

return await root.InvokeAsync(args);
