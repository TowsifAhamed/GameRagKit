# Voice I/O (speech-to-text and text-to-speech)

NPCs can accept spoken player input and reply with synthesized speech via
`POST /ask/voice`, using local, free CLI tools run as subprocesses:

- **Speech-to-text**: [whisper.cpp](https://github.com/ggml-org/whisper.cpp)'s `whisper-cli` binary (e.g. `brew install whisper-cpp` on macOS).
- **Text-to-speech**: [Piper](https://github.com/OHF-Voice/piper1-gpl) (`pip install piper-tts`).

GameRagKit does not bundle either engine — install them separately, the same way Ollama
is treated as an external local service. Both are invoked as short-lived subprocesses per
request; there is no persistent voice server process to manage.

## Configuration

Add a `providers.voice` section to an NPC's YAML:

```yaml
providers:
  voice:
    speech_to_text:
      engine: whisper_cpp
      model_path: /path/to/ggml-tiny.en.bin
      executable_path: whisper-cli   # optional, defaults to "whisper-cli" resolved via PATH
      language: en
      timeout_seconds: 60
    text_to_speech:
      engine: piper
      voice_model_path: /path/to/en_US-amy-low.onnx
      executable_path: piper        # optional, defaults to "piper" resolved via PATH
      timeout_seconds: 60
```

Both sub-sections are optional and independent — an NPC can have speech-to-text without
text-to-speech, or vice versa. `POST /ask/voice` requires speech-to-text always, and
requires text-to-speech only if `synthesizeReply` is requested.

Model paths can also be set via environment variables, which take priority over YAML:
`STT_MODEL_PATH`, `STT_EXECUTABLE_PATH`, `TTS_VOICE_MODEL_PATH`, `TTS_EXECUTABLE_PATH`.

## Getting the models

- **whisper.cpp models**: download a `.bin` file from
  [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp/tree/main).
  `ggml-tiny.en.bin` (~75MB) is a good default for English-only dialogue — fast, and
  accurate enough for short player utterances.
- **Piper voices**: download a `.onnx` model and matching `.onnx.json` config from
  [huggingface.co/rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices/tree/main).
  Both files must sit next to each other; `voice_model_path` points at the `.onnx` file.

## API

`POST /ask/voice` is a `multipart/form-data` request (not JSON, since it carries a binary
audio file):

| Field | Type | Required | Description |
|---|---|---|---|
| `npc` | string | yes | NPC identifier |
| `audio` | file | yes | Player's spoken question, as a WAV file |
| `options` | string (JSON) | no | Same shape as `/ask`'s `options` object |
| `synthesizeReply` | bool | no | Whether to synthesize the NPC's reply to speech (default depends on client; explicit `true`/`false` recommended) |

```bash
curl -X POST http://localhost:5280/ask/voice \
  -H "X-GameRAG-Protocol: 1" \
  -F "npc=guard-north-gate" \
  -F "audio=@question.wav" \
  -F "synthesizeReply=true"
```

Response:

```json
{
  "transcript": "What is your duty?",
  "answer": "My duty is to guard the North Gate...",
  "sources": ["faction:royal_guard.md#1"],
  "scores": [0.87],
  "fromCloud": false,
  "actions": [],
  "replyAudioWavBase64": "UklGRi..."
}
```

`replyAudioWavBase64` is `null` when `synthesizeReply` was `false` or omitted. When
present, it's a base64-encoded 16-bit PCM WAV file — decode and play it directly.

## Audio format

Input audio should be a WAV file; whisper.cpp accepts `flac`, `mp3`, `ogg`, and `wav`, but
`wav` is the safest default across game engine audio-recording APIs. Piper's synthesized
output is always 16-bit PCM mono WAV at the voice model's native sample rate (commonly
16kHz or 22050Hz depending on the voice).

## Engine client support

Neither the Unity package (`unity-package/com.gameragkit.unity`) nor the Unreal plugin
(`unreal-plugin/GameRagKit`) has `/ask/voice` client support yet — this phase only covers
the server side. Recording player audio and calling this endpoint from a game client is
left as follow-up work.

## Known limitations

- **Non-streaming only.** `/ask/voice` waits for the full transcription and (optionally)
  full synthesis before responding — there is no incremental/streaming voice variant yet,
  unlike `/ask/stream` for text. Real-time voice chat would need a different protocol
  (chunked audio in and out); this was intentionally out of scope for the current
  subprocess-based design.
- **Subprocess overhead.** Each request spawns a new `whisper-cli`/`piper` process. Both
  tools have model-load startup cost (whisper.cpp additionally has a one-time GPU shader
  compile cost on first invocation on Metal-capable machines, which does not recur on
  subsequent calls within the same process lifetime — but since GameRagKit spawns a fresh
  process per request, that cost is not amortized across requests today). For
  latency-sensitive use, keeping a request's audio short (a few seconds of speech) keeps
  turnaround reasonable.
- **Piper packaging gotcha.** Some Piper distributions resolve their bundled espeak-ng
  phoneme data files relative to an incorrect base path. If `POST /ask/voice` with
  `synthesizeReply=true` fails with a "No such file or directory" error mentioning
  `phontab`/`phonindex`/similar, symlink the contents of the installed
  `piper/espeak-ng-data/` directory into its parent `site-packages/` directory — this is
  an upstream Piper packaging issue, not a GameRagKit bug.
