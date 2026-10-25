// Kokoro-82M text-to-speech running in the visitor's browser (kokoro-js, ONNX in a Web
// Worker), for servers that don't voice NPCs themselves -- e.g. a free-tier host too small
// for the Kokoro server. Same model and voice ids as scripts/kokoro_server.py, so each NPC
// keeps its YAML casting (/scene/capabilities reports it). The ~90 MB quantized model is
// fetched once from Hugging Face's CDN and then served from the browser cache.
(function (global) {
  "use strict";

  const MODEL_ID = "onnx-community/Kokoro-82M-v1.0-ONNX";
  const KOKORO_JS = "https://cdn.jsdelivr.net/npm/kokoro-js@1.2.1/+esm";

  // The worker is inlined so the demo pages stay plain static files.
  const WORKER_SOURCE = `
    import { KokoroTTS } from "${KOKORO_JS}";
    let tts = null;
    self.onmessage = async (event) => {
      const msg = event.data;
      if (msg.type === "load") {
        try {
          tts = await KokoroTTS.from_pretrained("${MODEL_ID}", {
            dtype: "q8",
            device: "wasm",
            progress_callback: (p) => self.postMessage({ type: "progress", p })
          });
          self.postMessage({ type: "ready" });
        } catch (err) {
          self.postMessage({ type: "failed", error: String(err && err.message || err) });
        }
      } else if (msg.type === "speak") {
        try {
          const audio = await tts.generate(msg.text, { voice: msg.voice, speed: msg.speed || 1 });
          // Copy before transferring: the result can be a view into the WebAssembly runtime's
          // own memory, and transferring that buffer would detach it and wedge the model.
          const samples = new Float32Array(audio.audio);
          self.postMessage({ type: "audio", id: msg.id, samples, rate: audio.sampling_rate }, [samples.buffer]);
        } catch (err) {
          self.postMessage({ type: "audio", id: msg.id, error: String(err && err.message || err) });
        }
      }
    };
  `;

  let worker = null;
  let state = "idle"; // idle | loading | ready | failed
  let loadPromise = null;
  let nextId = 0;
  const pending = new Map();
  const progressListeners = new Set();

  // Desktop browsers with enough memory; phones keep the built-in voices rather than pull
  // ~90 MB over mobile data. ?kokoro=1 / ?kokoro=0 in the page URL overrides.
  function supported() {
    const override = new URLSearchParams(location.search).get("kokoro");
    if (override === "0") return false;
    if (typeof Worker === "undefined" || typeof WebAssembly === "undefined") return false;
    if (override === "1") return true;
    const touch = document.body && document.body.classList.contains("touch-mode");
    const lowMemory = navigator.deviceMemory !== undefined && navigator.deviceMemory < 4;
    return !touch && !lowMemory;
  }

  // Starts the model download (once). onProgress gets 0..1 for the model file.
  function load(onProgress) {
    if (onProgress) progressListeners.add(onProgress);
    if (loadPromise) return loadPromise;
    state = "loading";
    loadPromise = new Promise((resolve) => {
      try {
        const url = URL.createObjectURL(new Blob([WORKER_SOURCE], { type: "text/javascript" }));
        worker = new Worker(url, { type: "module" });
      } catch (err) {
        state = "failed";
        resolve(false);
        return;
      }

      worker.onmessage = (event) => {
        const msg = event.data;
        if (msg.type === "progress") {
          const p = msg.p || {};
          if (p.status === "progress" && /\.onnx$/.test(p.file || "") && p.total) {
            const fraction = Math.min(1, p.loaded / p.total);
            progressListeners.forEach((cb) => cb(fraction));
          }
        } else if (msg.type === "ready") {
          state = "ready";
          progressListeners.forEach((cb) => cb(1));
          resolve(true);
        } else if (msg.type === "failed") {
          console.warn("[kokoro-browser] model failed to load:", msg.error);
          state = "failed";
          resolve(false);
        } else if (msg.type === "audio") {
          const job = pending.get(msg.id);
          pending.delete(msg.id);
          if (!job) return;
          if (msg.error) job.reject(new Error(msg.error));
          else job.resolve({ samples: msg.samples, rate: msg.rate });
        }
      };
      worker.onerror = (event) => {
        console.warn("[kokoro-browser] worker error:", event.message);
        state = "failed";
        resolve(false);
      };
      worker.postMessage({ type: "load" });
    });
    return loadPromise;
  }

  // Resolves to { samples: Float32Array, rate }. Only call when isReady(); lines are voiced
  // in the order they're requested.
  function synthesize(text, voice, speed) {
    if (state !== "ready") return Promise.reject(new Error("Kokoro isn't loaded"));
    const id = ++nextId;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      worker.postMessage({ type: "speak", id, text, voice, speed });
    });
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.browserKokoro = {
    supported,
    load,
    synthesize,
    isReady: () => state === "ready",
    isLoading: () => state === "loading"
  };
})(window);
