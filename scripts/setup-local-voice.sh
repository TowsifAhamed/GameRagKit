#!/usr/bin/env bash
# Installs the local, free speech stack used by POST /ask/voice and POST /scene/voice:
#   - whisper.cpp (speech-to-text) via Homebrew, plus the ggml-base.en model
#   - Kokoro-82M (natural-sounding text-to-speech, 54 voices) in a private venv, served by
#     scripts/kokoro_server.py -- what the demo NPCs use
#   - Piper (lighter, more robotic text-to-speech CLI) plus four voices, for the
#     `engine: piper` option
# Everything lands in ./models (gitignored). Safe to re-run; existing files are skipped.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MODELS="$ROOT/models"
WHISPER_DIR="$MODELS/whisper"
PIPER_DIR="$MODELS/piper"
VENV="$MODELS/piper-venv"
mkdir -p "$WHISPER_DIR" "$PIPER_DIR"

fetch() { # url dest
  if [ -s "$2" ]; then echo "  have $(basename "$2")"; return; fi
  echo "  downloading $(basename "$2")"
  curl -fL --retry 3 -o "$2.part" "$1" && mv "$2.part" "$2"
}

echo "== whisper.cpp"
if ! command -v whisper-cli >/dev/null 2>&1; then
  if command -v brew >/dev/null 2>&1; then brew install whisper-cpp; else
    echo "whisper-cli not found and Homebrew unavailable -- build whisper.cpp manually." >&2; exit 1; fi
fi
fetch "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" "$WHISPER_DIR/ggml-base.en.bin"

echo "== kokoro"
KOKORO_DIR="$MODELS/kokoro"
KOKORO_VENV="$MODELS/kokoro-venv"
mkdir -p "$KOKORO_DIR"
if [ ! -x "$KOKORO_VENV/bin/python" ] || ! "$KOKORO_VENV/bin/python" -c "import kokoro_onnx" 2>/dev/null; then
  PY="$(command -v python3.12 || command -v python3.11 || command -v python3)"
  if command -v uv >/dev/null 2>&1; then
    uv venv --python "$PY" "$KOKORO_VENV"
    uv pip install --python "$KOKORO_VENV/bin/python" kokoro-onnx numpy
  else
    "$PY" -m venv "$KOKORO_VENV"
    "$KOKORO_VENV/bin/pip" install --upgrade pip kokoro-onnx numpy
  fi
fi
KOKORO_BASE="https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0"
fetch "$KOKORO_BASE/kokoro-v1.0.onnx" "$KOKORO_DIR/kokoro-v1.0.onnx"
fetch "$KOKORO_BASE/voices-v1.0.bin" "$KOKORO_DIR/voices-v1.0.bin"

echo "== piper"
if [ ! -x "$VENV/bin/piper" ]; then
  # piper-tts / onnxruntime wheels lag behind the newest CPython; pin a supported one.
  PY="$(command -v python3.12 || command -v python3.11 || command -v python3)"
  if command -v uv >/dev/null 2>&1; then
    uv venv --python "$PY" "$VENV"
    uv pip install --python "$VENV/bin/python" piper-tts
  else
    "$PY" -m venv "$VENV"
    "$VENV/bin/pip" install --upgrade pip piper-tts
  fi
fi

# Upstream packaging gotcha (see docs/voice.md): some piper builds look for espeak-ng
# data one directory too high. Link it into site-packages so either lookup succeeds.
SITE="$("$VENV/bin/python" -c 'import sysconfig; print(sysconfig.get_paths()["purelib"])')"
if [ -d "$SITE/piper/espeak-ng-data" ]; then
  for f in "$SITE/piper/espeak-ng-data/"*; do
    [ -e "$SITE/$(basename "$f")" ] || ln -s "$f" "$SITE/$(basename "$f")"
  done
fi

BASE="https://huggingface.co/rhasspy/piper-voices/resolve/main/en"
for voice in \
  "en_US/lessac/medium/en_US-lessac-medium" \
  "en_US/ryan/medium/en_US-ryan-medium" \
  "en_GB/alan/medium/en_GB-alan-medium" \
  "en_US/amy/medium/en_US-amy-medium"; do
  name="$(basename "$voice")"
  fetch "$BASE/$voice.onnx" "$PIPER_DIR/$name.onnx"
  fetch "$BASE/$voice.onnx.json" "$PIPER_DIR/$name.onnx.json"
done

echo "== smoke test"
echo "Testing one two three." | "$VENV/bin/piper" -m "$PIPER_DIR/en_US-lessac-medium.onnx" -f "$MODELS/smoke.wav" >/dev/null 2>&1
whisper-cli -m "$WHISPER_DIR/ggml-base.en.bin" -f "$MODELS/smoke.wav" -np -nt 2>/dev/null | tr -s ' \n' ' '
echo
echo "Done. Start the voice scene with:  scripts/run-voice-scene.sh"
