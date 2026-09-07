(function () {
  "use strict";

  const canvas = document.getElementById("npc-canvas");
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
  const NODE_RADIUS = 34;
  const NODE_COLOR = 0x6ea8fe;
  const NODE_COLOR_HOVER = 0x85b6ff;
  const NODE_COLOR_ACTIVE = 0x5ec26a;

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

  // ---- WebGL canvas (Three.js): pan / zoom / drag NPC nodes -------------

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0f1115);

  const camera = new THREE.OrthographicCamera(0, 0, 0, 0, -1000, 1000);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });

  let viewWidth = 0;
  let viewHeight = 0;
  let panX = 0;
  let panY = 0;
  let zoom = 1;

  function resize() {
    viewWidth = canvas.clientWidth;
    viewHeight = canvas.clientHeight;
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    renderer.setSize(viewWidth, viewHeight, false);
    updateCamera();
  }

  function updateCamera() {
    const halfW = viewWidth / 2 / zoom;
    const halfH = viewHeight / 2 / zoom;
    camera.left = panX - halfW;
    camera.right = panX + halfW;
    camera.top = panY + halfH;
    camera.bottom = panY - halfH;
    camera.updateProjectionMatrix();
  }

  window.addEventListener("resize", resize);

  // NPC node = a circle mesh + a label sprite, grouped so both move together.
  const nodesByPersonaId = new Map();

  function makeLabelTexture(text) {
    const canvasEl = document.createElement("canvas");
    const ctx = canvasEl.getContext("2d");
    const fontSize = 28;
    ctx.font = `600 ${fontSize}px -apple-system, sans-serif`;
    const textWidth = ctx.measureText(text).width;
    canvasEl.width = Math.ceil(textWidth) + 24;
    canvasEl.height = fontSize + 16;

    ctx.font = `600 ${fontSize}px -apple-system, sans-serif`;
    ctx.fillStyle = "#e6e8ec";
    ctx.textBaseline = "middle";
    ctx.fillText(text, 12, canvasEl.height / 2);

    const texture = new THREE.CanvasTexture(canvasEl);
    texture.needsUpdate = true;
    return { texture, width: canvasEl.width, height: canvasEl.height };
  }

  function createNode(personaId, x, y) {
    const group = new THREE.Group();
    group.position.set(x, y, 0);

    const circleGeometry = new THREE.CircleGeometry(NODE_RADIUS, 48);
    const circleMaterial = new THREE.MeshBasicMaterial({ color: NODE_COLOR });
    const circle = new THREE.Mesh(circleGeometry, circleMaterial);
    circle.userData.personaId = personaId;
    group.add(circle);

    const ringGeometry = new THREE.RingGeometry(NODE_RADIUS, NODE_RADIUS + 3, 48);
    const ringMaterial = new THREE.MeshBasicMaterial({ color: 0x0f1115 });
    group.add(new THREE.Mesh(ringGeometry, ringMaterial));

    const label = makeLabelTexture(personaId);
    const spriteMaterial = new THREE.SpriteMaterial({ map: label.texture, depthTest: false });
    const sprite = new THREE.Sprite(spriteMaterial);
    const scale = 0.6;
    sprite.scale.set(label.width * scale, label.height * scale, 1);
    sprite.position.set(0, -(NODE_RADIUS + 22), 1);
    group.add(sprite);

    scene.add(group);
    nodesByPersonaId.set(personaId, { group, circle, circleMaterial });
    return group;
  }

  function layoutNodes(personaIds) {
    const radius = Math.max(180, personaIds.length * 40);
    const angleStep = (2 * Math.PI) / Math.max(personaIds.length, 1);
    personaIds.forEach((id, index) => {
      const angle = index * angleStep;
      const x = Math.cos(angle) * radius;
      const y = Math.sin(angle) * radius;
      createNode(id, x, y);
    });
  }

  // ---- Pointer interaction: pan background, drag nodes, click to open ---

  const raycaster = new THREE.Raycaster();
  const pointerNdc = new THREE.Vector2();
  let activePersonaId = null;
  let draggingNode = null;
  let panning = false;
  let lastPointer = { x: 0, y: 0 };
  let pointerDownAt = { x: 0, y: 0 };
  let hoveredCircle = null;

  function screenToWorld(clientX, clientY) {
    const rect = canvas.getBoundingClientRect();
    const halfW = viewWidth / 2 / zoom;
    const halfH = viewHeight / 2 / zoom;
    const nx = (clientX - rect.left) / rect.width;
    const ny = (clientY - rect.top) / rect.height;
    return {
      x: panX - halfW + nx * (2 * halfW),
      y: panY + halfH - ny * (2 * halfH)
    };
  }

  function pickCircleAt(clientX, clientY) {
    const rect = canvas.getBoundingClientRect();
    pointerNdc.x = ((clientX - rect.left) / rect.width) * 2 - 1;
    pointerNdc.y = -((clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(pointerNdc, camera);
    const circles = Array.from(nodesByPersonaId.values()).map((n) => n.circle);
    const hits = raycaster.intersectObjects(circles, false);
    return hits.length > 0 ? hits[0].object : null;
  }

  canvas.addEventListener("pointerdown", (event) => {
    canvas.setPointerCapture(event.pointerId);
    pointerDownAt = { x: event.clientX, y: event.clientY };
    lastPointer = { x: event.clientX, y: event.clientY };

    const hit = pickCircleAt(event.clientX, event.clientY);
    if (hit) {
      draggingNode = nodesByPersonaId.get(hit.userData.personaId);
    } else {
      panning = true;
    }
  });

  canvas.addEventListener("pointermove", (event) => {
    const dx = event.clientX - lastPointer.x;
    const dy = event.clientY - lastPointer.y;
    lastPointer = { x: event.clientX, y: event.clientY };

    if (draggingNode) {
      draggingNode.group.position.x += dx / zoom;
      draggingNode.group.position.y -= dy / zoom;
      return;
    }

    if (panning) {
      panX -= dx / zoom;
      panY += dy / zoom;
      updateCamera();
      return;
    }

    const hit = pickCircleAt(event.clientX, event.clientY);
    if (hoveredCircle && hoveredCircle !== hit) {
      const entry = nodesByPersonaId.get(hoveredCircle.userData.personaId);
      if (entry && entry.circle.userData.personaId !== activePersonaId) {
        entry.circleMaterial.color.setHex(NODE_COLOR);
      }
    }
    if (hit) {
      const entry = nodesByPersonaId.get(hit.userData.personaId);
      if (entry.circle.userData.personaId !== activePersonaId) {
        entry.circleMaterial.color.setHex(NODE_COLOR_HOVER);
      }
      canvas.style.cursor = "pointer";
    } else {
      canvas.style.cursor = panning ? "grabbing" : "grab";
    }
    hoveredCircle = hit;
  });

  canvas.addEventListener("pointerup", (event) => {
    const movedDistance = Math.hypot(event.clientX - pointerDownAt.x, event.clientY - pointerDownAt.y);
    const wasClick = movedDistance < 4;

    if (wasClick) {
      const hit = pickCircleAt(event.clientX, event.clientY);
      if (hit) {
        openNpc(hit.userData.personaId);
      }
    }

    draggingNode = null;
    panning = false;
  });

  canvas.addEventListener("wheel", (event) => {
    event.preventDefault();
    const zoomFactor = Math.exp(-event.deltaY * 0.001);
    zoom = Math.min(4, Math.max(0.25, zoom * zoomFactor));
    updateCamera();
  }, { passive: false });

  function setActiveNode(personaId) {
    if (activePersonaId && nodesByPersonaId.has(activePersonaId)) {
      nodesByPersonaId.get(activePersonaId).circleMaterial.color.setHex(NODE_COLOR);
    }
    activePersonaId = personaId;
    if (personaId && nodesByPersonaId.has(personaId)) {
      nodesByPersonaId.get(personaId).circleMaterial.color.setHex(NODE_COLOR_ACTIVE);
    }
  }

  function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
  }

  // ---- Side panel: persona form + chat tester ----------------------------

  let currentPersonaId = null;

  async function openNpc(personaId) {
    currentPersonaId = personaId;
    setActiveNode(personaId);
    panelNpcName.textContent = personaId;
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
    setActiveNode(null);
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
    resize();
    animate();

    try {
      const npcs = await api.listNpcs();
      npcCountEl.textContent = `${npcs.length} NPC${npcs.length === 1 ? "" : "s"}`;
      layoutNodes(npcs.map((n) => n.personaId));
    } catch (err) {
      npcCountEl.textContent = "Failed to load NPCs";
      console.error(err);
    }
  }

  boot();
})();
