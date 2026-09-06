using GameRagKit.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

[ApiController]
[Route("ask/voice")]
public sealed class VoiceController : ControllerBase
{
    private readonly AgentRegistry _registry;

    public VoiceController(AgentRegistry registry)
    {
        _registry = registry;
    }

    [HttpPost]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> AskVoiceAsync(
        [FromForm] string npc,
        [FromForm] IFormFile audio,
        [FromForm] string? options,
        [FromForm] bool synthesizeReply = true,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-GameRAG-Protocol", out var protocol) || protocol != "1")
        {
            return BadRequest(new { error = "Missing X-GameRAG-Protocol header." });
        }

        if (!_registry.TryGetAgent(npc, out var agent))
        {
            return NotFound(new { error = "NPC not found" });
        }

        if (audio.Length == 0)
        {
            return BadRequest(new { error = "audio file is required and must not be empty." });
        }

        ApiMetrics.ObserveAsk(npc);

        AskOptionsPayload? optionsPayload = null;
        if (!string.IsNullOrWhiteSpace(options))
        {
            try
            {
                optionsPayload = System.Text.Json.JsonSerializer.Deserialize<AskOptionsPayload>(
                    options,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            }
            catch (System.Text.Json.JsonException ex)
            {
                return BadRequest(new { error = $"Invalid options JSON: {ex.Message}" });
            }
        }

        var askOptions = new AskHttpRequest(npc, string.Empty, optionsPayload).ToAskOptions();

        await using var audioStream = audio.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var audioBytes = memoryStream.ToArray();

        VoiceReply voiceReply;
        try
        {
            voiceReply = await agent.AskVoiceAsync(audioBytes, askOptions, synthesizeReply, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }

        var actions = voiceReply.Reply.Actions.Select(a => new ActionCallPayload(a.Name, a.Args)).ToArray();
        var response = new VoiceHttpResponse(
            voiceReply.Transcript,
            voiceReply.Reply.Text,
            voiceReply.Reply.Sources,
            voiceReply.Reply.Scores,
            voiceReply.Reply.FromCloud,
            actions,
            voiceReply.ReplyAudioWavBytes != null ? Convert.ToBase64String(voiceReply.ReplyAudioWavBytes) : null);

        return Ok(response);
    }
}

public sealed record VoiceHttpResponse(
    string Transcript,
    string Answer,
    string[] Sources,
    double[] Scores,
    bool FromCloud,
    ActionCallPayload[] Actions,
    string? ReplyAudioWavBase64);
