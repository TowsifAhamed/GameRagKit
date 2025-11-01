using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("ingest")]
public sealed class IngestController : ControllerBase
{
    private readonly AgentRegistry _registry;

    public IngestController(AgentRegistry registry)
    {
        _registry = registry;
    }

    [HttpPost]
    public async Task<IActionResult> IngestAsync([FromBody] IngestRequest request, CancellationToken cancellationToken)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-GameRAG-Protocol", out var protocol) || protocol != "1")
        {
            return BadRequest(new { error = "Missing X-GameRAG-Protocol header." });
        }

        if (!_registry.TryGetAgent(request.Npc, out var agent))
        {
            return NotFound(new { error = "NPC not found" });
        }

        var id = await agent.HotIngestAsync(request.Text, request.Tags, cancellationToken).ConfigureAwait(false);
        return Ok(new { added = id });
    }
}

public sealed record IngestRequest(string Npc, string Text, IReadOnlyDictionary<string, string>? Tags);
