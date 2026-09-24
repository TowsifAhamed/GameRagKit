(function () {
  "use strict";

  const npcGrid = document.getElementById("npc-grid");
  const npcCountEl = document.getElementById("npc-count");
  const sidePanel = document.getElementById("side-panel");
  const panelNpcName = document.getElementById("panel-npc-name");
  const panelClose = document.getElementById("panel-close");
  const fieldSystemPrompt = document.getElementById("field-system-prompt");
  const fieldStyle = document.getElementById("field-style");
  const fieldMoodTracking = document.getElementById("field-mood-tracking");
  const saveButton = document.getElementById("save-button");
  const saveStatus = document.getElementById("save-status");
  const chatLog = document.getElementById("chat-log");
  const chatForm = document.getElementById("chat-form");
  const chatInput = document.getElementById("chat-input");

  const PROTOCOL_HEADER = { "X-GameRAG-Protocol": "1" };

  // ---- API -------------------------------------------------------------

  async function fetchJson(url, options) {
    const response = await fetch(url, options);
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      const message = (body && body.error) || `Request failed (${response.status})`;
      throw new Error(message);
    }
    return body;
  }

  const api = {
    listNpcs: () => fetchJson("/studio/api/npcs"),
    getYaml: (id) => fetchJson(`/studio/api/npcs/${encodeURIComponent(id)}/yaml`),
    saveYaml: (id, edits) =>
      fetchJson(`/studio/api/npcs/${encodeURIComponent(id)}/yaml`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(edits)
      }),
    ask: (npc, question) =>
      fetchJson("/ask", {
        method: "POST",
        headers: Object.assign({ "Content-Type": "application/json" }, PROTOCOL_HEADER),
        body: JSON.stringify({ npc, question })
      })
  };

  // ---- YAML field extraction for populating the form --------------------
  // The API only returns the raw YAML text (so the patcher can apply
  // comment-preserving edits); the studio parses out just the handful of
  // fields it displays using the same simple patterns PersonaYamlPatcher
  // recognizes, rather than a full YAML parser in the browser.

  function extractPersonaFields(yamlText) {
    const lines = yamlText.replace(/\r\n/g, "\n").split("\n");
    const personaIndex = lines.findIndex((l) => /^persona:\s*$/.test(l));
    if (personaIndex < 0) {
      return { systemPrompt: "", style: "", moodTracking: false };
    }

    let systemPrompt = "";
    let style = "";
    let moodTracking = false;

    for (let i = personaIndex + 1; i < lines.length; i++) {
      const line = lines[i];
      if (line.length > 0 && !/^\s/.test(line)) {
        break; // dedented out of the persona: block
      }

      const styleMatch = line.match(/^\s{2}style:\s*(.*)$/);
      if (styleMatch) {
        style = unquote(styleMatch[1].trim());
        continue;
      }

      const moodMatch = line.match(/^\s{2}mood_tracking:\s*(true|false)\s*$/);
      if (moodMatch) {
        moodTracking = moodMatch[1] === "true";
        continue;
      }

      const promptMatch = line.match(/^\s{2}system_prompt:\s*([>|])\s*$/);
      if (promptMatch) {
        const promptLines = [];
        for (let j = i + 1; j < lines.length; j++) {
          if (/^\s{4}/.test(lines[j]) || lines[j].trim() === "") {
            promptLines.push(lines[j].replace(/^\s{4}/, ""));
          } else {
            break;
          }
        }
        systemPrompt = promptLines.join(" ").trim();
        continue;
      }

      const inlinePromptMatch = line.match(/^\s{2}system_prompt:\s*(.+)$/);
      if (inlinePromptMatch) {
        systemPrompt = unquote(inlinePromptMatch[1].trim());
      }
    }

    return { systemPrompt, style, moodTracking };
  }

  function unquote(value) {
    if (value.length >= 2 && value[0] === '"' && value[value.length - 1] === '"') {
      return value.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, "\\");
    }
    return value;
  }

  // ---- NPC grid: one card per persona, with a portrait image or a ------
  // generated initial-avatar fallback --------------------------------------
  //
  // Only a handful of the bundled demo personas (guard-north-gate,
  // tavern-keeper-mira) have a real cropped screenshot portrait under
  // images/npc-*.jpg -- the rest (courier/vendor/office-worker archetypes
  // used by the Metropolis crowd demo, plus any user-authored persona) have
  // no matching artwork at all, so they always fall back to a deterministic
  // colored initial avatar rather than guessing a wrong character image for
  // them.
  const PORTRAIT_BY_PERSONA_ID = {
    "guard-north-gate": "images/npc-guard.jpg",
    "tavern-keeper-mira": "images/npc-tavern-keeper.jpg"
  };

  // Same idea as GitHub/Slack's default avatars: a stable color derived from
  // the persona id (so the same NPC always gets the same color across a
  // reload) plus its initials, so cards without a real portrait are still
  // visually distinct from each other at a glance instead of all looking
  // identical.
  const AVATAR_COLORS = ["#6ea8fe", "#5ec26a", "#e5735a", "#d4c23a", "#8a8ad4", "#d45a5a", "#5ac27a", "#d48a3a"];

  function hashString(value) {
    let hash = 0;
    for (let i = 0; i < value.length; i++) {
      hash = (hash * 31 + value.charCodeAt(i)) | 0;
    }
    return Math.abs(hash);
  }

  function initialsFor(personaId) {
    const words = personaId.split(/[-_\s]+/).filter(Boolean);
    if (words.length === 0) return "?";
    if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
    return (words[0][0] + words[1][0]).toUpperCase();
  }

  function labelFor(personaId) {
    return personaId
      .split(/[-_]+/)
      .filter(Boolean)
      .map((w) => w[0].toUpperCase() + w.slice(1))
      .join(" ");
  }

  function buildCardMedia(personaId) {
    const portraitSrc = PORTRAIT_BY_PERSONA_ID[personaId];
    if (portraitSrc) {
      const img = document.createElement("img");
      img.className = "npc-card-portrait";
      img.src = portraitSrc;
      img.alt = "";
      img.loading = "lazy";
      return img;
    }

    const avatar = document.createElement("div");
    avatar.className = "npc-card-avatar";
    avatar.style.background = AVATAR_COLORS[hashString(personaId) % AVATAR_COLORS.length];
    avatar.textContent = initialsFor(personaId);
    return avatar;
  }

  const cardsByPersonaId = new Map();

  function createCard(personaId) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "npc-card";
    card.dataset.personaId = personaId;

    card.appendChild(buildCardMedia(personaId));

    const name = document.createElement("div");
    name.className = "npc-card-name";
    name.textContent = labelFor(personaId);
    card.appendChild(name);

    card.addEventListener("click", () => openNpc(personaId));

    npcGrid.appendChild(card);
    cardsByPersonaId.set(personaId, card);
    return card;
  }

  function renderGrid(personaIds) {
    npcGrid.innerHTML = "";
    cardsByPersonaId.clear();
    personaIds.forEach(createCard);
  }

  function setActiveCard(personaId) {
    if (activePersonaId && cardsByPersonaId.has(activePersonaId)) {
      cardsByPersonaId.get(activePersonaId).classList.remove("active");
    }
    activePersonaId = personaId;
    if (personaId && cardsByPersonaId.has(personaId)) {
      cardsByPersonaId.get(personaId).classList.add("active");
    }
  }

  // ---- Side panel: persona form + chat tester ----------------------------

  let activePersonaId = null;
  let currentPersonaId = null;

  async function openNpc(personaId) {
    currentPersonaId = personaId;
    setActiveCard(personaId);
    panelNpcName.textContent = labelFor(personaId);
    sidePanel.hidden = false;
    chatLog.innerHTML = "";
    saveStatus.textContent = "";
    saveStatus.className = "";

    try {
      const { yaml } = await api.getYaml(personaId);
      const fields = extractPersonaFields(yaml);
      fieldSystemPrompt.value = fields.systemPrompt;
      fieldStyle.value = fields.style;
      fieldMoodTracking.checked = fields.moodTracking;
    } catch (err) {
      addChatMessage("system", `Failed to load NPC: ${err.message}`);
    }
  }

  panelClose.addEventListener("click", () => {
    sidePanel.hidden = true;
    setActiveCard(null);
    currentPersonaId = null;
  });

  saveButton.addEventListener("click", async () => {
    if (!currentPersonaId) return;
    saveButton.disabled = true;
    saveStatus.textContent = "Saving...";
    saveStatus.className = "";

    try {
      await api.saveYaml(currentPersonaId, {
        systemPrompt: fieldSystemPrompt.value,
        style: fieldStyle.value,
        moodTracking: fieldMoodTracking.checked
      });
      saveStatus.textContent = "Saved. Chat tester now uses the updated persona.";
      saveStatus.className = "success";
    } catch (err) {
      saveStatus.textContent = `Save failed: ${err.message}`;
      saveStatus.className = "error";
    } finally {
      saveButton.disabled = false;
    }
  });

  function addChatMessage(role, text) {
    const el = document.createElement("div");
    el.className = `chat-message ${role}`;
    el.textContent = text;
    chatLog.appendChild(el);
    chatLog.scrollTop = chatLog.scrollHeight;
    return el;
  }

  chatForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const question = chatInput.value.trim();
    if (!question || !currentPersonaId) return;

    addChatMessage("player", question);
    chatInput.value = "";
    const pending = addChatMessage("system", "Thinking...");

    try {
      const reply = await api.ask(currentPersonaId, question);
      pending.remove();
      addChatMessage("npc", reply.answer);
      if (reply.mood) {
        addChatMessage("system", `mood: ${reply.mood.value} (${reply.mood.intensity.toFixed(1)})`);
      }
    } catch (err) {
      pending.remove();
      addChatMessage("system", `Error: ${err.message}`);
    }
  });

  // ---- Boot ---------------------------------------------------------------

  async function boot() {
    try {
      const npcs = await api.listNpcs();
      npcCountEl.textContent = `${npcs.length} NPC${npcs.length === 1 ? "" : "s"}`;
      renderGrid(npcs.map((n) => n.personaId));
    } catch (err) {
      npcCountEl.textContent = "Failed to load NPCs";
      console.error(err);
    }
  }

  boot();
})();
