#!/usr/bin/env python3
"""Local Kokoro-82M text-to-speech server with an OpenAI-compatible API subset.

GameRagKit's `openai_speech` TTS engine talks to this (or to any other server that speaks
POST /v1/audio/speech, e.g. Kokoro-FastAPI or OpenAI itself). The model is loaded once at
startup, so each line only pays for synthesis, not for model loading.

    POST /v1/audio/speech  {"input": "...", "voice": "bm_george", "speed": 1.0}  -> audio/wav
    GET  /v1/audio/voices  -> {"voices": [...]}
    GET  /health           -> {"status": "ok"}

Usage: kokoro_server.py --model kokoro-v1.0.onnx --voices voices-v1.0.bin [--port 8880]
Installed and started by scripts/setup-local-voice.sh / scripts/run-voice-scene.sh.
"""

import argparse
import io
import json
import threading
import time
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
from kokoro_onnx import Kokoro

MAX_INPUT_CHARS = 2000


def to_wav(samples: np.ndarray, sample_rate: int) -> bytes:
    # Peak-normalize to -1 dBFS so voices are consistently loud without ever clipping.
    peak = float(np.max(np.abs(samples))) if samples.size else 0.0
    if peak > 0:
        samples = samples * (0.89 / peak)
    pcm = (np.clip(samples, -1.0, 1.0) * 32767).astype(np.int16)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(sample_rate)
        out.writeframes(pcm.tobytes())
    return buffer.getvalue()


def lang_for(voice: str) -> str:
    # Kokoro voice ids are prefixed by accent: a* = American, b* = British English.
    return "en-gb" if voice.startswith("b") else "en-us"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", required=True)
    parser.add_argument("--voices", required=True)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8880)
    parser.add_argument("--default-voice", default="af_heart")
    args = parser.parse_args()

    kokoro = Kokoro(args.model, args.voices)
    voices = sorted(kokoro.get_voices())
    lock = threading.Lock()  # one synthesis at a time; parallel runs just fight over the CPU

    started = time.time()
    kokoro.create("Ready.", voice=args.default_voice, speed=1.0, lang="en-us")  # warm-up
    print(f"Kokoro ready on http://{args.host}:{args.port} ({len(voices)} voices, warm-up {time.time() - started:.1f}s)", flush=True)

    class Handler(BaseHTTPRequestHandler):
        def _json(self, status: int, payload: dict) -> None:
            body = json.dumps(payload).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:
            if self.path == "/health":
                self._json(200, {"status": "ok"})
            elif self.path == "/v1/audio/voices":
                self._json(200, {"voices": voices})
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self) -> None:
            if self.path != "/v1/audio/speech":
                self._json(404, {"error": "not found"})
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                request = json.loads(self.rfile.read(length) or b"{}")
                text = str(request.get("input", "")).strip()[:MAX_INPUT_CHARS]
                voice = str(request.get("voice") or args.default_voice)
                speed = float(request.get("speed") or 1.0)
                fmt = str(request.get("response_format") or "wav")
            except (ValueError, json.JSONDecodeError) as exc:
                self._json(400, {"error": f"bad request: {exc}"})
                return

            if not text:
                self._json(400, {"error": "input is required"})
                return
            if voice not in voices:
                self._json(400, {"error": f"unknown voice '{voice}'"})
                return
            if fmt != "wav":
                self._json(400, {"error": "only response_format=wav is supported"})
                return

            started_at = time.time()
            with lock:
                samples, sample_rate = kokoro.create(text, voice=voice, speed=max(0.5, min(2.0, speed)), lang=lang_for(voice))
            body = to_wav(samples, sample_rate)
            self.log_message("%s %d chars -> %.1fs audio in %.2fs", voice, len(text), len(samples) / sample_rate, time.time() - started_at)

            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    server_class = ThreadingHTTPServer
    if ":" in args.host:  # IPv6 bind (e.g. "::" for Railway's IPv6-only private network)
        import socket

        class DualStackServer(ThreadingHTTPServer):
            address_family = socket.AF_INET6

        server_class = DualStackServer
    server_class((args.host, args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
