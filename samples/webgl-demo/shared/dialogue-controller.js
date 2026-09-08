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

    let activeNpc = null;
    let dialogueOpen = false;
    let transcript = [];

    function showInteractPrompt(npc) {
      interactPrompt.hidden = !npc || dialogueOpen;
      if (npc) {
        interactPrompt.textContent = "";
        const kbd = document.createElement("kbd");
        kbd.textContent = "E";
        interactPrompt.appendChild(kbd);
        interactPrompt.appendChild(document.createTextNode(` Talk to ${npc.label}`));
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
      dialogueInput.focus();
    }

    function closeDialogue() {
      dialogueOpen = false;
      game.setDialogueOpen(false);
      dialoguePanel.hidden = true;
      activeNpc = null;
      transcript = [];
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
