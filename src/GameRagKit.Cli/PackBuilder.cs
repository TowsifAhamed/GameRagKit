using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameRagKit.Config;

namespace GameRagKit.Cli;

public static class PackBuilder
{
    public static async Task BuildAsync(DirectoryInfo configDir, FileInfo outputFile, CancellationToken cancellationToken)
    {
        if (!configDir.Exists)
        {
            throw new DirectoryNotFoundException($"Config directory not found: {configDir.FullName}");
        }

        var yamlFiles = configDir.EnumerateFiles("*.yaml", SearchOption.AllDirectories).ToArray();
        if (yamlFiles.Length == 0)
        {
            throw new InvalidOperationException("No NPC YAML files were found to pack.");
        }

        if (outputFile.Exists)
        {
            outputFile.Delete();
        }

        var npcIds = new List<string>();
        NpcConfig? referenceConfig = null;
        foreach (var file in yamlFiles)
        {
            var yaml = await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            var config = NpcConfig.LoadFromYaml(yaml);
            npcIds.Add(config.Persona.Id);
            referenceConfig ??= config;
        }

        using var archive = ZipFile.Open(outputFile.FullName, ZipArchiveMode.Create);

        void AddFile(string filePath)
        {
            var relative = Path.GetRelativePath(configDir.FullName, filePath);
            var entryName = string.IsNullOrWhiteSpace(relative) ? Path.GetFileName(filePath) : relative;
            archive.CreateEntryFromFile(filePath, entryName);
        }

        foreach (var file in yamlFiles)
        {
            AddFile(file.FullName);
        }

        foreach (var dir in new[] { "world", "region", "faction", "npc" })
        {
            var fullPath = Path.Combine(configDir.FullName, dir);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                AddFile(file);
            }
        }

        var storageRoot = Path.Combine(configDir.FullName, ".gamerag");
        var indexNames = Array.Empty<string>();
        if (Directory.Exists(storageRoot))
        {
            var indexPath = Path.Combine(storageRoot, "indexes");
            if (Directory.Exists(indexPath))
            {
                indexNames = Directory.GetFiles(indexPath, "*.json", SearchOption.AllDirectories)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray()!;
            }

            foreach (var file in Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
            {
                AddFile(file);
            }
        }

        var manifest = new PackManifest
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ChunkSize = referenceConfig?.Rag.ChunkSize ?? 0,
            Overlap = referenceConfig?.Rag.Overlap ?? 0,
            ChatModel = referenceConfig?.Providers.Local?.ChatModel ?? referenceConfig?.Providers.Cloud?.ChatModel,
            EmbedModel = referenceConfig?.Providers.Local?.EmbedModel ?? referenceConfig?.Providers.Cloud?.EmbedModel,
            EmbeddingDimensions = GameRagOptions.FromEnvironment().Database.EmbeddingDimensions,
            NpcIds = npcIds.ToArray(),
            Indexes = indexNames
        };

        var manifestEntry = archive.CreateEntry("pack.json");
        await using (var stream = manifestEntry.Open())
        await using (var writer = new StreamWriter(stream))
        {
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            await writer.WriteAsync(json).ConfigureAwait(false);
        }
    }

    private sealed record PackManifest
    {
        public DateTimeOffset CreatedAtUtc { get; init; }
        public int ChunkSize { get; init; }
        public int Overlap { get; init; }
        public string? ChatModel { get; init; }
        public string? EmbedModel { get; init; }
        public int EmbeddingDimensions { get; init; }
        public string[] NpcIds { get; init; } = Array.Empty<string>();
        public string[] Indexes { get; init; } = Array.Empty<string>();
    }
}
