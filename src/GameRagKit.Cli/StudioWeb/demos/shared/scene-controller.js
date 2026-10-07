// Multi-character voice conversations for a GameRagDemo scene (see game-engine.js).
// Walk up to a group of NPCs, press E, then type or hold-to-talk: the line goes to
// POST /scene/voice (or /scene/ask), the server's intent router decides who answers, and
// each NPC's reply streams back in its own voice, played as positional audio from where
// that NPC stands, with gestures, gaze and talking motion. NPCs can react to each other.
//
// Speech plumbing (server STT/TTS with browser fallbacks) lives in voice-io.js.
(function (global) {
  "use strict";

  const HISTORY_LIMIT = 20;

  function attachSceneController(game, config) {
    const voice = GameRagDemo.createVoice(game, { serverUrl: config.serverUrl || "" });
    const members = config.npcs; // [{ npc (from game.addNpc), name, color, signatureGesture? }]
    const byId = new Map(members.map((m) => [m.npc.npcId, m]));

    const el = (id) => document.getElementById(id);
    const interactPrompt = el("interact-prompt");
    const panel = el("dialogue-panel");
    const log = el("dialogue-log");
    const form = el("dialogue-form");
    const input = el("dialogue-input");
    const closeBtn = el("dialogue-close");
    const micBtn = el("mic-button");
    const statusEl = el("scene-status");
    const voiceToggle = el("voice-toggle");
    const routingEl = el("scene-routing");

    const isTouch = document.body.classList.contains("touch-mode");
    let open = false;
    let history = [];
    let caps = { serverStt: false, browserStt: false, canListen: false, serverTts: new Set() };
    let currentRequest = null; // AbortController for the in-flight scene request

    // ---------- 3D: speaking rings + gaze ----------
    const rings = new Map();
    for (const member of members) {
      const ring = new THREE.Mesh(
        new THREE.RingGeometry(0.55, 0.78, 40),
        new THREE.MeshBasicMaterial({ color: member.color, transparent: true, opacity: 0, side: THREE.DoubleSide })
      );
      ring.rotation.x = -Math.PI / 2;
      ring.position.y = 0.03;
      member.npc.group.add(ring);
      rings.set(member.npc.npcId, { ring, state: "idle" });
    }

    let speakerId = null;   // NPC currently voicing a line
    let addresseeId = null; // who that line is aimed at ("player" or an NPC id)

    function setNpcState(npcId, state) {
      const entry = rings.get(npcId);
      if (entry) entry.state = state;
      voice.setThinking(byId.get(npcId).npc, state === "thinking");
    }

    function lerpAngle(from, to, t) {
      let delta = ((to - from + Math.PI) % (2 * Math.PI)) - Math.PI;
      if (delta < -Math.PI) delta += 2 * Math.PI;
      return from + delta * t;
    }

    function positionOf(id) {
      if (id === "player") return game.camera.position;
      const member = byId.get(id);
      return member ? member.npc.group.position : null;
    }

    game.onFrame((dt) => {
      const t = performance.now() / 1000;
      const ease = 1 - Math.exp(-dt * 5);
      for (const member of members) {
        const id = member.npc.npcId;
        const entry = rings.get(id);
        const target = entry.state === "speaking" ? 0.65 + 0.25 * Math.sin(t * 9) : entry.state === "thinking" ? 0.25 + 0.15 * Math.sin(t * 4) : 0;
        entry.ring.material.opacity += (target - entry.ring.material.opacity) * 0.2;

        // Gaze: the speaker faces whoever it's addressing; everyone else watches the
        // speaker; with nobody talking, the group faces the player.
        if (open) {
          const lookAt = speakerId === id ? addresseeId || "player" : speakerId || "player";
          const pos = positionOf(lookAt);
          const group = member.npc.group;
          if (pos) group.rotation.y = lerpAngle(group.rotation.y, Math.atan2(pos.x - group.position.x, pos.z - group.position.z), ease);
        }
      }
    });

    // Who a line is aimed at: the first other participant it names, else the player.
    function addresseeOf(text, speaker) {
      let best = null;
      let bestIndex = Infinity;
      for (const m of members) {
        if (m.npc.npcId === speaker) continue;
        const match = new RegExp(`\\b${m.name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\b`, "i").exec(text);
        if (match && match.index < bestIndex) {
          best = m.npc.npcId;
          bestIndex = match.index;
        }
      }
      return best || "player";
    }

    // ---------- Ordered playback ----------
    // One "beat" = everything the NPCs say in response to one player line. Clips are keyed
    // by turn index and played strictly in order; a clip can arrive before or after its
    // turn's text (the server synthesizes in the background while the next NPC thinks).
    let beat = null;

    function newBeat(withVoice) {
      stopPlayback();
      beat = { withVoice, turns: new Map(), clips: new Map(), next: 0, playing: false, done: false, cancelled: false };
      return beat;
    }

    function stopPlayback() {
      if (beat) beat.cancelled = true;
      voice.stopAll();
      for (const id of rings.keys()) setNpcState(id, "idle");
      speakerId = null;
      addresseeId = null;
    }

    async function pump(b) {
      if (b.playing || b.cancelled) return;
      const turn = b.turns.get(b.next);
      if (!turn) {
        if (b.done) setStatus("");
        return;
      }
      if (b.withVoice && !b.clips.has(b.next)) return; // wait for this turn's clip

      b.playing = true;
      const clip = b.clips.get(b.next);
      const member = byId.get(turn.npc);
      speakerId = turn.npc;
      addresseeId = addresseeOf(turn.text, turn.npc);
      const gesture = voice.chooseGesture(turn, member.signatureGesture);
      if (gesture) game.playGesture(member.npc, gesture);
      setNpcState(turn.npc, "speaking");
      turn.lineEl.classList.add("speaking");
      setStatus(`${member.name} is speaking…`);
      try {
        if (!b.withVoice) {
          // Text-only mode: a beat per line so the turn-taking still reads.
          voice.setSpeaking(member.npc, "synthetic");
          await sleep(Math.min(4000, 600 + turn.text.length * 25));
          voice.setSpeaking(member.npc, null);
        } else if (clip && clip.wav) {
          await voice.playWav(clip.wav, { position: member.npc.group.position, npc: member.npc });
        } else {
          await voice.speakBrowser(turn.text, members.indexOf(member), member.npc);
        }
      } catch (err) {
        console.warn("[scene] playback failed", err);
      } finally {
        if (speakerId === turn.npc) {
          speakerId = null;
          addresseeId = null;
        }
        setNpcState(turn.npc, "idle");
        turn.lineEl.classList.remove("speaking");
        b.playing = false;
        b.next++;
        if (!b.cancelled) pump(b);
      }
    }

    // ---------- Conversation UI ----------
    function nameOf(npcId) {
      return npcId === "player" ? "You" : (byId.get(npcId) || { name: npcId }).name;
    }

    function setStatus(text) {
      statusEl.textContent = text;
      statusEl.hidden = !text;
    }

    function addLine(kind, text, npcId) {
      const line = document.createElement("div");
      line.className = `dialogue-line ${kind}`;
      if (kind === "npc") {
        const who = document.createElement("span");
        who.className = "speaker";
        who.textContent = nameOf(npcId);
        who.style.color = "#" + byId.get(npcId).color.toString(16).padStart(6, "0");
        line.appendChild(who);
        line.appendChild(document.createTextNode(text));
      } else {
        line.textContent = kind === "player" ? `You: ${text}` : text;
      }
      log.appendChild(line);
      log.scrollTop = log.scrollHeight;
      return line;
    }

    function remember(speaker, text) {
      history.push({ speaker, text });
      if (history.length > HISTORY_LIMIT) history = history.slice(-HISTORY_LIMIT);
    }

    function showPrompt(npc) {
      const inGroup = npc && byId.has(npc.npcId);
      interactPrompt.hidden = !inGroup || open;
      if (!inGroup) return;
      interactPrompt.textContent = "";
      if (!isTouch) {
        const kbd = document.createElement("kbd");
        kbd.textContent = "E";
        interactPrompt.appendChild(kbd);
        interactPrompt.appendChild(document.createTextNode(" "));
      }
      interactPrompt.appendChild(document.createTextNode(config.promptText || "Join the conversation"));
    }

    game.onProximityChange((npc) => { if (!open) showPrompt(npc); });

    async function loadCapabilities() {
      caps = await voice.capabilities(members.map((m) => m.npc.npcId));
      micBtn.hidden = !caps.canListen;
      el("mic-hint").hidden = micBtn.hidden;
      el("voice-mode").textContent = caps.serverTts.size
        ? `server voices (${caps.serverTts.size}/${members.length})`
        : "browser voices";
    }

    function openScene() {
      open = true;
      game.setDialogueOpen(true);
      document.exitPointerLock();
      document.body.classList.add("dialogue-open");
      interactPrompt.hidden = true;
      panel.hidden = false;
      log.innerHTML = "";
      history = [];
      routingEl.textContent = "";
      voice.ensureAudio();
      if (config.openingLine) {
        const opener = members[0];
        addLine("npc", config.openingLine, opener.npc.npcId);
        remember(opener.npc.npcId, config.openingLine);
      }
      loadCapabilities();
      // Not focusing the text box: like in-game chat, Enter starts typing and hold-V
      // talks, which only works while focus isn't in the input.
      input.blur();
    }

    function closeScene() {
      open = false;
      if (currentRequest) currentRequest.abort();
      stopPlayback();
      ptt.stop();
      game.setDialogueOpen(false);
      panel.hidden = true;
      document.body.classList.remove("dialogue-open");
      setStatus("");
      showPrompt(game.getClosestNpc());
    }

    // Talking over the NPCs cuts them off (barge-in), like it would in person.
    const ptt = voice.createPushToTalk({
      button: micBtn,
      getMode: () => (caps.serverStt ? "server" : "browser"),
      onStart: () => {
        if (currentRequest) currentRequest.abort();
        stopPlayback();
      },
      onStatus: setStatus,
      onError: (message) => { setStatus(""); addLine("system", message); },
      onAudio: (wav) => runScene({ audio: wav }),
      onText: (text) => { addLine("player", text); runScene({ text }); }
    });

    window.addEventListener("keydown", (event) => {
      if (!open) {
        if (event.code === "KeyE") {
          const npc = game.getClosestNpc();
          if (npc && byId.has(npc.npcId)) {
            event.preventDefault();
            openScene();
          }
        }
        return;
      }
      if (event.code === "Escape") {
        if (document.activeElement === input) input.blur();
        else closeScene();
      } else if (event.code === "Enter" && document.activeElement !== input) {
        event.preventDefault();
        input.focus();
      } else if (event.code === "KeyV" && document.activeElement !== input && !event.repeat && !micBtn.hidden) {
        event.preventDefault();
        ptt.start();
      }
    });

    window.addEventListener("keyup", (event) => {
      if (open && event.code === "KeyV") ptt.stop();
    });

    if (isTouch) {
      interactPrompt.addEventListener("click", () => {
        const npc = game.getClosestNpc();
        if (npc && byId.has(npc.npcId)) openScene();
      });
    }

    closeBtn.addEventListener("click", closeScene);

    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const text = input.value.trim();
      if (!text) return;
      input.value = "";
      if (!isTouch) input.blur(); // hand the keyboard back to hold-V
      addLine("player", text);
      runScene({ text });
    });

    // ---------- Talking to the server ----------
    async function runScene({ text, audio }) {
      if (currentRequest) currentRequest.abort();
      const controller = new AbortController();
      currentRequest = controller;

      const withVoice = voiceToggle.checked;
      const b = newBeat(withVoice);
      // Server audio only if at least one NPC has a server voice; the rest (and every NPC
      // on a server with none) fall back to browser speech per turn.
      const serverVoices = withVoice && caps.serverTts.size > 0;
      const settings = { history, maxResponders: 2, maxReactions: 1, synthesizeReply: serverVoices };
      if (text) {
        remember("player", text);
        setStatus("…");
      }

      try {
        await voice.streamScene({
          participants: members.map((m) => ({ npc: m.npc.npcId, name: m.name })),
          message: text,
          audio,
          settings,
          signal: controller.signal,
          onEvent: (event) => handleEvent(b, event, serverVoices)
        });
      } catch (err) {
        if (err.name !== "AbortError") {
          setStatus("");
          addLine("system", `Error: ${err.message}`);
        }
      } finally {
        if (currentRequest === controller) currentRequest = null;
        b.done = true;
        // Anything still waiting on a clip that will never come: speak it in the browser.
        for (const index of b.turns.keys()) {
          if (b.withVoice && !b.clips.has(index)) b.clips.set(index, { wav: null });
        }
        pump(b);
      }
    }

    function handleEvent(b, event, serverVoices) {
      switch (event.type) {
        case "transcript":
          addLine("player", event.text);
          remember("player", event.text);
          break;
        case "routing":
          routingEl.textContent = `→ ${event.responders.map(nameOf).join(" & ")} · ${event.method.replace(/-/g, " ")}`;
          break;
        case "thinking":
          setNpcState(event.npc, "thinking");
          if (!b.playing) setStatus(`${nameOf(event.npc)} is thinking…`);
          break;
        case "turn": {
          if (b.cancelled) return;
          const lineEl = addLine("npc", event.text, event.npc);
          if (event.reason === "reaction") lineEl.classList.add("reaction");
          if (event.mood) {
            const mood = document.createElement("span");
            mood.className = "mood-tag";
            mood.textContent = event.mood.value;
            lineEl.firstChild.after(mood);
          }
          for (const action of event.actions || []) {
            const args = Object.entries(action.args || {}).map(([k, v]) => `${k}: ${v}`).join(", ");
            addLine("action", `${nameOf(event.npc)} → ${action.name}${args ? ` (${args})` : ""}`);
          }
          remember(event.npc, event.text);
          b.turns.set(event.index, { npc: event.npc, text: event.text, lineEl, mood: event.mood && event.mood.value, actions: event.actions });
          if (rings.get(event.npc).state === "thinking") setNpcState(event.npc, "idle");
          if (b.withVoice && !serverVoices) b.clips.set(event.index, { wav: null });
          pump(b);
          break;
        }
        case "audio":
          b.clips.set(event.index, { wav: event.wavBase64 });
          pump(b);
          break;
        case "error":
          addLine("system", `Error: ${event.error}`);
          break;
      }
    }

    function sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

    showPrompt(null);

    // Lets a game open the conversation or inject a line from its own code (cutscenes,
    // triggers) instead of only through the proximity prompt.
    return {
      open: openScene,
      close: closeScene,
      say(text) {
        if (!open) openScene();
        addLine("player", text);
        return runScene({ text });
      }
    };
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.attachSceneController = attachSceneController;
})(window);
