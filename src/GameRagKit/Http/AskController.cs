using GameRagKit.Actions;
using GameRagKit.Mood;
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

        ApiMetrics.ObserveAsk(request.Npc);

        var options = request.ToAskOptions();
        var reply = await agent.AskAsync(request.Question, options, cancellationToken).ConfigureAwait(false);
        var actions = reply.Actions.Select(a => new ActionCallPayload(a.Name, a.Args)).ToArray();
        var mood = reply.Mood != null ? new MoodPayload(reply.Mood.Value, reply.Mood.Intensity) : null;
        var response = new AskHttpResponse(reply.Text, reply.Sources, reply.Scores, reply.FromCloud, actions, mood);
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
            Importance: Options?.Importance ?? double.NaN,
            ForceLocal: Options?.ForceLocal ?? false,
            ForceCloud: Options?.ForceCloud ?? false,
            State: Options?.State,
            WorldState: Options?.WorldState?.ToWorldState());
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
    public string? State { get; init; }
    public WorldStatePayload? WorldState { get; init; }
}

public sealed record WorldStatePayload
{
    public string? TimeOfDay { get; init; }
    public bool? InCombat { get; init; }
    public List<NearbyEntityPayload>? NearbyEntities { get; init; }
    public List<InventoryItemPayload>? PlayerInventory { get; init; }
    public Dictionary<string, string>? Custom { get; init; }

    public WorldState ToWorldState()
    {
        return new WorldState
        {
            TimeOfDay = TimeOfDay,
            InCombat = InCombat,
            NearbyEntities = NearbyEntities?
                .Select(e => new NearbyEntity(e.Id, e.Type, e.DistanceMeters))
                .ToArray() ?? Array.Empty<NearbyEntity>(),
            PlayerInventory = PlayerInventory?
                .Select(i => new InventoryItem(i.ItemId, i.Quantity ?? 1))
                .ToArray() ?? Array.Empty<InventoryItem>(),
            Custom = Custom ?? new Dictionary<string, string>()
        };
    }
}

public sealed record NearbyEntityPayload(string Id, string? Type, double? DistanceMeters);

public sealed record InventoryItemPayload(string ItemId, int? Quantity);

public sealed record AskHttpResponse(string Answer, string[] Sources, double[] Scores, bool FromCloud, ActionCallPayload[] Actions, MoodPayload? Mood);

public sealed record ActionCallPayload(string Name, IReadOnlyDictionary<string, string> Args);

public sealed record MoodPayload(string Value, double Intensity);
