using GameRagKit.Config;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("studio/api")]
public sealed class StudioController : ControllerBase
{
    private readonly StudioNpcCatalog _catalog;
    private readonly AgentRegistry _registry;

    public StudioController(StudioNpcCatalog catalog, AgentRegistry registry)
    {
        _catalog = catalog;
        _registry = registry;
    }

    [HttpGet("npcs")]
    public IActionResult ListNpcs()
    {
        var npcs = _catalog.All.Select(e => new StudioNpcSummary(e.PersonaId)).ToArray();
        return Ok(npcs);
    }

    [HttpGet("npcs/{personaId}/yaml")]
    public async Task<IActionResult> GetYaml(string personaId, CancellationToken cancellationToken)
    {
        if (!_catalog.TryGetYamlPath(personaId, out var yamlPath))
        {
            return NotFound(new { error = "NPC not found" });
        }

        var yaml = await System.IO.File.ReadAllTextAsync(yamlPath, cancellationToken).ConfigureAwait(false);
        return Ok(new StudioYamlResponse(yaml));
    }

    [HttpPost("npcs/{personaId}/yaml")]
    public async Task<IActionResult> SaveYaml(string personaId, [FromBody] StudioSaveYamlRequest request, CancellationToken cancellationToken)
    {
        if (!_catalog.TryGetYamlPath(personaId, out var yamlPath))
        {
            return NotFound(new { error = "NPC not found" });
        }

        var originalYaml = await System.IO.File.ReadAllTextAsync(yamlPath, cancellationToken).ConfigureAwait(false);

        var edits = new List<PersonaFieldEdit>();
        if (request.SystemPrompt != null)
        {
            edits.Add(new PersonaFieldEdit("system_prompt", request.SystemPrompt));
        }

        if (request.Style != null)
        {
            edits.Add(new PersonaFieldEdit("style", request.Style));
        }

        if (request.MoodTracking.HasValue)
        {
            edits.Add(new PersonaFieldEdit("mood_tracking", request.MoodTracking.Value ? "true" : "false", IsRawScalar: true));
        }

        var patchResult = PersonaYamlPatcher.ApplyEdits(originalYaml, edits);
        if (!patchResult.Success)
        {
            return UnprocessableEntity(new { error = $"Could not apply edits without reformatting the file: {patchResult.UnsupportedReason}" });
        }

        // Validate the patched YAML actually parses and still describes the same NPC
        // before writing it to disk, so a patcher bug can never corrupt the file.
        NpcConfig patchedConfig;
        try
        {
            patchedConfig = NpcConfig.LoadFromYaml(patchResult.UpdatedYaml!);
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { error = $"Patched YAML failed to parse, changes were not saved: {ex.Message}" });
        }

        if (!string.Equals(patchedConfig.Persona.Id, personaId, StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new { error = "Patched YAML's persona.id changed unexpectedly; changes were not saved." });
        }

        await System.IO.File.WriteAllTextAsync(yamlPath, patchResult.UpdatedYaml!, cancellationToken).ConfigureAwait(false);

        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(yamlPath)) ?? Directory.GetCurrentDirectory();
        var reloadedAgent = await GameRAGKit.Load(yamlPath, cancellationToken).ConfigureAwait(false);
        reloadedAgent.UseEnv();
        await reloadedAgent.EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        await _registry.ReplaceAgentAsync(reloadedAgent, cancellationToken).ConfigureAwait(false);

        return Ok(new StudioYamlResponse(patchResult.UpdatedYaml!));
    }
}

public sealed record StudioNpcSummary(string PersonaId);

public sealed record StudioYamlResponse(string Yaml);

public sealed record StudioSaveYamlRequest(string? SystemPrompt, string? Style, bool? MoodTracking);
