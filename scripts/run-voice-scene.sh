#!/usr/bin/env bash
# Runs GameRagKit Studio fully locally -- Ollama for chat/embeddings, whisper.cpp for
# speech-to-text, a Kokoro server for per-NPC voices, and an in-memory vector store -- then open
#   http://localhost:5290/demos/tavern.html
# Run scripts/setup-local-voice.sh once first.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MODELS="$ROOT/models"
PORT="${PORT:-5290}"

if [ ! -x "$MODELS/piper-venv/bin/piper" ] || [ ! -s "$MODELS/whisper/ggml-base.en.bin" ] || [ ! -s "$MODELS/kokoro/voices-v1.0.bin" ]; then
  echo "Local voice models missing -- running scripts/setup-local-voice.sh first."
  "$ROOT/scripts/setup-local-voice.sh"
fi

OLLAMA_HOST="${OLLAMA_HOST:-http://127.0.0.1:11434}"
if ! curl -sf "$OLLAMA_HOST/api/tags" >/dev/null; then
  echo "Ollama isn't reachable at $OLLAMA_HOST -- start it with 'ollama serve'." >&2
  exit 1
fi
for model in llama3.2:3b-instruct-q4_K_M nomic-embed-text; do
  curl -sf "$OLLAMA_HOST/api/tags" | grep -q "\"$model" || ollama pull "$model"
done

# Kokoro TTS server (model stays loaded between lines). Reuse one that's already running.
KOKORO_PORT="${KOKORO_PORT:-8880}"
KOKORO_PID=""
if ! curl -sf "http://127.0.0.1:$KOKORO_PORT/health" >/dev/null; then
  "$MODELS/kokoro-venv/bin/python" "$ROOT/scripts/kokoro_server.py" \
    --model "$MODELS/kokoro/kokoro-v1.0.onnx" --voices "$MODELS/kokoro/voices-v1.0.bin" --port "$KOKORO_PORT" &
  KOKORO_PID=$!
  trap '[ -n "$KOKORO_PID" ] && kill "$KOKORO_PID" 2>/dev/null' EXIT INT TERM
  for _ in $(seq 1 60); do curl -sf "http://127.0.0.1:$KOKORO_PORT/health" >/dev/null && break; sleep 1; done
fi
# Personas name their Kokoro voice; this tells every one of them where the server is.
export TTS_ENDPOINT="http://127.0.0.1:$KOKORO_PORT"

export DB=memory
export GAMERAG_VOICE_DIR="$MODELS/whisper:$MODELS/piper"
export TTS_EXECUTABLE_PATH="$MODELS/piper-venv/bin/piper"

echo "Open http://localhost:$PORT/demos/tavern.html once the server is up."
dotnet run --project "$ROOT/src/GameRagKit.Cli" -- studio --port "$PORT" "$@"
