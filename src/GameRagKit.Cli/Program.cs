using System.CommandLine;
using System.CommandLine.Invocation;
using GameRagKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var root = new RootCommand("GameRAGKit command-line tools");

var ingestCommand = new Command("ingest", "Build or rebuild indexes for a folder of NPC configs")
{
    new Argument<DirectoryInfo>("config", description: "Folder containing NPC YAML configs"),
    new Option<bool>("--clean", "Force a rebuild by clearing manifests")
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
        var agent = await GameRAGKit.Load(file.FullName);
        agent.UseEnv();
        await agent.EnsureIndexAsync();
    }

    Console.WriteLine("Ingestion complete.");
}, ingestCommand.Arguments[0], ingestCommand.Options[0]);

var chatCommand = new Command("chat", "Chat with an NPC via the console")
{
    new Option<FileInfo>("--npc", description: "Path to NPC YAML file") { IsRequired = true },
    new Option<string?>("--question", description: "Optional question to ask")
};
chatCommand.SetHandler(async (FileInfo configFile, string? question) =>
{
    if (!configFile.Exists)
    {
        Console.Error.WriteLine($"NPC config not found: {configFile.FullName}");
        return;
    }

    var agent = await GameRAGKit.Load(configFile.FullName);
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
}, chatCommand.Options[0], chatCommand.Options[1]);

var serveCommand = new Command("serve", "Host GameRAGKit as a lightweight HTTP service")
{
    new Option<DirectoryInfo>("--config", description: "Directory containing NPC YAML configs") { IsRequired = true },
    new Option<int>("--port", () => 5280, "Port to listen on")
};
serveCommand.SetHandler(async (DirectoryInfo configDir, int port) =>
{
    if (!configDir.Exists)
    {
        Console.Error.WriteLine($"Config directory not found: {configDir.FullName}");
        return;
    }

    var agents = new Dictionary<string, NpcAgent>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in configDir.EnumerateFiles("*.yaml", SearchOption.AllDirectories))
    {
        var agent = await GameRAGKit.Load(file.FullName);
        agent.UseEnv();
        await agent.EnsureIndexAsync();
        agents[agent.PersonaId] = agent;
        agents[Path.GetFileNameWithoutExtension(file.Name)] = agent;
    }

    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(port);
    });

    var app = builder.Build();

    app.MapPost("/ask", async (AskRequest request, CancellationToken cancellationToken) =>
    {
        if (!agents.TryGetValue(request.Npc, out var agent))
        {
            return Results.NotFound(new { error = "NPC not found" });
        }

        var options = new AskOptions(Importance: request.Importance ?? agent.DefaultImportance);
        var reply = await agent.AskAsync(request.Question, options, cancellationToken);
        return Results.Ok(new AskResponse(reply.Text, reply.Sources, reply.Scores, reply.FromCloud));
    });

    await app.RunAsync();
}, serveCommand.Options[0], serveCommand.Options[1]);

root.AddCommand(ingestCommand);
root.AddCommand(chatCommand);
root.AddCommand(serveCommand);

return await root.InvokeAsync(args);

internal sealed record AskRequest(string Npc, string Question, double? Importance);
internal sealed record AskResponse(string Answer, string[] Sources, double[] Scores, bool FromCloud);
