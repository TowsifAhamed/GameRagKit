// Wires the proximity-based "press E to talk" prompt and the dialogue panel to a
// GameRagDemo game instance (see game-engine.js). Shared by the one-on-one demo scenes.
//
// Voice: hold V (or the mic button) to talk and NPCs answer out loud, with gestures and
// talking motion on actors that have an animated model. Speech plumbing, including the
// browser fallbacks used when the server has no speech models, lives in voice-io.js.
(function (global) {
  "use strict";

  const HISTORY_LIMIT = 16;
  // The NPC keeps what it never got to say for this many of its later lines.
  const UNSAID_TURNS = 2;

  function attachDialogueController(game, options) {
    const voice = GameRagDemo.createVoice(game, { serverUrl: (options && options.serverUrl) || "" });
    const el = (id) => document.getElementById(id);
    const interactPrompt = el("interact-prompt");
    const dialoguePanel = el("dialogue-panel");
    const dialogueNpcName = el("dialogue-npc-name");
    const dialogueLog = el("dialogue-log");
    const dialogueForm = el("dialogue-form");
    const dialogueInput = el("dialogue-input");
    const dialogueClose = el("dialogue-close");
    // Optional voice UI: pages without these elements stay text-only.
    const micBtn = el("mic-button");
    const statusEl = el("scene-status");
    const voiceToggle = el("voice-toggle");
    const micHint = el("mic-hint");
    const voiceMode = el("voice-mode");

    let activeNpc = null;
    let dialogueOpen = false;
    let transcript = []; // [{ role: "player" | "npc", text, interrupted? }] for the CURRENT conversation
    let unsaid = null;   // { text, turnsLeft }: what the NPC was cut off before saying
    let speaking = null; // { entry, lineEl } for the reply being voiced right now
    let caps = { serverStt: false, canListen: false, serverTts: new Set() };
    let currentRequest = null;

    // Touch devices have no "E" key, so the same prompt element doubles as a tappable
    // button there (see touch-controls.js, which adds the touch-mode class to <body>) --
    // one prompt element serves both input modes rather than maintaining two.
    const isTouch = document.body.classList.contains("touch-mode");

    function showInteractPrompt(npc) {
      interactPrompt.hidden = !npc || dialogueOpen;
      if (npc) {
        interactPrompt.textContent = "";
        if (isTouch) {
          interactPrompt.appendChild(document.createTextNode(`Talk to ${npc.label}`));
        } else {
          const kbd = document.createElement("kbd");
          kbd.textContent = "E";
          interactPrompt.appendChild(kbd);
          interactPrompt.appendChild(document.createTextNode(` Talk to ${npc.label}`));
        }
      }
    }

    game.onProximityChange((npc) => {
      if (!dialogueOpen) {
        showInteractPrompt(npc);
      }
    });

    function setStatus(text) {
      if (!statusEl) return;
      statusEl.textContent = text;
      statusEl.hidden = !text;
    }

    async function loadCapabilities(npc) {
      if (!micBtn) return;
      caps = await voice.capabilities([npc.npcId]);
      if (activeNpc !== npc) return;
      micBtn.hidden = !caps.canListen;
      if (micHint) micHint.hidden = micBtn.hidden;
      if (voiceMode) voiceMode.textContent = caps.serverTts.has(npc.npcId) ? "server voice" : "browser voice";
    }

    function openDialogue(npc) {
      activeNpc = npc;
      dialogueOpen = true;
      game.setDialogueOpen(true);
      transcript = [{ role: "npc", text: npc.greeting }];
      unsaid = null;
      interactPrompt.hidden = true;
      dialoguePanel.hidden = false;
      dialogueNpcName.textContent = npc.label;
      dialogueLog.innerHTML = "";
      addLine("npc", npc.greeting);
      document.exitPointerLock();
      // Marks the dialogue as open for touch-controls.js, which hides the joystick/look
      // zones while this class is set -- otherwise their full-screen touch targets sit
      // over the dialogue panel and swallow taps meant for the input/log/close button.
      document.body.classList.add("dialogue-open");
      voice.ensureAudio();
      loadCapabilities(npc);
      // Desktop: keep focus off the input so hold-V talks; Enter starts typing. Touch:
      // autofocusing would pop the keyboard over the greeting.
      dialogueInput.blur();
    }

    // Stops the NPC mid-sentence (player barged in, or left). Its transcript entry keeps
    // only the words actually heard; the rest is remembered as unsaid for later.
    function interrupt() {
      const cut = voice.stopAll();
      const current = speaking;
      speaking = null;
      if (!current) return;
      const { said, unsaid: left } = cut || { said: "", unsaid: current.entry.text };
      if (said) {
        current.entry.text = said;
        current.entry.interrupted = true;
        current.lineEl.textContent = `${said} —`;
        current.lineEl.classList.add("cut-off");
        current.lineEl.title = `Cut off. Didn't get to say: "${left}"`;
      } else {
        transcript.splice(transcript.indexOf(current.entry), 1);
        current.lineEl.remove();
      }
      if (left) unsaid = { text: unsaid ? `${unsaid.text} ${left}`.slice(-400) : left, turnsLeft: UNSAID_TURNS };
    }

    function closeDialogue() {
      if (currentRequest) currentRequest.abort();
      interrupt();
      if (ptt) ptt.stop();
      dialogueOpen = false;
      game.setDialogueOpen(false);
      dialoguePanel.hidden = true;
      activeNpc = null;
      transcript = [];
      setStatus("");
      document.body.classList.remove("dialogue-open");
      showInteractPrompt(game.getClosestNpc());
    }

    function addLine(role, text) {
      const el = document.createElement("div");
      el.className = `dialogue-line ${role}`;
      el.textContent = role === "player" ? `You: ${text}` : text;
      dialogueLog.appendChild(el);
      dialogueLog.scrollTop = dialogueLog.scrollHeight;
      return el;
    }

    // Speaks a line: the server clip when there is one, else the browser voice. Actors
    // (addNpc NPCs with a model) gesture and move their head while talking.
    async function speak(npc, text, wavBase64, meta, lineEl, entry) {
      const actor = npc.group ? npc : null;
      speaking = { entry, lineEl };
      if (actor) {
        const gesture = voice.chooseGesture(Object.assign({ text }, meta || {}), npc.signatureGesture);
        if (gesture) game.playGesture(actor, gesture);
      }
      try {
        if (wavBase64) {
          await voice.playWav(wavBase64, { position: actor ? actor.group.position : null, npc: actor, text });
        } else {
          await voice.speakBrowser(text, GameRagDemo.voiceIndexFor(npc.npcId), actor);
        }
      } catch (err) {
        console.warn("[dialogue] playback failed", err);
      }
      // Finished without being cut off: it had its chance to say what it held back.
      if (speaking && speaking.entry === entry) {
        speaking = null;
        if (unsaid && --unsaid.turnsLeft <= 0) unsaid = null;
      }
    }

    // /scene/ask with a single participant: same streaming + voice path as the group
    // scene, with history so the NPC sees the whole conversation.
    async function ask({ text, audio }) {
      const npc = activeNpc;
      if (!npc) return;
      if (currentRequest) currentRequest.abort();
      interrupt();
      const controller = new AbortController();
      currentRequest = controller;

      const withVoice = !!(voiceToggle && voiceToggle.checked);
      const serverVoice = withVoice && caps.serverTts.has(npc.npcId);
      const history = transcript.slice(-HISTORY_LIMIT).map((t) => ({ speaker: t.role === "player" ? "player" : npc.npcId, text: t.text, interrupted: t.interrupted || undefined }));
      // An archetype NPC (many crowd figures sharing one backend agent) can otherwise give
      // itself a different name each turn; quoting its last reply back and forbidding a
      // new name keeps it consistent (soft phrasing wasn't followed by small models).
      const lastNpcLine = [...transcript].reverse().find((t) => t.role === "npc");
      const settings = {
        history,
        maxResponders: 1,
        maxReactions: 0,
        synthesizeReply: serverVoice,
        unsaid: unsaid ? [{ speaker: npc.npcId, text: unsaid.text }] : undefined,
        options: lastNpcLine
          ? { systemOverride: `You already told the player this in your last reply: "${lastNpcLine.text}". You MUST stay consistent with that -- if it named you, use that exact same name again if asked; do not pick a different name or contradict what you already said.` }
          : undefined
      };
      if (text) {
        transcript.push({ role: "player", text });
        setStatus("…");
      }

      let reply = null;
      let clip;
      try {
        await voice.streamScene({
          participants: [{ npc: npc.npcId, name: GameRagDemo.shortName(npc.label) }],
          message: text,
          audio,
          settings,
          signal: controller.signal,
          onEvent: (event) => {
            if (event.type === "transcript") {
              addLine("player", event.text);
              transcript.push({ role: "player", text: event.text });
            } else if (event.type === "thinking") {
              setStatus(`${npc.label} is thinking…`);
              voice.setThinking(npc, true);
            } else if (event.type === "turn") {
              voice.setThinking(npc, false);
              reply = { text: event.text, mood: event.mood && event.mood.value, actions: event.actions };
              // Voice mode: shown and remembered when spoken (see below), like subtitles.
              if (!withVoice) {
                addLine("npc", event.text);
                transcript.push({ role: "npc", text: event.text });
              }
            } else if (event.type === "audio") {
              clip = event.wavBase64 || null;
            } else if (event.type === "error") {
              addLine("system", `Error: ${event.error}`);
            }
          }
        });
      } catch (err) {
        if (err.name !== "AbortError") addLine("system", `Error: ${err.message}`);
      } finally {
        voice.setThinking(npc, false);
        if (currentRequest === controller) currentRequest = null;
      }

      if (controller.signal.aborted || !reply || activeNpc !== npc) {
        setStatus("");
        return;
      }
      if (withVoice) {
        setStatus(`${npc.label} is speaking…`);
        const entry = { role: "npc", text: reply.text };
        transcript.push(entry);
        const lineEl = addLine("npc", reply.text);
        await speak(npc, reply.text, clip, reply, lineEl, entry);
      } else if (npc.group) {
        const gesture = voice.chooseGesture(reply, npc.signatureGesture);
        if (gesture) game.playGesture(npc, gesture);
      }
      if (activeNpc === npc && !currentRequest) setStatus("");
    }

    const ptt = micBtn
      ? voice.createPushToTalk({
          button: micBtn,
          getMode: () => (caps.serverStt ? "server" : "browser"),
          onStart: () => {
            if (currentRequest) currentRequest.abort();
            interrupt();
          },
          onStatus: setStatus,
          onError: (message) => { setStatus(""); addLine("system", message); },
          onAudio: (wav) => ask({ audio: wav }),
          onText: (text) => { addLine("player", text); ask({ text }); }
        })
      : null;

    window.addEventListener("keydown", (event) => {
      if (dialogueOpen) {
        if (event.code === "Escape") {
          if (document.activeElement === dialogueInput) dialogueInput.blur();
          else closeDialogue();
        } else if (event.code === "Enter" && document.activeElement !== dialogueInput) {
          event.preventDefault();
          dialogueInput.focus();
        } else if (event.code === "KeyV" && ptt && !micBtn.hidden && document.activeElement !== dialogueInput && !event.repeat) {
          event.preventDefault();
          ptt.start();
        }
        return;
      }

      if (event.code === "KeyE") {
        const npc = game.getClosestNpc();
        if (npc) {
          event.preventDefault();
          openDialogue(npc);
        }
      }
    });

    window.addEventListener("keyup", (event) => {
      if (dialogueOpen && ptt && event.code === "KeyV") ptt.stop();
    });

    if (isTouch) {
      interactPrompt.addEventListener("click", () => {
        const npc = game.getClosestNpc();
        if (npc) {
          openDialogue(npc);
        }
      });
    }

    if (dialogueClose) {
      dialogueClose.addEventListener("click", () => closeDialogue());
    }

    dialogueForm.addEventListener("submit", (event) => {
      event.preventDefault();
      const question = dialogueInput.value.trim();
      if (!question || !activeNpc) return;
      addLine("player", question);
      dialogueInput.value = "";
      if (!isTouch) dialogueInput.blur(); // hand the keyboard back to hold-V
      ask({ text: question });
    });

    showInteractPrompt(null);
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.attachDialogueController = attachDialogueController;
})(window);
