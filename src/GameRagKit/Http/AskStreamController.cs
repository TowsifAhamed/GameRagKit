using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("ask/stream")]
public sealed class AskStreamController : ControllerBase
{
    private readonly AgentRegistry _registry;

    public AskStreamController(AgentRegistry registry)
    {
        _registry = registry;
    }

    [HttpPost]
    public async Task StreamAsync([FromBody] AskHttpRequest request, CancellationToken cancellationToken)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-GameRAG-Protocol", out var protocol) || protocol != "1")
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "Missing X-GameRAG-Protocol header." }, cancellationToken);
            return;
        }

        if (!_registry.TryGetAgent(request.Npc, out var agent))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { error = "NPC not found" }, cancellationToken);
            return;
        }

        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        var options = request.ToAskOptions();
        await foreach (var token in agent.StreamAsync(request.Question, options, cancellationToken).ConfigureAwait(false))
        {
            await Response.WriteAsync($"data: {token}\n\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
