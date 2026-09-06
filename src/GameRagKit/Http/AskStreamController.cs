using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("ask/stream")]
public sealed class AskStreamController : ControllerBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
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

        ApiMetrics.ObserveAskStream(request.Npc);

        var options = request.ToAskOptions();
        await foreach (var streamEvent in agent.StreamAsync(request.Question, options, cancellationToken).ConfigureAwait(false))
        {
            object payload = streamEvent switch
            {
                StreamEvent.Start start => new { type = "start", npc = start.Npc },
                StreamEvent.Chunk chunk => new { type = "chunk", text = chunk.Text },
                StreamEvent.End end => new
                {
                    type = "end",
                    sources = end.Sources,
                    actions = end.Actions.Select(a => new ActionCallPayload(a.Name, a.Args)).ToArray()
                },
                _ => throw new InvalidOperationException($"Unknown stream event: {streamEvent.GetType()}")
            };

            if (streamEvent is StreamEvent.Chunk)
            {
                ApiMetrics.ObserveAskStreamToken(request.Npc);
            }

            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
