using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("ask")]
public sealed class AskController : ControllerBase
{
    private readonly AgentRegistry _registry;

    public AskController(AgentRegistry registry)
    {
        _registry = registry;
    }

    [HttpPost]
    public async Task<IActionResult> AskAsync([FromBody] AskHttpRequest request, CancellationToken cancellationToken)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-GameRAG-Protocol", out var protocol) || protocol != "1")
        {
            return BadRequest(new { error = "Missing X-GameRAG-Protocol header." });
        }

        if (!_registry.TryGetAgent(request.Npc, out var agent))
        {
            return NotFound(new { error = "NPC not found" });
        }

        var options = request.ToAskOptions();
        var reply = await agent.AskAsync(request.Question, options, cancellationToken).ConfigureAwait(false);
        var response = new AskHttpResponse(reply.Text, reply.Sources, reply.Scores, reply.FromCloud);
        return Ok(response);
    }
}

public sealed record AskHttpRequest(string Npc, string Question, AskOptionsPayload? Options)
{
    public AskOptions ToAskOptions()
    {
        return new AskOptions(
            TopK: Options?.TopK ?? 4,
            InCharacter: Options?.InCharacter ?? true,
            SystemOverride: Options?.SystemOverride,
            Importance: Options?.Importance ?? 0.2,
            ForceLocal: Options?.ForceLocal ?? false,
            ForceCloud: Options?.ForceCloud ?? false);
    }
}

public sealed record AskOptionsPayload
{
    public int? TopK { get; init; }
    public bool? InCharacter { get; init; }
    public string? SystemOverride { get; init; }
    public double? Importance { get; init; }
    public bool? ForceLocal { get; init; }
    public bool? ForceCloud { get; init; }
}

public sealed record AskHttpResponse(string Answer, string[] Sources, double[] Scores, bool FromCloud);
