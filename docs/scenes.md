# Multi-character scenes (group conversations + voice)

A *scene* is one conversation between the player and several NPCs at once. The player says
something (typed or spoken), GameRagKit works out who it was meant for, those NPCs answer
in turn, each seeing what the others just said, and an NPC who gets named in another NPC's
reply can answer back. Every NPC keeps its own persona, lore, actions, mood and voice.

```
player line ──► speech-to-text (whisper.cpp, optional)
            ──► intent router: who answers?
                  1. directly addressed by name ("Mira, ..." / "hey Bram" / "..., Oswin?")
                  2. addressed as a group ("everyone", "you two", ...)
                  3. LLM router reads the line + recent conversation
                  4. fallback: whoever spoke last keeps the floor
            ──► each responder replies in sequence (sees the shared transcript)
            ──► NPC named in a reply reacts (max_reactions, default 1)
            ──► each reply synthesized in that NPC's own voice (Kokoro or Piper), in the background
```

## Try it locally (everything offline)

```bash
scripts/setup-local-voice.sh     # once: whisper.cpp + base.en, Kokoro-82M (+ Piper) -> ./models
scripts/run-voice-scene.sh       # Kokoro TTS server on :8880 + Studio on :5290 (Ollama, in-memory vectors)
open http://localhost:5290/demos/tavern.html
```

Walk to the table, press **E**, then **hold V** (or hold the mic button) and talk. Press
**Enter** to type instead. Talking while the NPCs are still speaking cuts them off.

Requirements: Ollama running with `llama3.2:3b-instruct-q4_K_M` and `nomic-embed-text`
(the run script pulls them if missing), and Homebrew for `whisper-cpp`. `DB=memory` keeps
vectors in process, so no Postgres or Qdrant is needed. Lore is re-embedded at each start.

## Giving an NPC its own voice

Add `providers.voice` to the persona YAML. Two text-to-speech engines are supported:

**`openai_speech` (recommended)**: any server implementing OpenAI's `POST /v1/audio/speech`.
The bundled [`scripts/kokoro_server.py`](../scripts/kokoro_server.py) serves
[Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) locally: natural-sounding, about 4×
faster than real time on an M1 CPU, with 54 voices (`af_*`/`am_*` American, `bf_*`/`bm_*`
British; the best-rated English ones are `af_heart`, `af_bella`, `am_fenrir`, `am_michael`,
`bm_george`, `bm_fable`). The same engine also works with Kokoro-FastAPI or OpenAI's hosted
TTS (set `TTS_API_KEY`).

```yaml
providers:
  voice:
    speech_to_text:
      engine: whisper_cpp
      model_path: ggml-base.en.bin   # bare name -> looked up in GAMERAG_VOICE_DIR
    text_to_speech:
      engine: openai_speech          # alias: kokoro
      # endpoint: http://127.0.0.1:8880 # or set TTS_ENDPOINT for every NPC at once;
      #                                 # with neither, the voice stays off
      voice: bm_george
      speed: 1.05
```

**`piper`**: the Piper CLI, run per line. It's lighter, but noticeably more robotic.

```yaml
    text_to_speech:
      engine: piper
      voice_model_path: en_GB-alan-medium.onnx   # bare name -> GAMERAG_VOICE_DIR
      volume: 0.8        # headroom; raw Piper output can clip
      noise_scale: 0.5   # lower = steadier, less warbly
      noise_w: 0.6
      # length_scale: 1.1  # > 1 speaks slower
```

`GAMERAG_VOICE_DIR` takes one or more `:`-separated directories. If a Piper model file is
missing, the NPC is text-only. If a Kokoro server is down, each line reports an audio error,
and the demo page falls back to the browser's built-in speech for it. `TTS_VOICE_MODEL_PATH`
overrides every Piper NPC with one model, so leave it unset when you want distinct voices.

## Hosted deploys (Railway, Cloud Run)

The demo pages work on a deploy with no speech models at all: speech-to-text falls back to
the browser's own recognition (Chrome, Edge, Safari) and replies to the browser's built-in
voices. For natural NPC voices on a CPU-only host, run Kokoro as a second service from
[`deploy/kokoro/Dockerfile`](../deploy/kokoro/Dockerfile) (about 1 GB RAM) and set
`TTS_ENDPOINT` on the GameRagKit service to its private URL, e.g.
`http://kokoro.railway.internal:8880`. The personas in `deploy/cloudrun-config` already
name their voices. Qwen3-TTS and Chatterbox sound more expressive but need a GPU (or an
Apple Silicon Mac) to keep up in real time.

## HTTP API

Both endpoints respond with **server-sent events** so a client can show and play the first
reply while later NPCs are still thinking. Send the `X-GameRAG-Protocol: 1` header.

### `POST /scene/ask` (JSON)

```json
{
  "participants": [
    { "npc": "tavern-keeper-mira", "name": "Mira" },
    { "npc": "blacksmith-bram", "name": "Bram" },
    { "npc": "town-crier-oswin", "name": "Oswin" }
  ],
  "message": "Mira, does Bram owe you money?",
  "history": [ { "speaker": "player", "text": "Evening all." },
               { "speaker": "blacksmith-bram", "text": "Evening." } ],
  "maxResponders": 2,
  "maxReactions": 1,
  "useLlmRouter": true,
  "synthesizeReply": true,
  "options": { "worldState": { "timeOfDay": "dusk" } }
}
```

`name` is how players and other NPCs address the character; the first word of it ("Mira"
from "Mira, Tavern Keeper") also counts. `history` is the client's transcript of the scene
so far (`speaker` is `"player"` or an NPC id); the server keeps no per-scene state.
`options` takes the same shape as `/ask`'s and applies to every NPC. Up to 6 participants.

### `POST /scene/voice` (multipart)

| Field | Description |
|---|---|
| `audio` | the player's speech as a WAV file (16 kHz mono is ideal) |
| `participants` | JSON array, as above |
| `request` | optional JSON with any of the `/scene/ask` fields except `participants`/`message` |

The speech is transcribed by the first participant that has speech-to-text configured.
Returns `422` if nothing intelligible was said, `503` if no participant can transcribe.

### `GET /scene/capabilities?npc=a&npc=b`

`{ "npcs": { "a": { "speechToText": true, "textToSpeech": true }, ... } }`: lets a client
decide whether to show a mic and whether to expect server audio.

### Events

| `type` | Fields | Meaning |
|---|---|---|
| `transcript` | `text` | (voice only) what the player said |
| `routing` | `responders`, `method` | who will answer and why (`addressed-by-name`, `addressed-group`, `llm-router`, `last-speaker`, `only-participant`) |
| `thinking` | `npc` | an NPC started generating |
| `turn` | `index`, `npc`, `reason`, `text`, `actions`, `mood`, `sources`, `fromCloud` | one spoken line; `reason` is `routed` or `reaction` |
| `audio` | `index`, `npc`, `wavBase64`, `error` | that turn's speech (may arrive after later turns; play by `index`). `wavBase64` is null with an `error` when it couldn't be voiced |
| `error` | `error` | the scene failed partway |
| `done` | | end of the beat |

When `synthesizeReply` is true, every `turn` gets exactly one matching `audio` event, so
clients can queue playback by index without timeouts.

## Notes for game clients

- **Spatial audio**: play each clip from the speaking NPC's position. The web demo uses a
  WebAudio `PannerNode` with `equalpower` panning; HRTF made small-model TTS voices sound
  phasey.
- **Barge-in**: abort the in-flight request and stop playback when the player starts
  talking again.
- **Latency**: lines are generated one after another so each NPC hears the previous one.
  With a local 3B model, expect a few seconds per line; synthesis overlaps with the next
  NPC's generation. Lower `maxResponders`/`maxReactions` for snappier beats.
- Replies are trimmed for speech: speaker-name prefixes, stage directions (`*smiles*`,
  `(laughs)`), lines written for other characters, and anything past three sentences are removed.
