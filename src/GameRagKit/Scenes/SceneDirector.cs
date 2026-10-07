using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace GameRagKit.Scenes;

/// <summary>
/// Runs one beat of a multi-character conversation: the player says something to a group
/// of NPCs, an intent router decides which of them should answer, those NPCs reply in
/// sequence (each seeing what the others just said), and an NPC that is addressed by name
/// in another NPC's reply gets to react. Each NPC keeps its own persona, lore, actions,
/// mood and -- when text-to-speech is enabled -- its own voice.
///
/// Speech synthesis for a turn runs in the background while the next NPC's reply is being
/// generated, so a client can start playing the first line before the whole beat is done.
/// </summary>
public sealed class SceneDirector
{
    private static readonly Regex GroupAddress = new(
        @"\b(every(one|body)|all of you|you all|y'?all|both of you|you (two|three|guys)|guys|folks|each of you)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async IAsyncEnumerable<SceneEvent> RunAsync(
        IReadOnlyList<SceneParticipant> participants,
        string playerLine,
        IReadOnlyList<SceneLine> history,
        SceneOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (participants.Count == 0)
        {
            throw new ArgumentException("A scene needs at least one participant.", nameof(participants));
        }

        options ??= new SceneOptions();
        var channel = Channel.CreateUnbounded<SceneEvent>();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var producer = Task.Run(async () =>
        {
            try
            {
                await ProduceAsync(channel.Writer, participants, playerLine, history, options, linkedCts.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var sceneEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return sceneEvent;
            }
        }
        finally
        {
            linkedCts.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch
            {
                // Already surfaced through the channel, or the consumer stopped listening.
            }
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<SceneEvent> writer,
        IReadOnlyList<SceneParticipant> participants,
        string playerLine,
        IReadOnlyList<SceneLine> history,
        SceneOptions options,
        CancellationToken cancellationToken)
    {
        var (responders, method) = await PickRespondersAsync(participants, playerLine, history, options, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(new SceneEvent.Routing(responders.Select(p => p.NpcId).ToArray(), method), cancellationToken).ConfigureAwait(false);

        var transcript = new List<SceneLine>(history) { new(SceneLine.PlayerSpeaker, playerLine) };
        var synthesisTasks = new List<Task>();
        var spoken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(SceneParticipant Participant, string Reason, SceneLine? Prompt)>(
            responders.Select(p => (p, "routed", (SceneLine?)null)));
        var reactionsLeft = Math.Max(0, options.MaxReactions);
        var turnIndex = 0;

        while (queue.Count > 0)
        {
            var (participant, reason, prompt) = queue.Dequeue();
            if (!spoken.Add(participant.NpcId) && reason != "reaction")
            {
                continue;
            }

            await writer.WriteAsync(new SceneEvent.Thinking(participant.NpcId), cancellationToken).ConfigureAwait(false);

            var question = prompt == null
                ? playerLine
                : $"{NameOf(participants, prompt.Speaker)} just said to the group: \"{prompt.Text}\"";
            var askOptions = options.AskOptions with
            {
                SystemOverride = Join(options.AskOptions.SystemOverride, BuildSceneInstructions(participant, participants, transcript))
            };

            var reply = await participant.Agent.AskAsync(question, askOptions, cancellationToken).ConfigureAwait(false);
            var text = CleanSpokenText(reply.Text, participant, participants);
            reply = reply with { Text = text };

            var index = turnIndex++;
            await writer.WriteAsync(new SceneEvent.Turn(index, participant.NpcId, reason, reply), cancellationToken).ConfigureAwait(false);
            transcript.Add(new SceneLine(participant.NpcId, text));

            // Every turn gets an Audio event when speech was requested, even if it can't be
            // voiced, so clients sequencing playback by index never wait on a missing clip.
            if (options.SynthesizeSpeech)
            {
                if (participant.Agent.HasTextToSpeech && text.Length > 0)
                {
                    synthesisTasks.Add(SynthesizeAsync(writer, index, participant, text, cancellationToken));
                }
                else
                {
                    var reasonText = text.Length == 0 ? "Empty reply." : "No text-to-speech voice configured for this NPC.";
                    await writer.WriteAsync(new SceneEvent.Audio(index, participant.NpcId, null, reasonText), cancellationToken).ConfigureAwait(false);
                }
            }

            // NPC-to-NPC: whoever this NPC just addressed by name answers next.
            if (reactionsLeft > 0)
            {
                foreach (var other in FindAddressed(text, participants, exclude: participant.NpcId))
                {
                    if (reactionsLeft == 0)
                    {
                        break;
                    }

                    if (queue.Any(q => q.Participant.NpcId == other.NpcId))
                    {
                        continue;
                    }

                    queue.Enqueue((other, "reaction", new SceneLine(participant.NpcId, text)));
                    reactionsLeft--;
                }
            }
        }

        await Task.WhenAll(synthesisTasks).ConfigureAwait(false);
    }

    private static async Task SynthesizeAsync(ChannelWriter<SceneEvent> writer, int index, SceneParticipant participant, string text, CancellationToken cancellationToken)
    {
        try
        {
            var audio = await participant.Agent.SynthesizeAsync(text, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(new SceneEvent.Audio(index, participant.NpcId, audio), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A voice failure shouldn't sink the whole scene; the text is already out.
            await writer.WriteAsync(new SceneEvent.Audio(index, participant.NpcId, null, ex.Message), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Decides who answers the player. Explicit address wins (a name, or "everyone");
    /// otherwise an LLM router reads the line and the recent conversation; if that fails,
    /// the NPC who spoke last keeps the floor.
    /// </summary>
    internal async Task<(IReadOnlyList<SceneParticipant> Responders, string Method)> PickRespondersAsync(
        IReadOnlyList<SceneParticipant> participants,
        string playerLine,
        IReadOnlyList<SceneLine> history,
        SceneOptions options,
        CancellationToken cancellationToken)
    {
        var maxResponders = Math.Clamp(options.MaxResponders, 1, participants.Count);

        if (participants.Count == 1)
        {
            return (participants, "only-participant");
        }

        // Only direct address counts here ("Mira, ..." / "hey Bram" / "..., Oswin?"). A name
        // merely mentioned ("does Bram owe you?") is left to the router, and that NPC
        // usually gets pulled in anyway as a reaction once the addressee mentions them.
        var addressed = FindVocatives(playerLine, participants);
        if (addressed.Count > 0)
        {
            return (addressed.Take(maxResponders).ToList(), "addressed-by-name");
        }

        if (GroupAddress.IsMatch(playerLine))
        {
            return (participants.Take(maxResponders).ToList(), "addressed-group");
        }

        if (options.UseLlmRouter)
        {
            try
            {
                var routed = await RouteWithLlmAsync(participants, playerLine, history, maxResponders, cancellationToken).ConfigureAwait(false);
                if (routed.Count > 0)
                {
                    return (routed, "llm-router");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fall through to the heuristic below.
            }
        }

        var lastNpc = history.LastOrDefault(line => !line.IsPlayer);
        var fallback = lastNpc != null
            ? participants.FirstOrDefault(p => string.Equals(p.NpcId, lastNpc.Speaker, StringComparison.OrdinalIgnoreCase))
            : null;
        return (new[] { fallback ?? participants[0] }, "last-speaker");
    }

    private static async Task<IReadOnlyList<SceneParticipant>> RouteWithLlmAsync(
        IReadOnlyList<SceneParticipant> participants,
        string playerLine,
        IReadOnlyList<SceneLine> history,
        int maxResponders,
        CancellationToken cancellationToken)
    {
        var system = new StringBuilder();
        system.AppendLine("You route lines of dialogue in a video game. Several characters stand together with the player.");
        system.AppendLine("Decide which character(s) should answer the player's latest line. Usually pick exactly ONE: the character the line is most clearly meant for, or whose role/knowledge fits best.");
        system.AppendLine($"Pick more than one (at most {maxResponders}) only when the line is clearly meant for several of them.");
        system.AppendLine("Characters:");
        foreach (var participant in participants)
        {
            system.AppendLine($"- id: {participant.NpcId} | name: {participant.DisplayName} | {Summarize(participant.Agent.PersonaPrompt)}");
        }

        system.AppendLine("Answer with only the chosen ids, comma separated, and nothing else.");

        var user = new StringBuilder();
        var recent = history.TakeLast(6).ToList();
        if (recent.Count > 0)
        {
            user.AppendLine("Recent conversation:");
            foreach (var line in recent)
            {
                user.AppendLine($"{NameOf(participants, line.Speaker)}: {line.Text}");
            }
        }

        user.AppendLine($"Player's latest line: \"{playerLine}\"");
        user.Append("Who answers?");

        var router = participants[0].Agent;
        var raw = await router.CompleteAsync(system.ToString(), user.ToString(), cancellationToken).ConfigureAwait(false);

        // Small local models don't reliably follow output formats, so accept an id or a
        // display name anywhere in the answer, in the order the model mentioned them.
        return participants
            .Select(p => (Participant: p, Position: FirstMention(raw, p)))
            .Where(x => x.Position >= 0)
            .OrderBy(x => x.Position)
            .Select(x => x.Participant)
            .Take(maxResponders)
            .ToList();
    }

    private static int FirstMention(string text, SceneParticipant participant)
    {
        var positions = new[] { participant.NpcId, participant.DisplayName, participant.ShortName }
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => Regex.Match(text, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase))
            .Where(match => match.Success)
            .Select(match => match.Index)
            .ToList();
        return positions.Count > 0 ? positions.Min() : -1;
    }

    private static readonly Regex Greeting = new(@"\b(hey|hi|hello|oi|yo|ahoy|greetings|good (morning|evening|day))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VocativeFiller = new(
        @"\b(hey|hi|hello|oi|yo|ahoy|greetings|good|morning|evening|day|ok|okay|so|well|and|now|please|excuse me|sorry|dear|sir|master|mister|madam|old|friend|my)\b|&",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Participants the player is talking TO, as opposed to talking ABOUT: a name that
    /// makes up the whole opening or closing phrase of a sentence ("Mira, ...", "Bram and
    /// Oswin: ...", "..., Oswin?"), or that directly follows a greeting ("hey Bram").
    /// </summary>
    internal static IReadOnlyList<SceneParticipant> FindVocatives(string line, IReadOnlyList<SceneParticipant> participants)
    {
        var result = new List<SceneParticipant>();
        void AddRange(IEnumerable<SceneParticipant> found)
        {
            foreach (var participant in found)
            {
                if (!result.Contains(participant))
                {
                    result.Add(participant);
                }
            }
        }

        foreach (var sentence in Regex.Split(line, @"(?<=[.!?])\s+"))
        {
            var phrases = sentence.Split(new[] { ',', ':', ';', '!', '?', '.', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (phrases.Length == 0)
            {
                continue;
            }

            var edges = phrases.Length > 1 ? new[] { phrases[0], phrases[^1] } : new[] { phrases[0] };
            foreach (var phrase in edges)
            {
                var named = FindAddressed(phrase, participants, exclude: null).ToList();
                if (named.Count == 0)
                {
                    continue;
                }

                var leftover = phrase;
                foreach (var token in participants.SelectMany(p => new[] { p.DisplayName, p.ShortName, p.NpcId }).OrderByDescending(t => t.Length))
                {
                    leftover = Regex.Replace(leftover, $@"\b{Regex.Escape(token)}\b", " ", RegexOptions.IgnoreCase);
                }

                leftover = VocativeFiller.Replace(leftover, " ");
                if (leftover.All(c => !char.IsLetterOrDigit(c)))
                {
                    AddRange(named);
                }
            }

            // "...busy today and Mira, did he pay?" -- speech transcripts often drop the
            // comma before a mid-sentence vocative but keep the one after it.
            foreach (var participant in participants)
            {
                var pattern = string.Join("|", new[] { participant.DisplayName, participant.ShortName }.Distinct().Select(Regex.Escape));
                if (Regex.IsMatch(sentence, $@"\b(and|so|now|also|oh|ok|okay)\s+({pattern})\s*,", RegexOptions.IgnoreCase))
                {
                    AddRange(new[] { participant });
                }
            }

            foreach (Match greeting in Greeting.Matches(sentence))
            {
                var after = sentence[(greeting.Index + greeting.Length)..].TrimStart(' ', ',');
                AddRange(participants.Where(p => FirstMention(after, p) == 0));
            }
        }

        return result;
    }

    internal static IEnumerable<SceneParticipant> FindAddressed(string text, IReadOnlyList<SceneParticipant> participants, string? exclude)
    {
        return participants
            .Where(p => !string.Equals(p.NpcId, exclude, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Participant: p, Position: FirstMention(text, p)))
            .Where(x => x.Position >= 0)
            .OrderBy(x => x.Position)
            .Select(x => x.Participant);
    }

    private static string BuildSceneInstructions(SceneParticipant self, IReadOnlyList<SceneParticipant> participants, IReadOnlyList<SceneLine> transcript)
    {
        var others = participants.Where(p => p.NpcId != self.NpcId).Select(p => p.DisplayName).ToList();
        var builder = new StringBuilder();
        builder.AppendLine(others.Count > 0
            ? $"SCENE: You are {self.DisplayName}, standing together with the player and {string.Join(", ", others)}. This is a live group conversation."
            : $"SCENE: You are {self.DisplayName}, talking face to face with the player.");
        builder.AppendLine("Conversation so far:");
        foreach (var line in transcript.TakeLast(10))
        {
            builder.AppendLine($"{NameOf(participants, line.Speaker)}: {line.Text}");
        }

        builder.AppendLine($"Reply ONLY as {self.DisplayName}, in one to three short sentences meant to be spoken aloud.");
        builder.AppendLine("Do not write lines for anyone else, do not prefix your reply with your name, and do not describe actions in asterisks.");
        if (others.Count > 0)
        {
            builder.AppendLine("You may react to what the others said, agree or tease them, or turn to one of them by name if they would know better.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Strips the habits small models fall into in group scenes: prefixing the reply with
    /// their own name, *stage directions*, and continuing on to write the next character's
    /// line ("Bram: ...") themselves.
    /// </summary>
    internal static string CleanSpokenText(string text, SceneParticipant self, IReadOnlyList<SceneParticipant> participants)
    {
        var cleaned = text.Trim().Trim('"').Trim();

        foreach (var name in new[] { self.DisplayName, self.ShortName, self.NpcId })
        {
            var prefix = Regex.Match(cleaned, $@"^\**{Regex.Escape(name)}\**\s*:\s*", RegexOptions.IgnoreCase);
            if (prefix.Success)
            {
                cleaned = cleaned[prefix.Length..];
                break;
            }
        }

        var speakerNames = participants
            .SelectMany(p => new[] { p.DisplayName, p.ShortName })
            .Append("Player")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(Regex.Escape);
        var nextSpeaker = Regex.Match(cleaned, $@"(^|\n)\s*\**({string.Join("|", speakerNames)})\**\s*:", RegexOptions.IgnoreCase);
        if (nextSpeaker.Success && nextSpeaker.Index > 0)
        {
            cleaned = cleaned[..nextSpeaker.Index];
        }

        // Stage directions: *smiles*, (laughs), [sighs] -- TTS would read them aloud.
        cleaned = Regex.Replace(cleaned, @"\*[^*]{1,80}\*|\([^)]{1,60}\)|\[[^\]]{1,60}\]", " ");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim().Trim('"').Trim();

        // Small models ignore "keep it short"; in a spoken group scene a monologue stalls
        // everyone else, so hold each line to its first few sentences.
        var sentences = Regex.Split(cleaned, @"(?<=[.!?])\s+(?=[A-Z""'])");
        if (sentences.Length > MaxSpokenSentences)
        {
            cleaned = string.Join(" ", sentences.Take(MaxSpokenSentences));
        }

        return cleaned;
    }

    private const int MaxSpokenSentences = 3;

    private static string Summarize(string personaPrompt)
    {
        var flat = Regex.Replace(personaPrompt ?? string.Empty, @"\s+", " ").Trim();
        var sentences = Regex.Split(flat, @"(?<=[.!?])\s+").Take(2);
        var summary = string.Join(" ", sentences);
        return summary.Length > 220 ? summary[..220] : summary;
    }

    private static string NameOf(IReadOnlyList<SceneParticipant> participants, string speaker)
    {
        if (string.Equals(speaker, SceneLine.PlayerSpeaker, StringComparison.OrdinalIgnoreCase))
        {
            return "Player";
        }

        return participants.FirstOrDefault(p => string.Equals(p.NpcId, speaker, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? speaker;
    }

    private static string? Join(string? first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first + "\n" + second;
}

public sealed record SceneParticipant(string NpcId, string DisplayName, NpcAgent Agent)
{
    /// <summary>First word of the display name ("Mira" for "Mira, Tavern Keeper"), how people actually address each other.</summary>
    public string ShortName { get; } = DisplayName.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? DisplayName;
}

public sealed record SceneLine(string Speaker, string Text)
{
    public const string PlayerSpeaker = "player";

    public bool IsPlayer => string.Equals(Speaker, PlayerSpeaker, StringComparison.OrdinalIgnoreCase);
}

public sealed record SceneOptions
{
    public AskOptions AskOptions { get; init; } = new();

    /// <summary>Most NPCs that answer the player directly in one beat.</summary>
    public int MaxResponders { get; init; } = 2;

    /// <summary>Most extra NPC-to-NPC replies triggered by one NPC addressing another by name.</summary>
    public int MaxReactions { get; init; } = 1;

    public bool UseLlmRouter { get; init; } = true;

    public bool SynthesizeSpeech { get; init; }
}

public abstract record SceneEvent
{
    public sealed record Routing(string[] Responders, string Method) : SceneEvent;

    public sealed record Thinking(string Npc) : SceneEvent;

    public sealed record Turn(int Index, string Npc, string Reason, AgentReply Reply) : SceneEvent;

    public sealed record Audio(int Index, string Npc, byte[]? WavBytes, string? Error = null) : SceneEvent;
}
