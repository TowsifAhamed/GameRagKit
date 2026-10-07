# Kokoro-82M text-to-speech server (scripts/kokoro_server.py) as its own service, e.g. a
# second Railway service next to the GameRagKit one. CPU-only; ~1 GB RAM.
#   Railway: new service from this repo, Dockerfile path deploy/kokoro/Dockerfile, then on
#   the GameRagKit service set TTS_ENDPOINT=http://<kokoro-service>.railway.internal:8880
# Build context is the repo root (it copies scripts/kokoro_server.py).
FROM python:3.12-slim
WORKDIR /app
RUN pip install --no-cache-dir kokoro-onnx numpy
ADD https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx /app/kokoro-v1.0.onnx
ADD https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin /app/voices-v1.0.bin
COPY scripts/kokoro_server.py /app/kokoro_server.py
ENV PORT=8880
# "::" listens on IPv6 too -- Railway's private network is IPv6-only.
CMD exec python kokoro_server.py --model kokoro-v1.0.onnx --voices voices-v1.0.bin --host :: --port $PORT
