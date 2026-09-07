// Shared Three.js game-scene engine used by both webgl-demo scenes (single-npc.html and
// city.html): a WASD + mouse-look player controller, a ground plane, NPC actors with a
// name label and a proximity-triggered "press E to talk" prompt, and a dialogue overlay
// that calls the real GameRagKit /ask endpoint. Both scenes build a scene by calling
// GameRagDemo.createGame({...}) and adding NPCs/props to the returned scene.
(function (global) {
  "use strict";

  const PROTOCOL_HEADER = { "X-GameRAG-Protocol": "1" };
  const PLAYER_SPEED = 6; // world units per second
  const PROXIMITY_RADIUS = 4.5;
  const PLAYER_EYE_HEIGHT = 1.7;

  async function askNpc(serverUrl, npcId, question) {
    const response = await fetch(`${serverUrl}/ask`, {
      method: "POST",
      headers: Object.assign({ "Content-Type": "application/json" }, PROTOCOL_HEADER),
      body: JSON.stringify({ npc: npcId, question })
    });

    const body = await response.json().catch(() => null);
    if (!response.ok) {
      throw new Error((body && body.error) || `Request failed (${response.status})`);
    }

    return body;
  }

  function createGame(options) {
    const {
      canvas,
      serverUrl,
      groundSize = 80,
      groundColor = 0x2a3040,
      skyColor = 0x0f1115,
      fogColor = 0x0f1115,
      spawnPosition = { x: 0, y: 0, z: 8 }
    } = options;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(skyColor);
    scene.fog = new THREE.Fog(fogColor, 20, 70);

    const camera = new THREE.PerspectiveCamera(70, canvas.clientWidth / canvas.clientHeight, 0.1, 500);
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);

    // Lighting: one directional "sun" plus soft ambient fill, enough to read shapes and
    // colors clearly without any texture/material setup.
    scene.add(new THREE.AmbientLight(0xffffff, 0.55));
    const sun = new THREE.DirectionalLight(0xffffff, 0.9);
    sun.position.set(15, 25, 10);
    scene.add(sun);

    const ground = new THREE.Mesh(
      new THREE.PlaneGeometry(groundSize, groundSize),
      new THREE.MeshStandardMaterial({ color: groundColor })
    );
    ground.rotation.x = -Math.PI / 2;
    scene.add(ground);

    const gridHelper = new THREE.GridHelper(groundSize, groundSize / 2, 0x3a4152, 0x232838);
    scene.add(gridHelper);

    // --- Player: a simple invisible capsule-ish collider; the camera IS the player's
    // eyes (first-person), so there's no player mesh to render.
    const player = {
      position: new THREE.Vector3(spawnPosition.x, PLAYER_EYE_HEIGHT, spawnPosition.z),
      yaw: Math.PI, // facing -Z (into the scene) by default
      pitch: 0
    };

    const keysDown = new Set();
    window.addEventListener("keydown", (e) => keysDown.add(e.code));
    window.addEventListener("keyup", (e) => keysDown.delete(e.code));

    let pointerLocked = false;
    canvas.addEventListener("click", () => {
      if (!pointerLocked) {
        canvas.requestPointerLock();
      }
    });
    document.addEventListener("pointerlockchange", () => {
      pointerLocked = document.pointerLockElement === canvas;
    });
    document.addEventListener("mousemove", (e) => {
      if (!pointerLocked) return;
      const sensitivity = 0.0022;
      player.yaw -= e.movementX * sensitivity;
      player.pitch -= e.movementY * sensitivity;
      player.pitch = Math.max(-Math.PI / 2 + 0.05, Math.min(Math.PI / 2 - 0.05, player.pitch));
    });

    const npcs = [];
    const obstacles = []; // simple bounding boxes for buildings/props, for collision

    function addPlaceholderNpcMesh(group, config, bodyHeight) {
      const body = new THREE.Mesh(
        new THREE.CapsuleGeometry(0.4, bodyHeight - 0.8, 4, 12),
        new THREE.MeshStandardMaterial({ color: config.color || 0x6ea8fe })
      );
      body.position.y = bodyHeight / 2;
      group.add(body);

      const head = new THREE.Mesh(
        new THREE.SphereGeometry(0.28, 16, 16),
        new THREE.MeshStandardMaterial({ color: config.color || 0x6ea8fe })
      );
      head.position.y = bodyHeight + 0.1;
      group.add(head);
    }

    // Optional real glTF/GLB model loading. If config.model is set and THREE.GLTFLoader
    // is available (loaded separately -- see ASSETS.md), the model replaces the
    // placeholder capsule+sphere once it finishes loading; until then (or if loading
    // fails, or no model/loader is present at all), the placeholder mesh is shown so the
    // scene is never left with a missing NPC.
    function loadNpcModel(group, config, bodyHeight) {
      if (!config.model || typeof THREE.GLTFLoader !== "function") {
        return;
      }

      const loader = new THREE.GLTFLoader();
      loader.load(
        config.model,
        (gltf) => {
          // Placeholder mesh (added synchronously in addNpc) is still there; remove it
          // once the real model is ready to swap in.
          const placeholders = group.children.filter((child) => child.isMesh);
          placeholders.forEach((mesh) => group.remove(mesh));

          const modelScene = gltf.scene;
          if (config.modelScale) {
            modelScene.scale.setScalar(config.modelScale);
          }
          group.add(modelScene);
        },
        undefined,
        (error) => {
          console.warn(`[GameRagKit demo] Failed to load NPC model "${config.model}", keeping placeholder mesh.`, error);
        }
      );
    }

    function addNpc(config) {
      const group = new THREE.Group();
      group.position.set(config.position.x, 0, config.position.z);

      const bodyHeight = 1.7;
      addPlaceholderNpcMesh(group, config, bodyHeight);
      loadNpcModel(group, config, bodyHeight);

      const label = makeLabelSprite(config.label || config.npcId);
      label.position.y = bodyHeight + 0.7;
      group.add(label);

      scene.add(group);

      const npc = {
        npcId: config.npcId,
        label: config.label || config.npcId,
        greeting: config.greeting || "Hello.",
        group,
        inRange: false
      };
      npcs.push(npc);
      return npc;
    }

    function addBuilding(config) {
      const width = config.width || 6;
      const depth = config.depth || 6;
      const height = config.height || 4;
      const group = new THREE.Group();
      group.position.set(config.position.x, 0, config.position.z);

      const placeholder = new THREE.Mesh(
        new THREE.BoxGeometry(width, height, depth),
        new THREE.MeshStandardMaterial({ color: config.color || 0x3a4152 })
      );
      placeholder.position.y = height / 2;
      group.add(placeholder);
      scene.add(group);

      obstacles.push({
        minX: config.position.x - width / 2,
        maxX: config.position.x + width / 2,
        minZ: config.position.z - depth / 2,
        maxZ: config.position.z + depth / 2
      });

      // Same optional-model pattern as addNpc: if config.model is set and GLTFLoader is
      // available, swap the placeholder box for a real building model once it loads.
      if (config.model && typeof THREE.GLTFLoader === "function") {
        const loader = new THREE.GLTFLoader();
        loader.load(
          config.model,
          (gltf) => {
            group.remove(placeholder);
            const modelScene = gltf.scene;
            if (config.modelScale) {
              modelScene.scale.setScalar(config.modelScale);
            }
            group.add(modelScene);
          },
          undefined,
          (error) => {
            console.warn(`[GameRagKit demo] Failed to load building model "${config.model}", keeping placeholder box.`, error);
          }
        );
      }

      return group;
    }

    // Ambient LEGO-style crowd, rendered with InstancedMesh so hundreds/thousands of
    // simple blocky figures cost only a handful of draw calls total (one per body part
    // shared across every instance), instead of one draw call per figure. Every figure is
    // individually talkable -- proximity/E-to-talk works the same as it does for addNpc()
    // actors -- but each figure is tagged with one of a small, bounded set of archetype
    // ids (config.archetypes), and talking to a figure calls /ask against that archetype's
    // single shared NpcAgent. So a scene can render a thousand figures while the backend
    // only ever runs as many real NpcAgents as there are archetypes: clicking any of 900
    // figures fans in to ~8-15 real agents, not 900, keeping LLM/embedding load bounded
    // regardless of crowd size.
    let crowdInstances = null;

    function addCrowd(config) {
      const count = config.count || 200;
      const bounds = config.bounds || { minX: -groundSize / 2 + 2, maxX: groundSize / 2 - 2, minZ: -groundSize / 2 + 2, maxZ: groundSize / 2 - 2 };
      const colors = config.colors || [0xd4453a, 0xd4a53a, 0x3ad46a, 0x3a8ad4, 0xd43ac2, 0xe8e8e8];
      const archetypes = config.archetypes && config.archetypes.length > 0
        ? config.archetypes
        : [{ id: "passerby", label: "Passerby" }];

      const torsoGeometry = new THREE.BoxGeometry(0.5, 0.7, 0.3);
      const headGeometry = new THREE.BoxGeometry(0.35, 0.35, 0.35);

      const torsoMesh = new THREE.InstancedMesh(torsoGeometry, new THREE.MeshStandardMaterial(), count);
      const headMesh = new THREE.InstancedMesh(headGeometry, new THREE.MeshStandardMaterial({ color: 0xf0c8a0 }), count);
      torsoMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      headMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);

      const dummy = new THREE.Object3D();
      const figures = [];

      for (let i = 0; i < count; i++) {
        const x = bounds.minX + Math.random() * (bounds.maxX - bounds.minX);
        const z = bounds.minZ + Math.random() * (bounds.maxZ - bounds.minZ);
        const heading = Math.random() * Math.PI * 2;
        const speed = 0.5 + Math.random() * 0.8;
        const color = colors[i % colors.length];
        const archetype = archetypes[i % archetypes.length];

        figures.push({
          x, z, heading, speed,
          wanderTimer: Math.random() * 4,
          npcId: archetype.id,
          label: archetype.label || archetype.id,
          greeting: archetype.greeting || "Oh, hey there.",
          inRange: false
        });

        torsoMesh.setColorAt(i, new THREE.Color(color));
      }

      scene.add(torsoMesh);
      scene.add(headMesh);
      crowdInstances = { torsoMesh, headMesh, dummy, figures, bounds, count };
    }

    function updateCrowd(dt) {
      if (!crowdInstances) return;

      const { torsoMesh, headMesh, dummy, figures, bounds } = crowdInstances;

      for (let i = 0; i < figures.length; i++) {
        const f = figures[i];
        f.wanderTimer -= dt;
        if (f.wanderTimer <= 0) {
          f.heading += (Math.random() - 0.5) * 1.4;
          f.wanderTimer = 1.5 + Math.random() * 3;
        }

        const nextX = f.x + Math.sin(f.heading) * f.speed * dt;
        const nextZ = f.z + Math.cos(f.heading) * f.speed * dt;

        if (nextX < bounds.minX || nextX > bounds.maxX || collidesWithObstacle(nextX, f.z)) {
          f.heading = Math.PI - f.heading;
        } else {
          f.x = nextX;
        }

        if (nextZ < bounds.minZ || nextZ > bounds.maxZ || collidesWithObstacle(f.x, nextZ)) {
          f.heading = -f.heading;
        } else {
          f.z = nextZ;
        }

        dummy.position.set(f.x, 0.55, f.z);
        dummy.rotation.set(0, f.heading, 0);
        dummy.updateMatrix();
        torsoMesh.setMatrixAt(i, dummy.matrix);

        dummy.position.set(f.x, 1.05, f.z);
        dummy.updateMatrix();
        headMesh.setMatrixAt(i, dummy.matrix);
      }

      torsoMesh.instanceMatrix.needsUpdate = true;
      headMesh.instanceMatrix.needsUpdate = true;
      if (torsoMesh.instanceColor) {
        torsoMesh.instanceColor.needsUpdate = true;
      }
    }

    function makeLabelSprite(text) {
      const canvasEl = document.createElement("canvas");
      const ctx = canvasEl.getContext("2d");
      const fontSize = 40;
      ctx.font = `600 ${fontSize}px -apple-system, sans-serif`;
      const textWidth = ctx.measureText(text).width;
      canvasEl.width = Math.ceil(textWidth) + 32;
      canvasEl.height = fontSize + 20;

      ctx.font = `600 ${fontSize}px -apple-system, sans-serif`;
      ctx.fillStyle = "rgba(15,17,21,0.75)";
      ctx.fillRect(0, 0, canvasEl.width, canvasEl.height);
      ctx.fillStyle = "#e6e8ec";
      ctx.textBaseline = "middle";
      ctx.fillText(text, 16, canvasEl.height / 2);

      const texture = new THREE.CanvasTexture(canvasEl);
      const material = new THREE.SpriteMaterial({ map: texture, depthTest: false });
      const sprite = new THREE.Sprite(material);
      const scale = 0.014;
      sprite.scale.set(canvasEl.width * scale, canvasEl.height * scale, 1);
      return sprite;
    }

    function resize() {
      const width = canvas.clientWidth;
      const height = canvas.clientHeight;
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      renderer.setSize(width, height, false);
    }

    window.addEventListener("resize", resize);

    function collidesWithObstacle(x, z) {
      const margin = 0.4;
      for (const box of obstacles) {
        if (x > box.minX - margin && x < box.maxX + margin && z > box.minZ - margin && z < box.maxZ + margin) {
          return true;
        }
      }
      return false;
    }

    let lastTime = performance.now();
    let closestNpcInRange = null;
    const listeners = { proximityChange: [] };

    function onProximityChange(callback) {
      listeners.proximityChange.push(callback);
    }

    function tick() {
      const now = performance.now();
      const dt = Math.min(0.05, (now - lastTime) / 1000);
      lastTime = now;

      // Movement relative to look direction, flattened to the XZ plane.
      const forward = new THREE.Vector3(Math.sin(player.yaw), 0, -Math.cos(-player.yaw)).normalize();
      // Recompute forward/right correctly from yaw (camera looks down -Z at yaw=0 after
      // rotation composition below); using explicit trig avoids relying on camera state.
      const dirZ = -Math.cos(player.yaw);
      const dirX = -Math.sin(player.yaw);
      const fwd = new THREE.Vector3(dirX, 0, dirZ);
      const right = new THREE.Vector3(fwd.z, 0, -fwd.x);

      let moveX = 0;
      let moveZ = 0;
      if (keysDown.has("KeyW") || keysDown.has("ArrowUp")) { moveX += fwd.x; moveZ += fwd.z; }
      if (keysDown.has("KeyS") || keysDown.has("ArrowDown")) { moveX -= fwd.x; moveZ -= fwd.z; }
      if (keysDown.has("KeyA") || keysDown.has("ArrowLeft")) { moveX -= right.x; moveZ -= right.z; }
      if (keysDown.has("KeyD") || keysDown.has("ArrowRight")) { moveX += right.x; moveZ += right.z; }

      const moveLen = Math.hypot(moveX, moveZ);
      if (moveLen > 0.0001) {
        moveX = (moveX / moveLen) * PLAYER_SPEED * dt;
        moveZ = (moveZ / moveLen) * PLAYER_SPEED * dt;

        const nextX = player.position.x + moveX;
        const nextZ = player.position.z + moveZ;
        const bound = groundSize / 2 - 1;
        if (!collidesWithObstacle(nextX, player.position.z) && Math.abs(nextX) < bound) {
          player.position.x = nextX;
        }
        if (!collidesWithObstacle(player.position.x, nextZ) && Math.abs(nextZ) < bound) {
          player.position.z = nextZ;
        }
      }

      camera.position.copy(player.position);
      camera.rotation.set(0, 0, 0);
      camera.rotateY(player.yaw);
      camera.rotateX(player.pitch);

      // Proximity check against every NPC actor plus every crowd figure; track the single
      // closest one in range so the "press E to talk" prompt only ever targets one at a
      // time. Crowd figures use the same { npcId, label, greeting, inRange } shape as
      // addNpc() actors so both can share this one proximity loop and the dialogue
      // controller doesn't need to know which kind of thing it's talking to.
      let closest = null;
      let closestDist = Infinity;
      for (const npc of npcs) {
        const dx = npc.group.position.x - player.position.x;
        const dz = npc.group.position.z - player.position.z;
        const dist = Math.hypot(dx, dz);
        npc.inRange = dist <= PROXIMITY_RADIUS;
        if (npc.inRange && dist < closestDist) {
          closest = npc;
          closestDist = dist;
        }

        // Billboard the label sprite toward the camera.
        npc.group.children.forEach((child) => {
          if (child.isSprite) {
            child.lookAt(camera.position);
          }
        });
      }

      if (crowdInstances) {
        for (const figure of crowdInstances.figures) {
          const dx = figure.x - player.position.x;
          const dz = figure.z - player.position.z;
          const dist = Math.hypot(dx, dz);
          figure.inRange = dist <= PROXIMITY_RADIUS;
          if (figure.inRange && dist < closestDist) {
            closest = figure;
            closestDist = dist;
          }
        }
      }

      if (closest !== closestNpcInRange) {
        closestNpcInRange = closest;
        listeners.proximityChange.forEach((cb) => cb(closest));
      }

      updateCrowd(dt);

      renderer.render(scene, camera);
      requestAnimationFrame(tick);
    }

    resize();
    requestAnimationFrame(tick);

    return {
      scene,
      camera,
      renderer,
      addNpc,
      addBuilding,
      addCrowd,
      onProximityChange,
      getClosestNpc: () => closestNpcInRange,
      ask: (npcId, question) => askNpc(serverUrl, npcId, question),
      exitPointerLock: () => document.exitPointerLock()
    };
  }

  global.GameRagDemo = { createGame };
})(window);
