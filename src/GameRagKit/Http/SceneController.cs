using System.Text.Json;
using GameRagKit.Scenes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GameRagKit.Http;

/// <summary>
/// Multi-character conversations: one player line (typed, or spoken and transcribed) goes
/// to a group of NPCs, and the response is a server-sent event stream of who answers, each
/// NPC's reply, and -- when synthesizeReply is set -- each reply's audio in that NPC's own
/// voice. See docs/scenes.md.
/// </summary>
[ApiController]
[Route("scene")]
public sealed class SceneController : ControllerBase
{
    private const int MaxParticipants = 6;
    private const int MaxUnsaidChars = 500;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AgentRegistry _registry;
    private readonly SceneDirector _director = new();

    public SceneController(AgentRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Which of the given NPCs can listen (speech-to-text) and speak (text-to-speech) on this server.</summary>
    [HttpGet("capabilities")]
    public IActionResult Capabilities([FromQuery(Name = "npc")] string[] npcs)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var npc in npcs.Take(MaxParticipants))
        {
            if (_registry.TryGetAgent(npc, out var agent))
            {
                result[npc] = new { speechToText = agent.HasSpeechToText, textToSpeech = agent.HasTextToSpeech };
            }
        }

        return Ok(new { npcs = result });
    }

    [HttpPost("ask")]
    public async Task AskAsync([FromBody] SceneHttpRequest request, CancellationToken cancellationToken)
    {
        if (!HasProtocolHeader())
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "Missing X-GameRAG-Protocol header.", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "message is required.", cancellationToken);
            return;
        }

        if (!TryResolveParticipants(request.Participants, out var participants, out var error))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, error, cancellationToken);
            return;
        }

        await StreamSceneAsync(participants, request.Message.Trim(), request, transcript: null, cancellationToken);
    }

    [HttpPost("voice")]
    [RequestSizeLimit(25_000_000)]
    public async Task VoiceAsync(
        [FromForm] IFormFile audio,
        [FromForm] string participants,
        [FromForm] string? request,
        CancellationToken cancellationToken = default)
    {
        if (!HasProtocolHeader())
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "Missing X-GameRAG-Protocol header.", cancellationToken);
            return;
        }

        if (audio == null || audio.Length == 0)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "audio file is required and must not be empty.", cancellationToken);
            return;
        }

        SceneParticipantPayload[]? participantPayloads;
        SceneHttpRequest settings;
        try
        {
            participantPayloads = JsonSerializer.Deserialize<SceneParticipantPayload[]>(participants, SerializerOptions);
            settings = string.IsNullOrWhiteSpace(request)
                ? new SceneHttpRequest()
                : JsonSerializer.Deserialize<SceneHttpRequest>(request, SerializerOptions) ?? new SceneHttpRequest();
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, $"Invalid JSON: {ex.Message}", cancellationToken);
            return;
        }

        if (!TryResolveParticipants(participantPayloads, out var resolved, out var error))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, error, cancellationToken);
            return;
        }

        var listener = resolved.FirstOrDefault(p => p.Agent.HasSpeechToText);
        if (listener == null)
        {
            await WriteErrorAsync(StatusCodes.Status503ServiceUnavailable,
                "No speech-to-text configured for any participant. Set providers.voice.speech_to_text or STT_MODEL_PATH.", cancellationToken);
            return;
        }

        byte[] audioBytes;
        await using (var audioStream = audio.OpenReadStream())
        using (var memoryStream = new MemoryStream())
        {
            await audioStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            audioBytes = memoryStream.ToArray();
        }

        string transcript;
        try
        {
            transcript = (await listener.Agent.TranscribeAsync(audioBytes, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            await WriteErrorAsync(StatusCodes.Status503ServiceUnavailable, ex.Message, cancellationToken);
            return;
        }

        if (IsBlankTranscript(transcript))
        {
            await WriteErrorAsync(StatusCodes.Status422UnprocessableEntity, "No speech detected in the recording.", cancellationToken);
            return;
        }

        await StreamSceneAsync(resolved, transcript, settings, transcript, cancellationToken);
    }

    private async Task StreamSceneAsync(
        IReadOnlyList<SceneParticipant> participants,
        string message,
        SceneHttpRequest request,
        string? transcript,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        if (transcript != null)
        {
            await WriteEventAsync(new { type = "transcript", text = transcript }, cancellationToken);
        }

        foreach (var participant in participants)
        {
            ApiMetrics.ObserveAsk(participant.NpcId);
        }

        var history = (request.History ?? Array.Empty<SceneLinePayload>())
            .Where(line => !string.IsNullOrWhiteSpace(line.Speaker) && !string.IsNullOrWhiteSpace(line.Text))
            .TakeLast(20)
            .Select(line => new SceneLine(line.Speaker!, line.Text!, line.Interrupted ?? false))
            .ToList();

        var unsaid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in request.Unsaid ?? Array.Empty<SceneLinePayload>())
        {
            if (!string.IsNullOrWhiteSpace(line.Speaker) && !string.IsNullOrWhiteSpace(line.Text))
            {
                var text = line.Text!.Trim();
                unsaid[line.Speaker!] = text.Length > MaxUnsaidChars ? text[^MaxUnsaidChars..] : text;
            }
        }

        var options = new SceneOptions
        {
            AskOptions = new AskHttpRequest(participants[0].NpcId, message, request.Options).ToAskOptions(),
            MaxResponders = Math.Clamp(request.MaxResponders ?? 2, 1, MaxParticipants),
            MaxReactions = Math.Clamp(request.MaxReactions ?? 1, 0, 3),
            UseLlmRouter = request.UseLlmRouter ?? true,
            SynthesizeSpeech = request.SynthesizeReply ?? false,
            Unsaid = unsaid
        };

        try
        {
            await foreach (var sceneEvent in _director.RunAsync(participants, message, history, options, cancellationToken).ConfigureAwait(false))
            {
                object payload = sceneEvent switch
                {
                    SceneEvent.Routing routing => new { type = "routing", responders = routing.Responders, method = routing.Method },
                    SceneEvent.Thinking thinking => new { type = "thinking", npc = thinking.Npc },
                    SceneEvent.Turn turn => new
                    {
                        type = "turn",
                        index = turn.Index,
                        npc = turn.Npc,
                        reason = turn.Reason,
                        text = turn.Reply.Text,
                        fromCloud = turn.Reply.FromCloud,
                        sources = turn.Reply.Sources,
                        actions = turn.Reply.Actions.Select(a => new ActionCallPayload(a.Name, a.Args)).ToArray(),
                        mood = turn.Reply.Mood != null ? new MoodPayload(turn.Reply.Mood.Value, turn.Reply.Mood.Intensity) : null
                    },
                    SceneEvent.Audio audio => new
                    {
                        type = "audio",
                        index = audio.Index,
                        npc = audio.Npc,
                        wavBase64 = audio.WavBytes != null ? Convert.ToBase64String(audio.WavBytes) : null,
                        error = audio.Error
                    },
                    _ => throw new InvalidOperationException($"Unknown scene event: {sceneEvent.GetType()}")
                };

                await WriteEventAsync(payload, cancellationToken);
            }

            await WriteEventAsync(new { type = "done" }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client went away mid-scene.
        }
        catch (Exception ex)
        {
            // Headers are already sent, so report failures in-band.
            await WriteEventAsync(new { type = "error", error = ex.Message }, cancellationToken);
        }
    }

    private bool TryResolveParticipants(
        IReadOnlyList<SceneParticipantPayload>? payloads,
        out IReadOnlyList<SceneParticipant> participants,
        out string error)
    {
        participants = Array.Empty<SceneParticipant>();
        if (payloads == null || payloads.Count == 0)
        {
            error = "participants must list at least one NPC.";
            return false;
        }

        if (payloads.Count > MaxParticipants)
        {
            error = $"A scene supports at most {MaxParticipants} participants.";
            return false;
        }

        var resolved = new List<SceneParticipant>();
        foreach (var payload in payloads)
        {
            if (string.IsNullOrWhiteSpace(payload.Npc) || !_registry.TryGetAgent(payload.Npc, out var agent))
            {
                error = $"NPC not found: {payload.Npc}";
                return false;
            }

            if (resolved.Any(p => string.Equals(p.NpcId, payload.Npc, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(payload.Name) ? payload.Npc : payload.Name.Trim();
            resolved.Add(new SceneParticipant(payload.Npc, name, agent));
        }

        participants = resolved;
        error = string.Empty;
        return true;
    }

    // whisper.cpp emits bracketed annotations instead of text for silence/noise.
    private static bool IsBlankTranscript(string transcript)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(transcript, @"\[[^\]]*\]|\([^)]*\)", string.Empty).Trim();
        return stripped.Length == 0;
    }

    private bool HasProtocolHeader()
        => HttpContext.Request.Headers.TryGetValue("X-GameRAG-Protocol", out var protocol) && protocol == "1";

    private async Task WriteErrorAsync(int statusCode, string message, CancellationToken cancellationToken)
    {
        Response.StatusCode = statusCode;
        await Response.WriteAsJsonAsync(new { error = message }, cancellationToken);
    }

    private async Task WriteEventAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed record SceneHttpRequest
{
    public SceneParticipantPayload[]? Participants { get; init; }
    public string? Message { get; init; }
    public SceneLinePayload[]? History { get; init; }

    /// <summary>Per NPC (speaker = NPC id): what it was interrupted before saying.</summary>
    public SceneLinePayload[]? Unsaid { get; init; }
    public AskOptionsPayload? Options { get; init; }
    public int? MaxResponders { get; init; }
    public int? MaxReactions { get; init; }
    public bool? UseLlmRouter { get; init; }
    public bool? SynthesizeReply { get; init; }
}

public sealed record SceneParticipantPayload(string Npc, string? Name);

public sealed record SceneLinePayload(string? Speaker, string? Text, bool? Interrupted = null);
