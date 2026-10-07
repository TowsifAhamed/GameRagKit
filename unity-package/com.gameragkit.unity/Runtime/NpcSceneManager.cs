using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GameRagKit.Unity
{
    /// <summary>One NPC taking part in a scene.</summary>
    [Serializable]
    public class SceneParticipant
    {
        [Tooltip("NPC id on the GameRagKit server (persona id or YAML file name).")]
        public string npcId;

        [Tooltip("How players and other NPCs address this character, e.g. \"Mira\".")]
        public string displayName;

        [Tooltip("Plays this NPC's voice. Put it on the NPC (spatial blend 1) so speech comes from where it stands.")]
        public AudioSource voice;

        [Tooltip("Optional: turned to face whoever it's talking to or listening to.")]
        public Transform root;

        [Tooltip("Optional: gets a bool parameter (Talking) while speaking and a trigger named after each gesture, when the controller has them.")]
        public Animator animator;

        [Tooltip("Optional: nods and tilts with the loudness of the NPC's speech, on top of the animation.")]
        public Transform headBone;

        [Tooltip("Optional gesture for exclamations (e.g. a town crier raising an arm).")]
        public string signatureGesture;
    }

    /// <summary>
    /// Voice conversations with one or more GameRagKit NPCs (the server's /scene API): the
    /// player types or speaks, the server works out which NPC(s) should answer, and each
    /// reply plays in that NPC's own voice from its AudioSource -- NPCs can answer each other
    /// too. The player can talk over an NPC (<see cref="Interrupt"/>, or just start talking):
    /// only the words actually heard are remembered, and the NPC keeps what it didn't get to
    /// say and can bring it up later ("go on" hands it the floor back).
    ///
    /// Needs a GameRagKit server with speech-to-text (whisper.cpp) for voice input, and with
    /// NPC voices (Kokoro or Piper) for spoken replies; otherwise lines arrive as text only
    /// (see docs/scenes.md). Works with a single NPC too.
    /// </summary>
    public class NpcSceneManager : MonoBehaviour
    {
        [Header("Server")]
        public string serverUrl = "http://localhost:5280";
        public string apiKey = "";

        [Header("Scene")]
        public List<SceneParticipant> participants = new();

        [Tooltip("Ask the server for spoken replies (needs NPC voices configured on the server).")]
        public bool synthesizeVoices = true;

        [Range(1, 6)] public int maxResponders = 2;
        [Range(0, 3)] public int maxReactions = 1;

        [Header("Body language")]
        [Tooltip("The player (usually the camera); NPCs turn toward it when addressed.")]
        public Transform player;
        public bool turnToFace = true;
        public float turnSpeed = 5f;
        public string talkingBoolParameter = "Talking";
        [Range(0f, 2f)] public float headNodStrength = 1f;

        [Header("Microphone")]
        [Tooltip("Longest single recording, in seconds.")]
        public int maxRecordingSeconds = 20;

        [Header("Debug")]
        public bool logEvents = false;

        /// <summary>What the server heard the player say (voice input only).</summary>
        public event Action<string> TranscriptReceived;
        /// <summary>Which NPC ids will answer, and why (addressed-by-name, llm-router, continue-interrupted, ...).</summary>
        public event Action<string[], string> Routed;
        /// <summary>An NPC started composing a reply.</summary>
        public event Action<string> NpcThinking;
        /// <summary>An NPC starts saying a line: show the subtitle, run Gesture, apply Actions.</summary>
        public event Action<NpcSceneLine> LineStarted;
        /// <summary>A line ended: (line, interrupted, said, unsaid). When interrupted, only <c>said</c> was heard.</summary>
        public event Action<NpcSceneLine, bool, string, string> LineFinished;
        public event Action<string> Error;

        /// <summary>The conversation so far, holding only what was actually heard.</summary>
        public IReadOnlyList<SceneHistoryLine> History => _history;
        public bool IsListening => _recording != null;
        public bool IsBusy => _request != null || _beat != null && !_beat.Finished;

        private const int HistoryLimit = 20;
        private const int UnsaidTurns = 2;
        private const int UnsaidMaxChars = 400;

        private readonly List<SceneHistoryLine> _history = new();
        private readonly Dictionary<string, (string Text, int TurnsLeft)> _unsaid = new();
        private readonly Dictionary<string, BodyState> _body = new();
        private readonly System.Random _random = new();
        private readonly float[] _levelBuffer = new float[256];

        private UnityWebRequest _request;
        private int _requestId;
        private Beat _beat;
        private AudioClip _recording;
        private string _microphone;

        private sealed class Beat
        {
            public readonly Dictionary<int, NpcSceneLine> Lines = new();
            public readonly Dictionary<int, AudioClip> Clips = new();
            public readonly HashSet<int> NoClip = new();
            public bool WithVoice;
            public int Next;
            public bool Done;
            public bool Cancelled;
            public Playing Current;
            public bool Finished => Cancelled || Done && Current == null && !Lines.ContainsKey(Next);
        }

        private sealed class Playing
        {
            public NpcSceneLine Line;
            public SceneParticipant Participant;
            public AudioClip Clip;
            public float StartedAt;
            public float Duration;
        }

        private sealed class BodyState
        {
            public bool Thinking;
            public float Level;
            public float Talk;
            public float Think;
            public Quaternion Offset = Quaternion.identity;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Say something (typed) to the NPCs. Talks over anyone currently speaking.</summary>
        public void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Interrupt();
            Remember("player", text.Trim(), false);
            StartScene(text.Trim(), null);
        }

        /// <summary>Start recording the player (push-to-talk down). Talks over anyone currently speaking.</summary>
        public void StartListening()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RaiseError("Microphone capture isn't available in WebGL builds; use Say() with your own speech recognition.");
#else
            if (_recording != null)
            {
                return;
            }

            if (Microphone.devices.Length == 0)
            {
                RaiseError("No microphone found.");
                return;
            }

            Interrupt();
            _microphone = Microphone.devices[0];
            Microphone.GetDeviceCaps(_microphone, out var minFreq, out var maxFreq);
            var frequency = minFreq == 0 && maxFreq == 0 ? WavUtility.SpeechSampleRate : Mathf.Clamp(WavUtility.SpeechSampleRate, minFreq, maxFreq);
            _recording = Microphone.Start(_microphone, false, maxRecordingSeconds, frequency);
#endif
        }

        /// <summary>Stop recording (push-to-talk up) and send what was said.</summary>
        public void StopListeningAndSend()
        {
            if (_recording == null)
            {
                return;
            }

            var frames = Microphone.GetPosition(_microphone);
            Microphone.End(_microphone);
            var clip = _recording;
            _recording = null;
            if (frames < clip.frequency * 0.35f)
            {
                RaiseError("Recording too short: hold the talk button while speaking.");
                return;
            }

            SendSpeech(WavUtility.EncodeSpeechWav(clip, frames));
        }

        /// <summary>Send already-recorded speech (16-bit PCM WAV, ideally 16 kHz mono -- see WavUtility).</summary>
        public void SendSpeech(byte[] wav)
        {
            if (wav == null || wav.Length <= 44)
            {
                RaiseError("No audio to send.");
                return;
            }

            Interrupt();
            StartScene(null, wav);
        }

        /// <summary>
        /// Cut the NPCs off: stops the voice mid-line, cancels replies still being generated,
        /// keeps only the heard words in <see cref="History"/>, and remembers the rest as that
        /// NPC's unsaid words.
        /// </summary>
        public void Interrupt()
        {
            if (_request != null)
            {
                _requestId++;
                _request.Abort();
                _request = null;
            }

            var beat = _beat;
            if (beat == null || beat.Cancelled)
            {
                return;
            }

            beat.Cancelled = true;
            foreach (var index in beat.Lines.Keys.Where(i => i >= beat.Next).OrderBy(i => i).ToList())
            {
                var line = beat.Lines[index];
                var playing = beat.Current != null && beat.Current.Line == line ? beat.Current : null;
                // Lines are revealed when they start. A voiced line keeps only the words heard;
                // a text-only line was shown in full; one that never started was never said.
                var (said, unsaid) = playing == null ? (string.Empty, line.Text)
                    : playing.Clip == null && !beat.WithVoice ? (line.Text, string.Empty)
                    : SceneSpeech.SplitSpoken(line.Text, SpokenChars(playing));

                if (playing != null)
                {
                    StopVoice(playing);
                    LineFinished?.Invoke(line, true, said, unsaid);
                }

                if (said.Length > 0)
                {
                    Remember(line.NpcId, said, true);
                }

                if (unsaid.Length > 0)
                {
                    AddUnsaid(line.NpcId, unsaid);
                }
            }

            beat.Current = null;
        }

        /// <summary>Forget the conversation (history and unsaid words) and stop everything.</summary>
        public void ResetConversation()
        {
            Interrupt();
            _beat = null;
            _history.Clear();
            _unsaid.Clear();
        }

        /// <summary>0..1 loudness of what this NPC is saying right now (for lip flaps, jaw bones, anim blends).</summary>
        public float GetSpeechLevel(string npcId) => _body.TryGetValue(npcId, out var state) ? state.Level * state.Talk : 0f;

        // ------------------------------------------------------------------ request

        private void StartScene(string message, byte[] wav)
        {
            if (participants.Count == 0)
            {
                RaiseError("NpcSceneManager has no participants.");
                return;
            }

            _beat = new Beat { WithVoice = synthesizeVoices };
            StartCoroutine(SceneRequest(message, wav, _beat, ++_requestId));
        }

        private IEnumerator SceneRequest(string message, byte[] wav, Beat beat, int requestId)
        {
            var participantsJson = "[" + string.Join(",", participants.Select(p =>
                $"{{\"npc\":{SceneSpeech.JsonString(p.npcId)},\"name\":{SceneSpeech.JsonString(string.IsNullOrEmpty(p.displayName) ? p.npcId : p.displayName)}}}")) + "]";
            var settingsJson = BuildSettingsJson();

            UnityWebRequest request;
            if (wav != null)
            {
                var form = new List<IMultipartFormSection>
                {
                    new MultipartFormFileSection("audio", wav, "speech.wav", "audio/wav"),
                    new MultipartFormDataSection("participants", participantsJson),
                    new MultipartFormDataSection("request", settingsJson)
                };
                request = UnityWebRequest.Post($"{serverUrl}/scene/voice", form);
            }
            else
            {
                var body = settingsJson.Insert(1, $"\"participants\":{participantsJson},\"message\":{SceneSpeech.JsonString(message)},");
                request = new UnityWebRequest($"{serverUrl}/scene/ask", "POST")
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body))
                };
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.SetRequestHeader("X-GameRAG-Protocol", "1");
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.SetRequestHeader("X-Api-Key", apiKey);
            }

            request.downloadHandler?.Dispose();
            request.downloadHandler = new SseDownloadHandler(line =>
            {
                if (requestId == _requestId && NpcResponseParser.TryParseSceneEvent(line, out var sceneEvent))
                {
                    HandleEvent(beat, sceneEvent);
                }
            });

            _request = request;
            using (request)
            {
                yield return request.SendWebRequest();

                if (requestId != _requestId)
                {
                    yield break; // interrupted
                }

                _request = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    RaiseError(request.responseCode >= 400
                        ? $"Scene request failed ({request.responseCode}) -- does the server have speech-to-text for voice input?"
                        : $"Scene request failed: {request.error}");
                }

                beat.Done = true;
                // A line whose audio never arrives still gets "spoken" (as text for its duration).
                foreach (var index in beat.Lines.Keys)
                {
                    if (!beat.Clips.ContainsKey(index))
                    {
                        beat.NoClip.Add(index);
                    }
                }
            }
        }

        private string BuildSettingsJson()
        {
            var sb = new StringBuilder("{");
            sb.Append("\"history\":[");
            sb.Append(string.Join(",", _history.Skip(Math.Max(0, _history.Count - HistoryLimit)).Select(h =>
                $"{{\"speaker\":{SceneSpeech.JsonString(h.speaker)},\"text\":{SceneSpeech.JsonString(h.text)},\"interrupted\":{(h.interrupted ? "true" : "false")}}}")));
            sb.Append("],\"unsaid\":[");
            sb.Append(string.Join(",", _unsaid.Select(u => $"{{\"speaker\":{SceneSpeech.JsonString(u.Key)},\"text\":{SceneSpeech.JsonString(u.Value.Text)}}}")));
            sb.Append($"],\"maxResponders\":{maxResponders},\"maxReactions\":{maxReactions},\"synthesizeReply\":{(synthesizeVoices ? "true" : "false")}}}");
            return sb.ToString();
        }

        private void HandleEvent(Beat beat, SceneEvent e)
        {
            if (logEvents)
            {
                Debug.Log($"[GameRagKit] scene event {e.Type} {e.Npc} {e.Text}");
            }

            switch (e.Type)
            {
                case "transcript":
                    Remember("player", e.Text, false);
                    TranscriptReceived?.Invoke(e.Text);
                    break;
                case "routing":
                    Routed?.Invoke(e.Responders, e.Method);
                    break;
                case "thinking":
                    StateFor(e.Npc).Thinking = true;
                    NpcThinking?.Invoke(e.Npc);
                    break;
                case "turn":
                    if (beat.Cancelled)
                    {
                        return;
                    }

                    StateFor(e.Npc).Thinking = false;
                    var participant = Find(e.Npc);
                    beat.Lines[e.Index] = new NpcSceneLine
                    {
                        Index = e.Index,
                        NpcId = e.Npc,
                        Text = e.Text ?? string.Empty,
                        Reason = e.Reason,
                        Mood = e.Mood,
                        Actions = e.Actions,
                        Gesture = SceneSpeech.ChooseGesture(e.Text, e.Mood, e.Actions, participant?.signatureGesture, _random),
                        Addressee = SceneSpeech.AddresseeOf(e.Text, e.Npc, participants.Select(p => (p.npcId, p.displayName)))
                    };
                    if (!synthesizeVoices)
                    {
                        beat.NoClip.Add(e.Index);
                    }

                    break;
                case "audio":
                    var clip = string.IsNullOrEmpty(e.WavBase64) ? null : WavUtility.ToAudioClip(Convert.FromBase64String(e.WavBase64), $"{e.Npc}-{e.Index}");
                    if (clip != null)
                    {
                        beat.Clips[e.Index] = clip;
                    }
                    else
                    {
                        beat.NoClip.Add(e.Index);
                    }

                    break;
                case "error":
                    RaiseError(e.Error);
                    break;
            }
        }

        // ------------------------------------------------------------------ playback

        private void Update()
        {
            var beat = _beat;
            if (beat != null && !beat.Cancelled)
            {
                if (beat.Current != null && Time.time - beat.Current.StartedAt >= beat.Current.Duration)
                {
                    FinishCurrent(beat);
                }

                if (beat.Current == null && beat.Lines.TryGetValue(beat.Next, out var next)
                    && (beat.Clips.ContainsKey(beat.Next) || beat.NoClip.Contains(beat.Next)))
                {
                    StartLine(beat, next);
                }
            }

            UpdateBody();
        }

        private void StartLine(Beat beat, NpcSceneLine line)
        {
            var participant = Find(line.NpcId);
            beat.Clips.TryGetValue(line.Index, out var clip);
            var playing = new Playing
            {
                Line = line,
                Participant = participant,
                Clip = clip,
                StartedAt = Time.time,
                // No audio (text-only, or no voice on the server): give the subtitle reading time.
                Duration = clip != null ? clip.length : Mathf.Clamp(line.Text.Length / 14f, 1.2f, 8f)
            };
            beat.Current = playing;

            if (clip != null && participant?.voice != null)
            {
                participant.voice.clip = clip;
                participant.voice.Play();
            }

            if (participant?.animator != null)
            {
                SetAnimatorBool(participant.animator, talkingBoolParameter, true);
                if (!string.IsNullOrEmpty(line.Gesture))
                {
                    SetAnimatorTrigger(participant.animator, line.Gesture);
                }
            }

            LineStarted?.Invoke(line);
        }

        private void FinishCurrent(Beat beat)
        {
            var playing = beat.Current;
            beat.Current = null;
            beat.Next++;
            if (playing.Participant?.animator != null)
            {
                SetAnimatorBool(playing.Participant.animator, talkingBoolParameter, false);
            }

            Remember(playing.Line.NpcId, playing.Line.Text, false);
            if (_unsaid.TryGetValue(playing.Line.NpcId, out var entry))
            {
                // It had its chance to bring up what it was holding back.
                if (entry.TurnsLeft <= 1) _unsaid.Remove(playing.Line.NpcId);
                else _unsaid[playing.Line.NpcId] = (entry.Text, entry.TurnsLeft - 1);
            }

            LineFinished?.Invoke(playing.Line, false, playing.Line.Text, string.Empty);
        }

        private int SpokenChars(Playing playing)
        {
            var source = playing.Participant?.voice;
            // Playback position when the clip is really playing; elapsed time otherwise (no audio
            // device, muted, or text-only lines).
            var fraction = playing.Clip != null && source != null && source.clip == playing.Clip && source.isPlaying && source.time > 0f
                ? source.time / playing.Clip.length
                : (Time.time - playing.StartedAt) / Mathf.Max(0.01f, playing.Duration);
            return Mathf.RoundToInt(playing.Line.Text.Length * Mathf.Clamp01(fraction));
        }

        private void StopVoice(Playing playing)
        {
            if (playing.Participant?.voice != null && playing.Participant.voice.clip == playing.Clip)
            {
                playing.Participant.voice.Stop();
            }

            if (playing.Participant?.animator != null)
            {
                SetAnimatorBool(playing.Participant.animator, talkingBoolParameter, false);
            }
        }

        // ------------------------------------------------------------------ body language

        private void UpdateBody()
        {
            var speaking = _beat != null && !_beat.Cancelled ? _beat.Current : null;
            var ease = 1f - Mathf.Exp(-Time.deltaTime * 4f);
            foreach (var participant in participants)
            {
                var state = StateFor(participant.npcId);
                var isSpeaker = speaking != null && speaking.Participant == participant;

                var level = 0f;
                if (isSpeaker && participant.voice != null && participant.voice.isPlaying)
                {
                    participant.voice.GetOutputData(_levelBuffer, 0);
                    var sum = 0f;
                    foreach (var v in _levelBuffer)
                    {
                        sum += v * v;
                    }

                    level = Mathf.Clamp01(Mathf.Sqrt(sum / _levelBuffer.Length) * 4f);
                }
                else if (isSpeaker)
                {
                    level = 0.4f + 0.2f * Mathf.Sin(Time.time * 5.3f) * Mathf.Sin(Time.time * 1.7f);
                }

                state.Level += (level - state.Level) * Mathf.Min(1f, Time.deltaTime * 6f);
                state.Talk += ((isSpeaker ? 1f : 0f) - state.Talk) * ease;
                state.Think += ((state.Thinking ? 1f : 0f) - state.Think) * ease;

                // Remove last frame's head offset before the Animator runs, so it never piles up
                // on a bone the controller doesn't animate (re-applied in LateUpdate).
                if (participant.headBone != null)
                {
                    participant.headBone.localRotation *= Quaternion.Inverse(state.Offset);
                    state.Offset = Quaternion.identity;
                }

                if (turnToFace && participant.root != null)
                {
                    var target = speaking == null ? player
                        : isSpeaker ? (speaking.Line.Addressee == "player" ? player : Find(speaking.Line.Addressee)?.root)
                        : speaking.Participant?.root;
                    if (target != null)
                    {
                        var direction = target.position - participant.root.position;
                        direction.y = 0;
                        if (direction.sqrMagnitude > 0.0001f)
                        {
                            participant.root.rotation = Quaternion.Slerp(participant.root.rotation, Quaternion.LookRotation(direction), Time.deltaTime * turnSpeed);
                        }
                    }
                }
            }
        }

        private void LateUpdate()
        {
            const float maxAngle = 8f; // a nod, not a headbang
            foreach (var participant in participants)
            {
                if (participant.headBone == null)
                {
                    continue;
                }

                var state = StateFor(participant.npcId);
                var t = Time.time;
                var l = state.Level * state.Talk;
                var offset = Quaternion.Euler(
                    Mathf.Clamp((l * 4f + Mathf.Sin(t * 3.1f) * 1.4f * l - state.Think * 3f) * headNodStrength, -maxAngle, maxAngle),
                    Mathf.Clamp(Mathf.Sin(t * 0.9f) * 2.3f * state.Talk * headNodStrength, -maxAngle, maxAngle),
                    Mathf.Clamp((Mathf.Sin(t * 1.3f) * 1.1f * state.Talk + state.Think * 4f) * headNodStrength, -maxAngle, maxAngle));
                participant.headBone.localRotation *= offset;
                state.Offset = offset;
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Remember(string speaker, string text, bool interrupted)
        {
            _history.Add(new SceneHistoryLine { speaker = speaker, text = text, interrupted = interrupted });
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveRange(0, _history.Count - HistoryLimit);
            }
        }

        private void AddUnsaid(string npcId, string text)
        {
            var combined = _unsaid.TryGetValue(npcId, out var existing) ? $"{existing.Text} {text}" : text;
            _unsaid[npcId] = (combined.Length > UnsaidMaxChars ? combined[^UnsaidMaxChars..] : combined, UnsaidTurns);
        }

        private SceneParticipant Find(string npcId) => participants.FirstOrDefault(p => string.Equals(p.npcId, npcId, StringComparison.OrdinalIgnoreCase));

        private BodyState StateFor(string npcId)
        {
            if (!_body.TryGetValue(npcId ?? string.Empty, out var state))
            {
                state = new BodyState();
                _body[npcId ?? string.Empty] = state;
            }

            return state;
        }

        private void RaiseError(string message)
        {
            Debug.LogWarning($"[GameRagKit] {message}");
            Error?.Invoke(message);
        }

        private static void SetAnimatorBool(Animator animator, string name, bool value)
        {
            if (!string.IsNullOrEmpty(name) && animator.parameters.Any(p => p.type == AnimatorControllerParameterType.Bool && p.name == name))
            {
                animator.SetBool(name, value);
            }
        }

        private static void SetAnimatorTrigger(Animator animator, string name)
        {
            if (animator.parameters.Any(p => p.type == AnimatorControllerParameterType.Trigger && p.name == name))
            {
                animator.SetTrigger(name);
            }
        }

        private void OnDisable()
        {
            if (_recording != null)
            {
                Microphone.End(_microphone);
                _recording = null;
            }

            Interrupt();
        }
    }
}
