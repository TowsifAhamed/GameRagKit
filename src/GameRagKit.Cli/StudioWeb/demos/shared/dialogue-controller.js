// Wires the proximity-based "press E to talk" prompt and the dialogue panel to a
// GameRagDemo game instance (see game-engine.js). Shared by both demo scenes.
(function (global) {
  "use strict";

  function attachDialogueController(game) {
    const interactPrompt = document.getElementById("interact-prompt");
    const dialoguePanel = document.getElementById("dialogue-panel");
    const dialogueNpcName = document.getElementById("dialogue-npc-name");
    const dialogueLog = document.getElementById("dialogue-log");
    const dialogueForm = document.getElementById("dialogue-form");
    const dialogueInput = document.getElementById("dialogue-input");
    const dialogueClose = document.getElementById("dialogue-close");

    let activeNpc = null;
    let dialogueOpen = false;
    let transcript = [];

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

    function openDialogue(npc) {
      activeNpc = npc;
      dialogueOpen = true;
      game.setDialogueOpen(true);
      transcript = [{ role: "npc", text: npc.greeting }];
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
      if (isTouch) {
        // Autofocusing the text input pops the on-screen keyboard immediately on touch,
        // covering half the dialogue panel before the player has read the greeting.
        dialogueInput.blur();
      } else {
        dialogueInput.focus();
      }
    }

    function closeDialogue() {
      dialogueOpen = false;
      game.setDialogueOpen(false);
      dialoguePanel.hidden = true;
      activeNpc = null;
      transcript = [];
      document.body.classList.remove("dialogue-open");
      showInteractPrompt(game.getClosestNpc());
    }

    function addLine(role, text) {
      const el = document.createElement("div");
      el.className = `dialogue-line ${role}`;
      el.textContent = role === "player" ? `You: ${text}` : text;
      dialogueLog.appendChild(el);
      dialogueLog.scrollTop = dialogueLog.scrollHeight;
    }

    window.addEventListener("keydown", (event) => {
      if (dialogueOpen) {
        if (event.code === "Escape") {
          closeDialogue();
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

    dialogueForm.addEventListener("submit", async (event) => {
      event.preventDefault();
      const question = dialogueInput.value.trim();
      if (!question || !activeNpc) return;

      addLine("player", question);
      transcript.push({ role: "player", text: question });
      dialogueInput.value = "";

      try {
        const reply = await game.ask(activeNpc.npcId, question, transcript);
        addLine("npc", reply.answer);
        transcript.push({ role: "npc", text: reply.answer });
      } catch (err) {
        addLine("system", `Error: ${err.message}`);
      }
    });

    showInteractPrompt(null);
  }

  global.GameRagDemo = global.GameRagDemo || {};
  global.GameRagDemo.attachDialogueController = attachDialogueController;
})(window);
