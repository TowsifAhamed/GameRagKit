// Shared voice plumbing for the demo scenes: push-to-talk, talking to /scene/*, playing
// NPC speech (positional, with a level meter), and the body language that goes with it.
// Used by scene-controller.js (group conversations) and dialogue-controller.js (one NPC).
//
// Speech-to-text: the server's whisper.cpp when it has one, otherwise the browser's own
// SpeechRecognition (Chrome/Edge/Safari). Text-to-speech: the server's voices (Kokoro or
// Piper) when it has them, otherwise the browser's speechSynthesis. So voice works on a
// plain hosted deploy with no speech models at all, and sounds best locally.
(function (global) {
  "use strict";

  const PROTOCOL_HEADER = { "X-GameRAG-Protocol": "1" };
  const MIN_RECORDING_MS = 350;
  const BrowserRecognition = global.SpeechRecognition || global.webkitSpeechRecognition;

  function createVoice(game, options) {
    const serverUrl = (options && options.serverUrl) || "";
    let audioCtx = null;
    const activeSources = new Set();

    function ensureAudio() {
      if (!audioCtx) {
        const Ctx = global.AudioContext || global.webkitAudioContext;
        audioCtx = new Ctx();
      }
      if (audioCtx.state === "suspended") audioCtx.resume();
      return audioCtx;
    }

    // ---------- What this server (and browser) can do ----------
    async function capabilities(npcIds) {
      const canRecord = !!(navigator.mediaDevices && global.MediaRecorder);
      let entries = [];
      try {
        const query = npcIds.map((id) => `npc=${encodeURIComponent(id)}`).join("&");
        const res = await fetch(`${serverUrl}/scene/capabilities?${query}`, { headers: PROTOCOL_HEADER });
        if (res.ok) entries = Object.entries((await res.json()).npcs || {});
      } catch (_) {
        // Older server without /scene: treat as no server speech.
      }
      const serverStt = canRecord && entries.some(([, c]) => c.speechToText);
      return {
        serverStt,
        browserStt: !serverStt && !!BrowserRecognition,
        canListen: serverStt || !!BrowserRecognition,
        serverTts: new Set(entries.filter(([, c]) => c.textToSpeech).map(([id]) => id)),
        browserTts: !!global.speechSynthesis
      };
    }

    // ---------- Push-to-talk ----------
    // Hold the button (or call start/stop from a key handler). In server mode the
    // recording is converted to 16 kHz WAV and handed to onAudio; in browser mode the
    // browser transcribes and onText gets the final words.
    function createPushToTalk(config) {
      const { button, getMode, onStatus, onAudio, onText, onError, onStart } = config;
      let recorder = null;
      let recognition = null;
      let startedAt = 0;
      let browserText = "";

      async function start() {
        if (recorder || recognition) return;
        if (onStart) onStart();
        ensureAudio();
        startedAt = performance.now();
        if (getMode() === "browser") {
          browserText = "";
          recognition = new BrowserRecognition();
          recognition.lang = "en-US";
          recognition.interimResults = true;
          recognition.continuous = true;
          recognition.onresult = (event) => {
            browserText = Array.from(event.results).map((r) => r[0].transcript).join(" ").trim();
            onStatus(browserText ? `“${browserText}”` : "Listening… release to send");
          };
          recognition.onerror = (event) => {
            if (event.error !== "aborted" && event.error !== "no-speech") onError(`Speech recognition: ${event.error}`);
          };
          recognition.onend = () => {
            recognition = null;
            button.classList.remove("recording");
            if (browserText) onText(browserText);
            else onStatus("Didn't catch that. Hold the mic while you talk.");
          };
          recognition.start();
        } else {
          let stream;
          try {
            stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
          } catch (err) {
            onError(`Microphone unavailable: ${err.message}`);
            return;
          }
          const chunks = [];
          recorder = new MediaRecorder(stream);
          recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
          recorder.onstop = async () => {
            const mime = recorder.mimeType;
            recorder = null;
            stream.getTracks().forEach((t) => t.stop());
            button.classList.remove("recording");
            if (performance.now() - startedAt < MIN_RECORDING_MS) {
              onStatus("Hold the mic (or V) while you talk.");
              return;
            }
            onStatus("Transcribing…");
            try {
              onAudio(await toWav16k(new Blob(chunks, { type: mime })));
            } catch (err) {
              onError(`Couldn't process the recording: ${err.message}`);
            }
          };
          recorder.start();
        }
        button.classList.add("recording");
        onStatus("Listening… release to send");
      }

      function stop() {
        if (recorder && recorder.state === "recording") recorder.stop();
        if (recognition) recognition.stop();
      }

      button.addEventListener("pointerdown", (event) => {
        event.preventDefault();
        button.setPointerCapture(event.pointerId);
        start();
      });
      button.addEventListener("pointerup", stop);
      button.addEventListener("pointercancel", stop);

      return { start, stop, isActive: () => !!(recorder || recognition) };
    }

    // whisper.cpp wants 16 kHz mono WAV; browsers record webm/opus or mp4/aac.
    async function toWav16k(blob) {
      const ctx = ensureAudio();
      const decoded = await ctx.decodeAudioData(await blob.arrayBuffer());
      const rate = 16000;
      const offline = new OfflineAudioContext(1, Math.ceil(decoded.duration * rate), rate);
      const src = offline.createBufferSource();
      src.buffer = decoded;
      src.connect(offline.destination);
      src.start();
      const samples = (await offline.startRendering()).getChannelData(0);

      const out = new DataView(new ArrayBuffer(44 + samples.length * 2));
      const writeStr = (offset, s) => { for (let i = 0; i < s.length; i++) out.setUint8(offset + i, s.charCodeAt(i)); };
      writeStr(0, "RIFF");
      out.setUint32(4, 36 + samples.length * 2, true);
      writeStr(8, "WAVE");
      writeStr(12, "fmt ");
      out.setUint32(16, 16, true);
      out.setUint16(20, 1, true); // PCM
      out.setUint16(22, 1, true); // mono
      out.setUint32(24, rate, true);
      out.setUint32(28, rate * 2, true);
      out.setUint16(32, 2, true);
      out.setUint16(34, 16, true);
      writeStr(36, "data");
      out.setUint32(40, samples.length * 2, true);
      for (let i = 0; i < samples.length; i++) {
        const s = Math.max(-1, Math.min(1, samples[i]));
        out.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
      }
      return new Blob([out.buffer], { type: "audio/wav" });
    }

    // ---------- Server conversation (SSE over fetch) ----------
    async function streamScene({ participants, message, audio, settings, signal, onEvent }) {
      let response;
      if (audio) {
        const body = new FormData();
        body.append("audio", audio, "speech.wav");
        body.append("participants", JSON.stringify(participants));
        body.append("request", JSON.stringify(settings));
        response = await fetch(`${serverUrl}/scene/voice`, { method: "POST", headers: PROTOCOL_HEADER, body, signal });
      } else {
        response = await fetch(`${serverUrl}/scene/ask`, {
          method: "POST",
          headers: Object.assign({ "Content-Type": "application/json" }, PROTOCOL_HEADER),
          body: JSON.stringify(Object.assign({ participants, message }, settings)),
          signal
        });
      }

      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.error || `Request failed (${response.status})`);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let split;
        while ((split = buffer.indexOf("\n\n")) >= 0) {
          const chunk = buffer.slice(0, split);
          buffer = buffer.slice(split + 2);
          for (const line of chunk.split("\n")) {
            if (line.startsWith("data: ")) {
              try { onEvent(JSON.parse(line.slice(6))); } catch (err) { console.warn("[voice] bad event", err); }
            }
          }
        }
      }
    }

    // ---------- Playback ----------
    // The listener follows the camera, written only when it moves: re-setting AudioParams
    // every frame causes audible zipper/bubbling artifacts.
    const forward = new THREE.Vector3();
    let lastListenerKey = "";
    function updateListener() {
      if (!audioCtx) return;
      const listener = audioCtx.listener;
      const p = game.camera.position;
      game.camera.getWorldDirection(forward);
      const key = [p.x, p.z, forward.x, forward.z].map((n) => n.toFixed(2)).join();
      if (key === lastListenerKey) return;
      lastListenerKey = key;
      if (listener.positionX) {
        listener.positionX.value = p.x;
        listener.positionY.value = p.y;
        listener.positionZ.value = p.z;
        listener.forwardX.value = forward.x;
        listener.forwardY.value = forward.y;
        listener.forwardZ.value = forward.z;
        listener.upX.value = 0;
        listener.upY.value = 1;
        listener.upZ.value = 0;
      } else {
        listener.setPosition(p.x, p.y, p.z);
        listener.setOrientation(forward.x, forward.y, forward.z, 0, 1, 0);
      }
    }

    // Plays a base64 WAV, positioned at `position` ({x, z}) if given. Resolves when done
    // or stopped. `npc` (an addNpc actor) gets talking motion driven by the audio level.
    async function playWav(base64, { position, npc } = {}) {
      const ctx = ensureAudio();
      const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
      const buffer = await ctx.decodeAudioData(bytes.buffer);
      const source = ctx.createBufferSource();
      source.buffer = buffer;

      let output = ctx.destination;
      if (position) {
        // equalpower = plain stereo placement + distance falloff. HRTF sounds more "3D" on
        // paper, but its filtering makes TTS voices sound phasey/bubbly.
        const panner = ctx.createPanner();
        panner.panningModel = "equalpower";
        panner.distanceModel = "inverse";
        panner.refDistance = 3;
        panner.rolloffFactor = 0.6;
        if (panner.positionX) {
          panner.positionX.value = position.x;
          panner.positionY.value = 1.6;
          panner.positionZ.value = position.z;
        } else {
          panner.setPosition(position.x, 1.6, position.z);
        }
        panner.connect(ctx.destination);
        output = panner;
      }
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      source.connect(analyser);
      source.connect(output);

      activeSources.add(source);
      if (npc) setSpeaking(npc, analyser);
      try {
        await new Promise((resolve) => {
          source.onended = resolve;
          source.start();
        });
      } finally {
        activeSources.delete(source);
        if (npc) setSpeaking(npc, null);
      }
    }

    // macOS ships joke voices (a bleating sheep, bubbles, an organ...) alongside the real
    // ones; never hand an NPC one of those.
    const NOVELTY_VOICES = /^(albert|bad news|bahh|bells|boing|bubbles|cellos|deranged|good news|hysterical|jester|organ|pipe organ|superstar|trinoids|whisper|wobble|zarvox|junior|ralph|fred|kathy|princess|grandma|grandpa|eddy|flo|reed|rocko|sandy|shelley)\b/i;

    // Natural/neural voices first (Edge "Online (Natural)", Chrome "Google", macOS
    // "Enhanced"/"Premium"), so the fallback sounds as human as the browser allows.
    function rankedBrowserVoices() {
      const score = (v) => (/natural|neural|online|premium|enhanced/i.test(v.name) ? 4 : 0) + (/google/i.test(v.name) ? 2 : 0) + (/^en-(us|gb)/i.test(v.lang) ? 1 : 0);
      return speechSynthesis.getVoices()
        .filter((v) => /^en/i.test(v.lang) && !NOVELTY_VOICES.test(v.name))
        .sort((a, b) => score(b) - score(a));
    }

    // Chrome fills getVoices() asynchronously; until it does, a line would get the
    // browser's default voice instead of this NPC's.
    const voicesReady = !global.speechSynthesis
      ? Promise.resolve()
      : new Promise((resolve) => {
          if (speechSynthesis.getVoices().length) return resolve();
          speechSynthesis.addEventListener("voiceschanged", () => resolve(), { once: true });
          setTimeout(resolve, 1500);
        });

    // Best-effort gender of a system voice from its name (Chrome "Google UK English Male",
    // Edge "Microsoft Guy Online", macOS "Daniel"/"Samantha"...), for npc.voiceGender.
    const MALE_VOICE = /\bmale\b|daniel|alex|rishi|aaron|arthur|gordon|oliver|thomas|\bguy\b|ryan|eric|christopher|roger|andrew|brian|davis|george|lee\b/i;
    const FEMALE_VOICE = /female|samantha|karen|moira|tessa|victoria|serena|aria|jenny|\bava\b|allison|susan|zira|emma|sonia|libby|michelle|fiona|google us english/i;

    // Browser fallback voice; voiceIndex picks a different system voice and pitch per NPC,
    // npc.voiceGender ("male"/"female") narrows it to matching voices when there are any.
    async function speakBrowser(text, voiceIndex, npc) {
      await voicesReady;
      return new Promise((resolve) => {
        if (!global.speechSynthesis || !text) return resolve();
        const utterance = new SpeechSynthesisUtterance(text);
        let voices = rankedBrowserVoices();
        const gender = npc && npc.voiceGender;
        if (gender) {
          const pattern = gender === "male" ? MALE_VOICE : FEMALE_VOICE;
          const matching = voices.filter((v) => pattern.test(v.name) && !(gender === "male" && /female/i.test(v.name)));
          if (matching.length) voices = matching;
        }
        voices = voices.slice(0, 8);
        if (voices.length) utterance.voice = voices[voiceIndex % voices.length];
        utterance.pitch = gender === "male" ? [0.85, 0.75, 0.95][voiceIndex % 3] : gender === "female" ? [1.1, 1.0, 1.2][voiceIndex % 3] : [1.15, 0.8, 1.0, 1.3, 0.9][voiceIndex % 5];
        utterance.rate = 1.02;
        const done = () => { if (npc) setSpeaking(npc, null); resolve(); };
        utterance.onstart = () => { if (npc) setSpeaking(npc, "synthetic"); };
        utterance.onend = done;
        utterance.onerror = done;
        speechSynthesis.speak(utterance);
      });
    }

    function stopAll() {
      for (const source of activeSources) {
        try { source.stop(); } catch (_) { /* already stopped */ }
      }
      activeSources.clear();
      if (global.speechSynthesis) speechSynthesis.cancel();
      for (const state of motion.values()) {
        state.source = null;
        state.thinking = false;
      }
    }

    // ---------- Body language ----------
    // Additive head motion on top of each model's idle/gesture pose, driven by how loud the
    // NPC's speech is right now: a stand-in for lip-sync, since these models have no mouth
    // blend shapes. The mixer rewrites the head bone every frame, so offsets never pile up.
    const motion = new Map(); // npc -> { source: AnalyserNode | "synthetic" | null, thinking, level, talk, think }
    const levelBuffer = new Uint8Array(512);

    function stateFor(npc) {
      let state = motion.get(npc);
      if (!state) {
        state = { source: null, thinking: false, level: 0, talk: 0, think: 0 };
        motion.set(npc, state);
      }
      return state;
    }

    function setSpeaking(npc, source) {
      if (npc && npc.group) stateFor(npc).source = source;
    }

    function setThinking(npc, thinking) {
      if (npc && npc.group) stateFor(npc).thinking = thinking;
    }

    function isSpeaking(npc) {
      const state = motion.get(npc);
      return !!(state && state.source);
    }

    function levelOf(state, t) {
      if (state.source === "synthetic") return 0.45 + 0.35 * Math.sin(t * 11) * Math.sin(t * 3.7);
      if (!state.source) return 0;
      state.source.getByteTimeDomainData(levelBuffer);
      let sum = 0;
      for (let i = 0; i < levelBuffer.length; i++) {
        const v = (levelBuffer[i] - 128) / 128;
        sum += v * v;
      }
      return Math.min(1, Math.sqrt(sum / levelBuffer.length) * 4);
    }

    game.onFrame((dt) => {
      const t = performance.now() / 1000;
      const ease = 1 - Math.exp(-dt * 5);
      for (const [npc, state] of motion) {
        state.level += (levelOf(state, t) - state.level) * Math.min(1, dt * 18);
        state.talk += ((state.source ? 1 : 0) - state.talk) * ease;
        state.think += ((state.thinking ? 1 : 0) - state.think) * ease;
        const head = npc.headBone;
        if (head) {
          const l = state.level * state.talk;
          head.rotation.x += l * 0.22 + Math.sin(t * 6.5) * 0.05 * l - state.think * 0.12;
          head.rotation.y += Math.sin(t * 1.9) * 0.1 * state.talk;
          head.rotation.z += Math.sin(t * 2.6) * 0.06 * l + state.think * 0.18;
        }
        npc.group.scale.set(1, 1 + state.level * state.talk * 0.015, 1);
      }
      updateListener();
    });

    // A gesture clip for a line, from its mood, any game actions, and the text itself.
    // null = just talk. signature = an NPC's own flourish for exclamations.
    function chooseGesture({ text, mood, actions }, signature) {
      const t = text || "";
      const m = (mood || "").toLowerCase();
      if (actions && actions.length) {
        return /give|hand|trade|sell|offer|buy/i.test(actions[0].name) ? "Use_Item" : "Interact";
      }
      if (/delight|happy|joy|excit|cheer|amus|glad|thrill|merry/.test(m) || /\b(ha){2,}\b|\bha!|\bhah\b/i.test(t)) return "Cheer";
      if (/wary|suspici|annoy|angry|hostile|defensive|irritat|offend/.test(m)) return "Block";
      if (signature && (/^\W*(hear ye|oyez|listen|attention)/i.test(t) || (t.match(/!/g) || []).length >= 2)) return signature;
      return Math.random() < 0.65 ? "Interact" : null;
    }

    return {
      ensureAudio,
      capabilities,
      createPushToTalk,
      streamScene,
      playWav,
      speakBrowser,
      stopAll,
      setSpeaking,
      setThinking,
      isSpeaking,
      chooseGesture
    };
  }

  // Short form of a label for addressing someone: "Mira, Tavern Keeper" -> "Mira".
  function shortName(label) {
    return String(label || "").split(/[ ,]/).filter(Boolean)[0] || label;
  }

  // Stable small integer per id, so each NPC keeps the same browser fallback voice.
  function voiceIndexFor(id) {
    let hash = 0;
    for (const ch of String(id)) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    return hash % 7;
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.createVoice = createVoice;
  global.GameRagDemo.shortName = shortName;
  global.GameRagDemo.voiceIndexFor = voiceIndexFor;
})(window);
