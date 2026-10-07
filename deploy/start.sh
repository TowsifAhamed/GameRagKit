#!/bin/sh
# Container entrypoint for the hosted demo (Dockerfile.cloudrun): starts the bundled Kokoro
# text-to-speech server on an internal port, then GameRagKit Studio. NPC voices fall back
# to the browser's own if Kokoro is disabled (KOKORO=0) or still warming up.
if [ "${KOKORO:-1}" != "0" ]; then
  /opt/kokoro/bin/python /app/kokoro/kokoro_server.py \
    --model /app/kokoro/kokoro-v1.0.onnx --voices /app/kokoro/voices-v1.0.bin \
    --host 127.0.0.1 --port 8880 &
else
  unset TTS_ENDPOINT
fi

exec dotnet GameRagKit.Cli.dll studio --config /app/config --port "${PORT:-8080}"
