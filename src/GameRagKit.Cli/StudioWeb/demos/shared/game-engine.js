// Shared Three.js game-scene engine used by both webgl-demo scenes (single-npc.html and
// city.html): a WASD + mouse-look player controller, a ground plane, NPC actors with a
// name label and a proximity-triggered "press E to talk" prompt, and a dialogue overlay
// that calls the real GameRagKit /ask endpoint. Both scenes build a scene by calling
// GameRagDemo.createGame({...}) and adding NPCs/props to the returned scene.
(function (global) {
  "use strict";

  const PROTOCOL_HEADER = { "X-GameRAG-Protocol": "1" };
  const PLAYER_SPEED = 6; // world units per second
  const SPRINT_MULTIPLIER = 1.8;
  const PROXIMITY_RADIUS = 4.5;
  const PLAYER_EYE_HEIGHT = 1.7;
  const JUMP_SPEED = 6.5; // initial upward velocity, world units per second
  const GRAVITY = 18; // world units per second^2

  // /ask is single-turn/memoryless -- it has no conversation-history field, so without
  // this, an archetype NPC (many crowd figures sharing one backend agent, prompted to
  // introduce itself differently per conversation) can contradict itself mid-conversation,
  // e.g. giving a different name on turn 2 than it gave on turn 1, since it never sees its
  // own earlier reply. transcript (an array of {role: "player"|"npc", text} for the
  // CURRENT conversation only) is folded into options.systemOverride so the model can see
  // what it already said. Verified against a real server that this field reaches the
  // model's prompt, but a soft "stay consistent with the conversation so far" framing was
  // NOT reliably followed by the small local model this demo targets (llama3.2:3b) --
  // direct, imperative phrasing that quotes the NPC's own most recent reply verbatim and
  // explicitly forbids picking a new name works reliably where the softer version didn't.
  async function askNpc(serverUrl, npcId, question, transcript) {
    const options = {};
    if (transcript && transcript.length > 0) {
      const lastNpcLine = [...transcript].reverse().find((turn) => turn.role === "npc");
      if (lastNpcLine) {
        options.systemOverride =
          `You already told the player this in your last reply: "${lastNpcLine.text}". ` +
          `You MUST stay consistent with that -- if it named you, use that exact same name ` +
          `again if asked; do not pick a different name or contradict what you already said.`;
      }
    }

    const response = await fetch(`${serverUrl}/ask`, {
      method: "POST",
      headers: Object.assign({ "Content-Type": "application/json" }, PROTOCOL_HEADER),
      body: JSON.stringify({ npc: npcId, question, options: Object.keys(options).length > 0 ? options : undefined })
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
      fogNear = 20,
      fogFar = 70,
      // Lighting is parameterized (not hardcoded white) so each demo scene can commit to
      // its own time-of-day/atmosphere -- e.g. warm low-angle dawn light for the small
      // North Gate courtyard vs. bright midday for the bustling town square vs. cool
      // moonlit ambient + warm point lights for the modern night metropolis -- instead of
      // every scene reading as the same place with different props.
      ambientColor = 0xffffff,
      ambientIntensity = 0.55,
      sunColor = 0xffffff,
      sunIntensity = 0.9,
      sunPosition = { x: 15, y: 25, z: 10 },
      spawnPosition = { x: 0, y: 0, z: 8 }
    } = options;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(skyColor);
    scene.fog = new THREE.Fog(fogColor, fogNear, fogFar);

    const camera = new THREE.PerspectiveCamera(70, canvas.clientWidth / canvas.clientHeight, 0.1, 500);
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);

    // Lighting: one directional "sun" plus soft ambient fill, enough to read shapes and
    // colors clearly without any texture/material setup. Color/intensity/position all
    // come from options above so each scene can set its own mood.
    scene.add(new THREE.AmbientLight(ambientColor, ambientIntensity));
    const sun = new THREE.DirectionalLight(sunColor, sunIntensity);
    sun.position.set(sunPosition.x, sunPosition.y, sunPosition.z);
    scene.add(sun);

    // Optional small warm/cool point lights (streetlamps, window glow, torches) a scene
    // can scatter around after createGame() -- kept as a thin helper here rather than
    // requiring every demo to reach into THREE directly.
    function addPointLight(config) {
      const light = new THREE.PointLight(
        config.color !== undefined ? config.color : 0xffffff,
        config.intensity !== undefined ? config.intensity : 1,
        config.distance !== undefined ? config.distance : 15,
        config.decay !== undefined ? config.decay : 2
      );
      light.position.set(config.position.x, config.position.y !== undefined ? config.position.y : 3, config.position.z);
      scene.add(light);
      return light;
    }

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
      // yaw: 0 faces -Z by default (matches both the movement trig and camera.rotateY
      // below), so scenes that place NPCs at negative Z relative to spawn -- as every demo
      // does -- have the player looking straight at them on load instead of away from them.
      yaw: spawnPosition.yaw ?? 0,
      pitch: 0,
      verticalVelocity: 0,
      grounded: true
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

    // Active THREE.AnimationMixers for every loaded NPC/building model that shipped baked
    // animation clips (see loadNpcModel below) -- ticked once per frame in tick() alongside
    // everything else. A model with no animations never gets an entry here at all, so the
    // update loop below is a no-op for it (no null-deref, no wasted work).
    const activeMixers = [];

    function addPlaceholderNpcMesh(group, config, bodyHeight) {
      // CylinderGeometry, not CapsuleGeometry -- the pinned three@0.128.0 CDN build (r128)
      // predates CapsuleGeometry (added in r142), so calling it here threw
      // "THREE.CapsuleGeometry is not a constructor" and aborted the whole scene script
      // before any NPC or building was ever added -- confirmed via a real browser console,
      // not just a curl/file-existence check.
      const body = new THREE.Mesh(
        new THREE.CylinderGeometry(0.4, 0.4, bodyHeight - 0.8, 12),
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

    // Hides child nodes/meshes whose name matches any of config.hideNodes (an array of
    // case-insensitive substrings, e.g. ["crossbow", "knife", "throwable"]) -- used to
    // strip a held weapon/prop mesh from a civilian-role NPC (tavern keeper, priest,
    // merchant) without needing a different source model. KayKit's Adventurers pack
    // attaches each held item as its own separately-named node under the hand bones
    // (e.g. "1H_Crossbow", "Knife_Offhand", "Spellbook") alongside the body meshes
    // ("Rogue_Body", "Rogue_Head", ...), so hiding just the matching nodes removes only
    // the weapon/prop, not the character's body, clothing, or skeleton -- the skinned
    // mesh keeps deforming normally under animation, the hidden node's bone still moves,
    // it's simply never rendered. Case-insensitive substring match, not a strict name
    // list, so it also catches offhand/two-handed variants ("1H_Crossbow"/"2H_Crossbow")
    // without listing every one individually.
    function hideMatchingNodes(root, hideNodes) {
      if (!Array.isArray(hideNodes) || hideNodes.length === 0) {
        return;
      }
      const patterns = hideNodes.map((s) => s.toLowerCase());
      root.traverse((obj) => {
        const name = (obj.name || "").toLowerCase();
        if (name && patterns.some((p) => name.includes(p))) {
          obj.visible = false;
        }
      });
    }

    // Picks which baked animation clip to play by default: prefers a clip whose name
    // looks like an idle or walk cycle (KayKit's Adventurers pack ships clips literally
    // named "Idle", "Unarmed_Idle", "Walking_A/B/C", etc.), falling back to the first
    // clip in the file if nothing matches so a model with unfamiliar clip names still
    // animates with *something* rather than staying in its static bind pose.
    function pickDefaultClip(clips) {
      const idle = clips.find((c) => /idle/i.test(c.name));
      if (idle) return idle;
      const walk = clips.find((c) => /walk/i.test(c.name));
      if (walk) return walk;
      return clips[0];
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
          // Kenney's kit models (mini-characters, castle-kit, city-kit-commercial) are
          // exported centered inside a normalized bounding cube rather than resting with
          // their base at y=0, so without this every loaded NPC would appear to float or
          // sink halfway into the ground relative to the placeholder it replaces.
          // config.modelYOffset (world units, applied after modelScale) corrects for that
          // per source pack.
          if (config.modelYOffset) {
            modelScene.position.y += config.modelYOffset;
          }

          hideMatchingNodes(modelScene, config.hideNodes);

          group.add(modelScene);

          // Animation playback: only for models that actually shipped baked clips (KayKit
          // Adventurers ships 76 per character -- combat/emote/locomotion clips -- but a
          // building or a future model with no `animations` array must keep rendering
          // exactly as a static mesh, no mixer created, nothing added to activeMixers, so
          // tick()'s mixer-update loop below has nothing to do for it and never touches a
          // null/undefined mixer).
          if (gltf.animations && gltf.animations.length > 0) {
            const mixer = new THREE.AnimationMixer(modelScene);
            const clip = pickDefaultClip(gltf.animations);
            mixer.clipAction(clip).play();
            activeMixers.push(mixer);
          }
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
            // See the matching comment in loadNpcModel: Kenney kit pieces are centered in
            // a normalized bounding cube, not resting on y=0, so this corrects the vertical
            // seat of the loaded model relative to the ground plane.
            if (config.modelYOffset) {
              modelScene.position.y += config.modelYOffset;
            }
            group.add(modelScene);
          },
          undefined,
          (error) => {
            console.warn(`[GameRagKit demo] Failed to load building model "${config.model}", keeping placeholder box.`, error);
          }
        );
      }

      // Composed structures: several small kit pieces (e.g. a castle-kit tower base + mid
      // + roof, or a wall + corner + gate arch) stacked/arranged into one bigger building,
      // instead of one single building.glb. Each entry is its own independent GLTFLoader
      // load with its own local position/rotation/scale/yOffset relative to this
      // building's group -- if one piece fails to load the others still render, so a
      // composed structure degrades piece-by-piece rather than all-or-nothing.
      if (Array.isArray(config.models) && typeof THREE.GLTFLoader === "function") {
        // Only remove the placeholder once at least one composed piece has actually
        // loaded successfully -- removing it eagerly (before any load result is known)
        // left an empty group with nothing in it at all whenever every piece failed to
        // load (e.g. a missing texture), instead of correctly falling back to the box.
        let placeholderRemoved = !!config.model;
        config.models.forEach((piece) => {
          const loader = new THREE.GLTFLoader();
          loader.load(
            piece.model,
            (gltf) => {
              if (!placeholderRemoved) {
                group.remove(placeholder);
                placeholderRemoved = true;
              }
              const modelScene = gltf.scene;
              const scale = piece.modelScale || 1;
              modelScene.scale.setScalar(scale);
              if (piece.modelYOffset) {
                modelScene.position.y += piece.modelYOffset;
              }
              if (piece.position) {
                modelScene.position.x += piece.position.x || 0;
                modelScene.position.y += piece.position.y || 0;
                modelScene.position.z += piece.position.z || 0;
              }
              if (piece.rotationY) {
                modelScene.rotation.y = piece.rotationY;
              }
              group.add(modelScene);
            },
            undefined,
            (error) => {
              console.warn(`[GameRagKit demo] Failed to load composed building piece "${piece.model}".`, error);
            }
          );
        });
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

    // Real navmesh-based street network, built from metropolis.html's regular city grid
    // (blockSize/streetWidth/cell/gridExtent) and consumed by the three-pathfinding
    // library (window.threePathfinding, loaded separately -- see ASSETS.md/metropolis.html)
    // instead of the old hand-rolled point-graph. The old graph placed one waypoint at
    // every block CENTER (k*cell, j*cell) -- i.e. literally on top of each building's own
    // footprint, since buildings are also centered at (bx*cell, bz*cell) -- and hopped
    // figures between them in a dead straight line with zero collision checking. That is
    // the root cause of figures clipping through buildings: the logical graph never
    // described real streets, it described building centers. This replacement instead
    // builds real ground-plane quad geometry along the actual street corridors (the gaps
    // between adjacent building footprints, which is where the pavement really is) and
    // the open plaza, and hands that geometry to three-pathfinding's Pathfinding.createZone
    // so it can triangulate/flood-fill it into a proper navmesh. A figure can only ever be
    // routed across quads that exist in this geometry, and no quad here ever overlaps a
    // building footprint (verified by construction below, and re-checked live in the
    // Puppeteer visual check), so route-following figures can't be routed through a
    // building at all -- not "less likely to," structurally cannot.
    //
    // Two independent zones are built, not one zone with two groups: "roads" (the
    // road-lane band down the center of every corridor, plus the full plaza square) for
    // vehicle-ish archetypes (bike-courier/delivery-courier), and "footpaths" (the
    // sidewalk strips on both sides of every corridor, plus a footpath ring around the
    // plaza) for pedestrian archetypes. Their quads never share a vertex, so even if they
    // had been merged into one zone three-pathfinding's connectivity flood-fill would
    // still have split them into separate groups -- using two zones instead just avoids
    // having to discover/guess which auto-assigned group index landed on which band.
    function buildStreetNavmeshGeometry(roadGrid) {
      const { cell, gridExtent, streetHalfWidth, footpathWidth, blockSize } = roadGrid;
      const streetWidth = streetHalfWidth * 2;
      const plazaHalf = blockSize / 2;
      // Extend every corridor a little past the outermost block's outer edge so the
      // navmesh reaches the far ends of the grid instead of stopping short at the last
      // intersection.
      const reach = gridExtent * cell + plazaHalf + streetWidth;

      const roadVerts = [];
      const roadIndices = [];
      const footpathVerts = [];
      const footpathIndices = [];

      function pushQuad(verts, indices, minX, maxX, minZ, maxZ) {
        const base = verts.length / 3;
        verts.push(minX, 0, minZ, maxX, 0, minZ, maxX, 0, maxZ, minX, 0, maxZ);
        // Two triangles, wound so the face normal points +Y (up) under three.js's
        // default counter-clockwise-is-front convention when viewed from +Y looking
        // down -- not load-bearing for three-pathfinding (it only reads position/index,
        // not normals), but keeps the geometry consistent if it's ever also rendered.
        indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
      }

      // One long quad per corridor axis, spanning the full grid reach, for every gap
      // between adjacent block columns/rows -- this "long strip" approach (rather than
      // one quad per intersection cell) keeps corridors fully connected end to end so
      // three-pathfinding's funnel/string-pull produces smooth diagonal-ish paths across
      // multiple intersections instead of forcing a stop at every single grid line.
      //
      // The corridor's own half-width (streetHalfWidth) is exactly the gap between a
      // block's center and the adjacent block's edge (cell/2 - blockSize/2, by how the
      // grid is laid out in metropolis.html) -- i.e. streetHalfWidth already reaches all
      // the way to the building edge, with zero room left over. Putting the footpath
      // OUTSIDE that (centerCorridor +/- streetHalfWidth +/- footpathWidth, as this used
      // to) put it directly inside the neighboring building, which a real headless-browser
      // check caught as ~1-2% of footpath figures clipping into buildings. The footpath
      // must instead eat into the existing corridor width (adjacent to the building edge),
      // with the remaining, narrower center strip left as the actual road lane.
      const footpathInnerHalf = Math.max(0, streetHalfWidth - footpathWidth);
      for (let k = -gridExtent; k < gridExtent; k++) {
        const corridorCenter = (k + 0.5) * cell;
        // Vertical corridor (runs along Z) at this X gap.
        pushQuad(roadVerts, roadIndices, corridorCenter - footpathInnerHalf, corridorCenter + footpathInnerHalf, -reach, reach);
        pushQuad(footpathVerts, footpathIndices, corridorCenter - streetHalfWidth, corridorCenter - footpathInnerHalf, -reach, reach);
        pushQuad(footpathVerts, footpathIndices, corridorCenter + footpathInnerHalf, corridorCenter + streetHalfWidth, -reach, reach);
        // Horizontal corridor (runs along X) at this Z gap.
        pushQuad(roadVerts, roadIndices, -reach, reach, corridorCenter - footpathInnerHalf, corridorCenter + footpathInnerHalf);
        pushQuad(footpathVerts, footpathIndices, -reach, reach, corridorCenter - streetHalfWidth, corridorCenter - footpathInnerHalf);
        pushQuad(footpathVerts, footpathIndices, -reach, reach, corridorCenter + footpathInnerHalf, corridorCenter + streetHalfWidth);
      }

      // The open (0,0) plaza block: a walkable road-band square in the middle (couriers
      // can cut through it) plus a footpath ring around its edge (pedestrians hug the
      // edge rather than walking through moving bike/courier traffic in the middle).
      pushQuad(roadVerts, roadIndices, -plazaHalf, plazaHalf, -plazaHalf, plazaHalf);
      const plazaOuter = plazaHalf + footpathWidth;
      pushQuad(footpathVerts, footpathIndices, -plazaOuter, plazaOuter, -plazaOuter, -plazaHalf);
      pushQuad(footpathVerts, footpathIndices, -plazaOuter, plazaOuter, plazaHalf, plazaOuter);
      pushQuad(footpathVerts, footpathIndices, -plazaOuter, -plazaHalf, -plazaHalf, plazaHalf);
      pushQuad(footpathVerts, footpathIndices, plazaHalf, plazaOuter, -plazaHalf, plazaHalf);

      function toGeometry(verts, indices) {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute("position", new THREE.Float32BufferAttribute(verts, 3));
        geometry.setIndex(indices);
        return geometry;
      }

      // Analytically finds the nearest point on the given band (road or footpath) to an
      // arbitrary (x,z) -- used to snap a figure's random open-ground spawn point onto
      // its band before ever asking three-pathfinding for a route. This matters because
      // Pathfinding.findPath's own getClosestNode call requires the QUERY point itself to
      // already sit inside a navmesh polygon (checkPolygon=true, hardcoded in the
      // library) -- a spawn point picked from the wide-open addCrowd bounds (most of
      // which isn't on any street at all) would make literally the figure's first path
      // request fail. This is plain corridor arithmetic (same centerlines/widths as
      // above), not a navmesh query, so it always returns *some* point regardless of
      // whether three-pathfinding itself is available.
      function nearestBandPoint(isFootpathBand, x, z) {
        // Matches the corrected corridor construction above: the footpath band is the
        // strip adjacent to the building edge (footpathInnerHalf..streetHalfWidth), and
        // the road band is the narrower center strip (0..footpathInnerHalf) -- footpath
        // no longer extends past streetHalfWidth into the neighboring building.
        const innerHalf = isFootpathBand ? footpathInnerHalf : 0;
        const outerHalf = isFootpathBand ? streetHalfWidth : footpathInnerHalf;
        // Clamps a signed offset from a corridor's centerline into the band -- for the
        // road band (innerHalf 0) that's simply [-outerHalf, outerHalf]; for a footpath
        // band (a strip on EITHER side of the road, not straddling the centerline) it's
        // whichever of [-outerHalf,-innerHalf] or [innerHalf,outerHalf] is nearer to the
        // actual offset, preserving which side of the street the point was already on.
        function clampOffset(offset) {
          if (offset >= 0) {
            return Math.max(innerHalf, Math.min(outerHalf, offset));
          }
          return -Math.max(innerHalf, Math.min(outerHalf, -offset));
        }

        // Candidate 1: clamp onto the nearest vertical corridor (constant-X band).
        const kX = Math.max(-gridExtent, Math.min(gridExtent - 1, Math.round(x / cell - 0.5)));
        const centerX = (kX + 0.5) * cell;
        const bandX = centerX + clampOffset(x - centerX);
        const clampedZ = Math.max(-reach, Math.min(reach, z));
        const candidateVertical = { x: bandX, z: clampedZ, dist: Math.hypot(x - bandX, z - clampedZ) };

        // Candidate 2: clamp onto the nearest horizontal corridor (constant-Z band).
        const kZ = Math.max(-gridExtent, Math.min(gridExtent - 1, Math.round(z / cell - 0.5)));
        const centerZ = (kZ + 0.5) * cell;
        const bandZ = centerZ + clampOffset(z - centerZ);
        const clampedX = Math.max(-reach, Math.min(reach, x));
        const candidateHorizontal = { x: clampedX, z: bandZ, dist: Math.hypot(x - clampedX, z - bandZ) };

        // Candidate 3: clamp onto the plaza band (square ring for footpath, filled
        // square for road).
        const plazaOuterHalf = isFootpathBand ? plazaHalf + footpathWidth : plazaHalf;
        const plazaClampedX = Math.max(-plazaOuterHalf, Math.min(plazaOuterHalf, x));
        const plazaClampedZ = Math.max(-plazaOuterHalf, Math.min(plazaOuterHalf, z));
        let plazaX = plazaClampedX;
        let plazaZ = plazaClampedZ;
        if (isFootpathBand) {
          // Push the point onto the ring itself (outside the inner plazaHalf square)
          // rather than letting it land in the plaza's road-band interior.
          const withinInnerX = Math.abs(plazaClampedX) < plazaHalf;
          const withinInnerZ = Math.abs(plazaClampedZ) < plazaHalf;
          if (withinInnerX && withinInnerZ) {
            // Push out along whichever axis is closer to the inner edge.
            if (plazaHalf - Math.abs(plazaClampedX) < plazaHalf - Math.abs(plazaClampedZ)) {
              plazaX = plazaClampedX < 0 ? -plazaHalf : plazaHalf;
            } else {
              plazaZ = plazaClampedZ < 0 ? -plazaHalf : plazaHalf;
            }
          }
        }
        const candidatePlaza = { x: plazaX, z: plazaZ, dist: Math.hypot(x - plazaX, z - plazaZ) };

        let best = candidateVertical;
        if (candidateHorizontal.dist < best.dist) best = candidateHorizontal;
        if (candidatePlaza.dist < best.dist) best = candidatePlaza;
        return { x: best.x, z: best.z };
      }

      return {
        roadGeometry: toGeometry(roadVerts, roadIndices),
        footpathGeometry: toGeometry(footpathVerts, footpathIndices),
        nearestRoadPoint: (x, z) => nearestBandPoint(false, x, z),
        nearestFootpathPoint: (x, z) => nearestBandPoint(true, x, z),
        reach
      };
    }

    // Thin wrapper around the three-pathfinding library (window.threePathfinding, a UMD
    // global -- see metropolis.html's <script> tag): builds the two navmesh zones above
    // once per addCrowd() call and exposes just the handful of operations updateCrowd
    // actually needs (request a path between two points on a given band, or a random
    // reachable point). Returns null if the library script didn't load for any reason
    // (e.g. offline dev, CDN hiccup) so addCrowd can fall back cleanly instead of
    // throwing and aborting the whole scene script.
    function buildStreetPathfinder(roadGrid) {
      if (typeof global.threePathfinding === "undefined" || !global.threePathfinding.Pathfinding) {
        console.warn("[GameRagKit demo] three-pathfinding did not load; road-aware crowd behaviors will stay put instead of pathing.");
        return null;
      }
      const { Pathfinding } = global.threePathfinding;
      const { roadGeometry, footpathGeometry, nearestRoadPoint, nearestFootpathPoint, reach } = buildStreetNavmeshGeometry(roadGrid);

      const pathfinding = new Pathfinding();
      const ROAD_ZONE = "roads";
      const FOOTPATH_ZONE = "footpaths";
      pathfinding.setZoneData(ROAD_ZONE, Pathfinding.createZone(roadGeometry));
      pathfinding.setZoneData(FOOTPATH_ZONE, Pathfinding.createZone(footpathGeometry));

      // findPath needs a groupID -- the index of the connected-component group a given
      // point falls in within its zone. Corridors here are all one connected mesh, but
      // getGroup is still called per-point (not hardcoded to 0) in case a future grid
      // shape (e.g. a disconnected dead-end block) ever splits a zone into more than one
      // group; a point whose nearest node is in group 3 must request a path in group 3,
      // not group 0.
      //
      // fromX/fromZ/toX/toZ are snapped onto the band (nearestPoint) before being handed
      // to three-pathfinding at all: Pathfinding.findPath's own getClosestNode lookups use
      // checkPolygon=true (hardcoded inside the library, confirmed by reading
      // Pathfinding.js's findPath source), which requires the QUERY point itself to
      // already sit inside a navmesh polygon -- passing a figure's raw open-ground spawn
      // position straight through would make its very first path request fail every
      // time, since most of the open bounds isn't on a street at all.
      function findPathOnZone(zoneId, nearestPoint, fromX, fromZ, toX, toZ) {
        const snappedFrom = nearestPoint(fromX, fromZ);
        const snappedTo = nearestPoint(toX, toZ);
        const from = new THREE.Vector3(snappedFrom.x, 0, snappedFrom.z);
        const to = new THREE.Vector3(snappedTo.x, 0, snappedTo.z);
        const group = pathfinding.getGroup(zoneId, from);
        if (group === null) return null;
        const path = pathfinding.findPath(from, to, zoneId, group);
        return path && path.length > 0 ? path : null;
      }

      function randomPointOnZone(zoneId, nearestPoint, nearX, nearZ) {
        const snapped = nearestPoint(nearX, nearZ);
        const group = pathfinding.getGroup(zoneId, new THREE.Vector3(snapped.x, 0, snapped.z));
        if (group === null) return null;
        // nearRange 0 (falsy) means "any node in the group," i.e. anywhere reachable on
        // that band, not just nearby -- matches the old random-route behavior of picking
        // any other waypoint on the grid, not just an adjacent one.
        const node = pathfinding.getRandomNode(zoneId, group, null, 0);
        return node || null;
      }

      return {
        findRoadPath: (fromX, fromZ, toX, toZ) => findPathOnZone(ROAD_ZONE, nearestRoadPoint, fromX, fromZ, toX, toZ),
        findFootpathPath: (fromX, fromZ, toX, toZ) => findPathOnZone(FOOTPATH_ZONE, nearestFootpathPoint, fromX, fromZ, toX, toZ),
        randomRoadPoint: (nearX, nearZ) => randomPointOnZone(ROAD_ZONE, nearestRoadPoint, nearX, nearZ),
        randomFootpathPoint: (nearX, nearZ) => randomPointOnZone(FOOTPATH_ZONE, nearestFootpathPoint, nearX, nearZ),
        nearestRoadPoint,
        nearestFootpathPoint,
        reach
      };
    }

    // Stateful per-figure path follower built once at spawn (see addCrowd below), wrapping
    // a streetPathfinder for one figure's behavior. Replaces the old per-frame waypoint-hop
    // logic in updateCrowd: a figure now walks a real multi-point navmesh path (already
    // smoothed/string-pulled by three-pathfinding, so it isn't confined to axis-aligned
    // hops the way the old point-graph was) rather than one grid edge at a time.
    //  - "fixed-route" computes its path ONCE here at spawn and re-walks the exact same
    //    point list forever, restarting from index 0 every time it finishes -- this is
    //    what makes a fixed route fixed rather than re-picked.
    //  - "random-route"/"footpath" request a fresh path to a new random reachable point
    //    (on the road or footpath band respectively) each time the previous path
    //    completes, so the route varies trip to trip but always stays on that band.
    // step(dt) advances along the current path segment at `speed` units/sec and returns
    // { x, z, heading, arrived } for updateCrowd to apply -- heading is always
    // Math.atan2 of the vector to the NEXT waypoint on the actual path being walked, so it
    // can never desync from the real movement direction the way a separately-tracked
    // heading could. If pathfinding ever fails to find a route (e.g. a transient navmesh
    // query issue) step() just holds position for that tick and retries next tick, rather
    // than throwing or teleporting.
    function buildPathFollower(streetPathfinder, behavior, startX, startZ) {
      const isFixed = behavior === "fixed-route";
      const isFootpath = behavior === "footpath";
      const findPath = isFootpath ? streetPathfinder.findFootpathPath : streetPathfinder.findRoadPath;
      const randomPoint = isFootpath ? streetPathfinder.randomFootpathPoint : streetPathfinder.randomRoadPoint;

      let path = null;
      let pathIndex = 0;
      let fixedPath = null; // cached once for fixed-route, re-walked forever

      function requestNewPath(fromX, fromZ) {
        const target = randomPoint(fromX, fromZ);
        if (!target) return null;
        const found = findPath(fromX, fromZ, target.x, target.z);
        return found;
      }

      function ensurePath(fromX, fromZ) {
        if (isFixed) {
          if (!fixedPath) {
            fixedPath = requestNewPath(fromX, fromZ);
          }
          return fixedPath;
        }
        return requestNewPath(fromX, fromZ);
      }

      if (isFixed) {
        fixedPath = requestNewPath(startX, startZ);
      }
      path = isFixed ? fixedPath : null;

      return {
        step(dt, x, z, speed) {
          if (!path || pathIndex >= path.length) {
            const fresh = ensurePath(x, z);
            if (!fresh || fresh.length === 0) {
              // No reachable path found (e.g. isolated navmesh island) -- hold position
              // rather than getting stuck retrying every single frame forever; a later
              // tick will naturally try again since path stays null.
              return { x, z, heading: null, arrived: false };
            }
            path = isFixed ? fixedPath : fresh;
            pathIndex = 0;
          }

          const waypoint = path[pathIndex];
          const dx = waypoint.x - x;
          const dz = waypoint.z - z;
          const dist = Math.hypot(dx, dz);
          const heading = Math.atan2(dx, dz);

          if (dist < 0.25) {
            pathIndex += 1;
            if (pathIndex >= path.length) {
              if (isFixed) {
                pathIndex = 0; // repeat the exact same cached path from its start
              } else {
                path = null; // next step() call will request a fresh random path
              }
            }
            return { x, z, heading, arrived: true };
          }

          const travel = Math.min(dist, speed * dt);
          return {
            x: x + Math.sin(heading) * travel,
            z: z + Math.cos(heading) * travel,
            heading,
            arrived: false
          };
        }
      };
    }

    function addCrowd(config) {
      const count = config.count || 200;
      const bounds = config.bounds || { minX: -groundSize / 2 + 2, maxX: groundSize / 2 - 2, minZ: -groundSize / 2 + 2, maxZ: groundSize / 2 - 2 };
      const colors = config.colors || [0xd4453a, 0xd4a53a, 0x3ad46a, 0x3a8ad4, 0xd43ac2, 0xe8e8e8];
      const archetypes = config.archetypes && config.archetypes.length > 0
        ? config.archetypes
        : [{ id: "passerby", label: "Passerby" }];
      // Optional street navmesh for road-aware movement (fixed-route/random-route
      // couriers, footpath-only pedestrians), built and queried through three-pathfinding
      // (see buildStreetPathfinder above). Without it, every behavior falls back to the
      // original open-bounds wander/patrol/stationary logic -- e.g. single-npc.html and
      // city.html don't call addCrowd at all, and any metropolis-style scene that doesn't
      // pass roadGrid keeps working exactly as before. If the pathfinding library script
      // failed to load, buildStreetPathfinder returns null and the same fallback applies.
      const streetPathfinder = config.roadGrid ? buildStreetPathfinder(config.roadGrid) : null;

      const torsoGeometry = new THREE.BoxGeometry(0.5, 0.7, 0.3);
      const headGeometry = new THREE.BoxGeometry(0.35, 0.35, 0.35);

      const torsoMesh = new THREE.InstancedMesh(torsoGeometry, new THREE.MeshStandardMaterial(), count);
      const headMesh = new THREE.InstancedMesh(headGeometry, new THREE.MeshStandardMaterial({ color: 0xf0c8a0 }), count);
      torsoMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      headMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);

      // Per-archetype props (config.archetypes[i].prop): "stall" gives a stationary
      // vendor/barista/shop-owner an actual stand to be standing behind instead of open
      // pavement; "bike" gives a bike/delivery courier something to ride. Both are
      // rendered as their own InstancedMesh pools (not one instance per figure in the
      // main crowd meshes) so archetypes needing a prop still cost only 1-2 extra draw
      // calls total, not one per figure -- figures without a prop for their archetype get
      // their pool slot scaled to 0 so it's simply invisible, cheaper than branching mesh
      // membership per figure at runtime.
      const stallGeometry = new THREE.BoxGeometry(0.9, 0.6, 0.6);
      const stallAwningGeometry = new THREE.BoxGeometry(1.1, 0.08, 0.7);
      const stallMesh = new THREE.InstancedMesh(stallGeometry, new THREE.MeshStandardMaterial({ color: 0x8a5a3a }), count);
      const stallAwningMesh = new THREE.InstancedMesh(stallAwningGeometry, new THREE.MeshStandardMaterial({ color: 0xd4453a }), count);
      stallMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      stallAwningMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);

      // Bike prop geometry: previously read as a "self-balancing scooter" because the two
      // wheel instances were flat horizontal discs (rotation.set(Math.PI/2, heading, 0)
      // tips the cylinder to lie flat face-up, THEN spins that flat disc around Y by
      // heading -- a horizontal spinning platform at wheel height, not a wheel at all).
      // Verified this really was the old behavior by reading the exact matrix composition
      // in the old renderCrowdInstance/updateCrowd code before touching it, rather than
      // assuming. Fixed by keeping the wheel geometry's own cylinder axis ALWAYS
      // horizontal and perpendicular to the direction of travel (rotate it 90 degrees
      // around its LOCAL Z once, baked into the geometry itself below, not into the
      // per-frame instance transform), so applying only a Y-axis heading rotation on top
      // (see renderBikeInstance below) turns the whole upright wheel to face the bike's
      // heading without ever flattening it into a disc. bikeFrameGeometry runs along
      // local Z (forward/back, i.e. the direction of travel at heading 0 -- see the
      // dummy.rotation convention used everywhere else in this file), connecting the two
      // wheels front-to-back like a real frame instead of a flat plank.
      const bikeFrameGeometry = new THREE.BoxGeometry(0.1, 0.55, 0.95);
      bikeFrameGeometry.translate(0, 0.2, 0); // frame sits above axle height, not centered on it
      const bikeWheelGeometry = new THREE.CylinderGeometry(0.3, 0.3, 0.055, 16);
      bikeWheelGeometry.rotateZ(Math.PI / 2); // bake "axis horizontal, perpendicular to travel" in once
      const bikeMesh = new THREE.InstancedMesh(bikeFrameGeometry, new THREE.MeshStandardMaterial({ color: 0x2a2a2a }), count);
      const bikeWheelMesh = new THREE.InstancedMesh(bikeWheelGeometry, new THREE.MeshStandardMaterial({ color: 0x141414 }), count * 2);
      bikeMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      bikeWheelMesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);

      // General prop-kit system for every OTHER archetype prop beyond stall/bike (which
      // stay as their own hand-tuned meshes above): each kit is a fixed list of parts
      // (box/cylinder/cone primitives with a size, color, and local offset/rotation), so
      // a vendor's food cart, a guard's booth, a courier's delivery basket etc. can each
      // be genuinely different shapes instead of every stationary archetype sharing one
      // generic stand. Parts are pooled into one InstancedMesh per (shape, size, color)
      // combination -- most kits are small (2-5 parts) and stationary, so this is a
      // handful of extra draw calls total, not one per figure, same principle as
      // stall/bike above. A figure's prop is placed once at spawn (kits attached to
      // stationary/patrol archetypes never move again after that) except for
      // delivery-courier's basket, which rides along with its bike every frame.
      const PROP_KITS = {
        "food-cart": [
          { shape: "box", size: [0.9, 0.5, 0.55], color: 0x8a5a3a, offset: [0, 0.25, 0] },
          { shape: "cylinder", size: [0.04, 0.04, 0.9], color: 0x3a3a3a, offset: [0, 0.9, 0] },
          { shape: "cone", size: [0.55, 0.25], color: 0xd4453a, offset: [0, 1.35, 0] }
        ],
        "newsstand": [
          { shape: "box", size: [0.7, 1.1, 0.5], color: 0x3a5a8a, offset: [0, 0.55, 0] },
          { shape: "box", size: [0.9, 0.06, 0.65], color: 0x2a2a2a, offset: [0, 1.15, 0] }
        ],
        "coffee-cart": [
          { shape: "box", size: [0.7, 0.55, 0.5], color: 0x5a4530, offset: [0, 0.275, 0] },
          { shape: "cylinder", size: [0.14, 0.1, 0.22], color: 0xe8e8e8, offset: [0, 0.66, 0] }
        ],
        "shop-sign": [
          { shape: "cylinder", size: [0.05, 0.05, 1.6], color: 0x3a3a3a, offset: [0, 0.8, 0] },
          { shape: "box", size: [0.7, 0.4, 0.06], color: 0xd4a53a, offset: [0, 1.35, 0.2] }
        ],
        "food-truck": [
          { shape: "box", size: [1.6, 0.9, 0.8], color: 0xe8e8e8, offset: [0, 0.55, 0] },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [-0.55, 0.18, 0.35], rotationAxis: "z" },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [0.55, 0.18, 0.35], rotationAxis: "z" }
        ],
        "instrument-case": [
          { shape: "box", size: [0.9, 0.06, 0.35], color: 0x2a1f1a, offset: [0, 0.03, 0.45] }
        ],
        "guard-booth": [
          { shape: "box", size: [0.7, 1.3, 0.6], color: 0x2a3a4a, offset: [0, 0.65, 0] },
          { shape: "cone", size: [0.5, 0.35], color: 0x1a2a4a, offset: [0, 1.5, 0] }
        ],
        "maintenance-cart": [
          { shape: "box", size: [0.6, 0.5, 0.5], color: 0xd4c23a, offset: [-0.5, 0.25, 0] },
          { shape: "cone", size: [0.16, 0.35], color: 0xd4622a, offset: [0.4, 0.175, 0.2] },
          { shape: "cone", size: [0.16, 0.35], color: 0xd4622a, offset: [0.4, 0.175, -0.2] }
        ],
        "delivery-basket": [
          { shape: "box", size: [0.45, 0.35, 0.35], color: 0x5a3a2a, offset: [0, 0.55, -0.55] }
        ],
        "taxi-stand": [
          { shape: "cylinder", size: [0.05, 0.05, 1.5], color: 0x3a3a3a, offset: [-1.4, 0.75, 0] },
          { shape: "box", size: [0.5, 0.35, 0.06], color: 0xe8c840, offset: [-1.4, 1.35, 0] },
          { shape: "box", size: [1.6, 0.55, 0.85], color: 0xe8c840, offset: [0.4, 0.35, 0] },
          { shape: "box", size: [1.0, 0.35, 0.8], color: 0xe8c840, offset: [0.4, 0.75, 0] },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [-0.2, 0.18, 0.4], rotationAxis: "z" },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [-0.2, 0.18, -0.4], rotationAxis: "z" },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [1.0, 0.18, 0.4], rotationAxis: "z" },
          { shape: "cylinder", size: [0.18, 0.18, 0.1], color: 0x1a1a1a, offset: [1.0, 0.18, -0.4], rotationAxis: "z" }
        ],
        "backpack": [
          { shape: "box", size: [0.28, 0.35, 0.16], color: 0x3a5a3a, offset: [0, 0.45, -0.24] }
        ],
        "bench": [
          { shape: "box", size: [1.1, 0.06, 0.4], color: 0x5a4530, offset: [0, 0.42, 0.55] },
          { shape: "box", size: [0.06, 0.42, 0.06], color: 0x2a2a2a, offset: [-0.45, 0.21, 0.55] },
          { shape: "box", size: [0.06, 0.42, 0.06], color: 0x2a2a2a, offset: [0.45, 0.21, 0.55] }
        ],
        "cane": [
          { shape: "cylinder", size: [0.025, 0.025, 0.75], color: 0x5a3a2a, offset: [0.32, 0.375, 0.1], rotationAxis: "x", rotationAmount: 0.35 }
        ]
      };

      // One InstancedMesh pool per distinct (shape, size, color) part across every kit --
      // parts get reused across kits automatically when their (shape,size,color) matches
      // (e.g. the two 0.18-radius black wheel cylinders in food-truck and taxi-stand share
      // one pool). Every part in every kit gets a reserved slot per figure (propPoolIndex,
      // assigned below) regardless of whether that figure's archetype actually uses that
      // part, the same "reserve for all, scale-to-zero when unused" approach stall/bike
      // already use, so a kit never needs its own separate count bookkeeping.
      // key -> { mesh, slotsPerFigure }. slotsPerFigure matters because a single kit can
      // use the SAME (shape,size,color) part more than once -- e.g. taxi-stand has 4
      // identical wheel cylinders, food-truck has 2 -- and InstancedMesh.setMatrixAt(i, ...)
      // for the same index i overwrites whatever was there before, so every occurrence of
      // a shared part within one kit needs its OWN reserved slot, not all four wheels
      // fighting over the one slot index a figure would otherwise get. slotsPerFigure is
      // the max times this pool's key appears in any single kit's part list; each part
      // instance is pre-assigned a fixed occurrence index (0, 1, 2...) at kit-definition
      // time below, and a figure's slot for that part is
      // figureIndex * slotsPerFigure + occurrenceIndex -- deterministic and collision-free.
      const propPools = new Map();
      function poolKeyFor(part) {
        return `${part.shape}:${part.size.join(",")}:${part.color}`;
      }
      // First pass: count max occurrences of each key across all kits, and assign each
      // part object its own occurrenceIndex (mutates the part objects in PROP_KITS --
      // fine, they're this module's own private data, never read before this runs).
      const keyOccurrenceCounts = new Map();
      Object.values(PROP_KITS).forEach((parts) => {
        const seenInThisKit = new Map();
        parts.forEach((part) => {
          const key = poolKeyFor(part);
          const occurrenceIndex = seenInThisKit.get(key) || 0;
          part.occurrenceIndex = occurrenceIndex;
          seenInThisKit.set(key, occurrenceIndex + 1);
          keyOccurrenceCounts.set(key, Math.max(keyOccurrenceCounts.get(key) || 0, occurrenceIndex + 1));
        });
      });

      function getOrCreatePropPool(part) {
        const key = poolKeyFor(part);
        if (propPools.has(key)) return propPools.get(key);
        let geometry;
        if (part.shape === "box") {
          geometry = new THREE.BoxGeometry(part.size[0], part.size[1], part.size[2]);
        } else if (part.shape === "cylinder") {
          geometry = new THREE.CylinderGeometry(part.size[0], part.size[1], part.size[2], 12);
        } else {
          geometry = new THREE.ConeGeometry(part.size[0], part.size[1], 12);
        }
        const slotsPerFigure = keyOccurrenceCounts.get(key) || 1;
        const mesh = new THREE.InstancedMesh(geometry, new THREE.MeshStandardMaterial({ color: part.color }), count * slotsPerFigure);
        mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        const entry = { mesh, slotsPerFigure };
        propPools.set(key, entry);
        return entry;
      }
      // Pre-create a pool for every part used by every kit, up front, so every figure's
      // slot exists before any figure is placed (mirrors the stall/bike zero-scale-init
      // pattern below).
      Object.values(PROP_KITS).forEach((parts) => parts.forEach((part) => getOrCreatePropPool(part)));

      const dummy = new THREE.Object3D();
      const zeroScaleDummy = new THREE.Object3D();
      zeroScaleDummy.scale.set(0, 0, 0);
      zeroScaleDummy.updateMatrix();
      for (let i = 0; i < count; i++) {
        stallMesh.setMatrixAt(i, zeroScaleDummy.matrix);
        stallAwningMesh.setMatrixAt(i, zeroScaleDummy.matrix);
        bikeMesh.setMatrixAt(i, zeroScaleDummy.matrix);
        bikeWheelMesh.setMatrixAt(i * 2, zeroScaleDummy.matrix);
        bikeWheelMesh.setMatrixAt(i * 2 + 1, zeroScaleDummy.matrix);
      }
      propPools.forEach(({ mesh, slotsPerFigure }) => {
        for (let i = 0; i < count * slotsPerFigure; i++) {
          mesh.setMatrixAt(i, zeroScaleDummy.matrix);
        }
      });

      // Places every part of a figure's prop kit relative to (x, z, heading) -- used both
      // at spawn (for stationary/patrol kits, set once) and per-frame for kits attached to
      // a moving figure (currently just delivery-courier's basket, riding its bike).
      // Offsets are in the figure's LOCAL frame (x = right, z = forward, matching this
      // file's heading convention elsewhere) and rotated into world space by heading.
      function placePropKit(kitName, index, x, z, heading) {
        const parts = PROP_KITS[kitName];
        if (!parts) return;
        const sinH = Math.sin(heading);
        const cosH = Math.cos(heading);
        for (const part of parts) {
          const pool = getOrCreatePropPool(part);
          const slot = index * pool.slotsPerFigure + part.occurrenceIndex;
          const [ox, oy, oz] = part.offset;
          const worldX = x + ox * cosH + oz * sinH;
          const worldZ = z - ox * sinH + oz * cosH;
          dummy.position.set(worldX, oy, worldZ);
          dummy.rotation.set(0, 0, 0);
          if (part.rotationAxis === "z") {
            dummy.rotation.z = Math.PI / 2;
            dummy.rotation.y = heading;
          } else if (part.rotationAxis === "x") {
            dummy.rotation.y = heading;
            dummy.rotation.x = part.rotationAmount || 0;
          } else {
            dummy.rotation.y = heading;
          }
          dummy.updateMatrix();
          pool.mesh.setMatrixAt(slot, dummy.matrix);
          pool.mesh.instanceMatrix.needsUpdate = true;
        }
      }

      const figures = [];

      for (let i = 0; i < count; i++) {
        // Reject-sample so figures never spawn inside a building's footprint -- with
        // buildings covering most of a city grid, a plain random spawn put a large
        // fraction of figures inside obstacles, where they'd get stuck oscillating in
        // place forever (see the collision-bounce comment in updateCrowd below). 30
        // attempts is comfortably enough even when buildings cover most of the bounds;
        // the last attempt is used regardless so a pathological config can't infinite-loop.
        let x = 0;
        let z = 0;
        for (let attempt = 0; attempt < 30; attempt++) {
          x = bounds.minX + Math.random() * (bounds.maxX - bounds.minX);
          z = bounds.minZ + Math.random() * (bounds.maxZ - bounds.minZ);
          if (!collidesWithObstacle(x, z)) {
            break;
          }
        }
        const heading = Math.random() * Math.PI * 2;
        const color = colors[i % colors.length];
        const archetype = archetypes[i % archetypes.length];
        // "stationary" (vendors/baristas/shop owners tied to a stall) wanders within a
        // small radius of its stall instead of walking the map; "patrol" (guards/
        // maintenance) walks a short back-and-forth line; "fixed-route" (bike couriers)
        // follows the same repeating navmesh path every time -- computed once at spawn
        // (see buildPathFollower below) and re-walked identically forever, never re-routed;
        // "random-route" (other couriers) requests a fresh path to a new random reachable
        // point each time the previous one completes, always via the road navmesh, never
        // cutting across open ground or through buildings; "footpath" (ordinary
        // pedestrians) does the same but constrained to the footpath navmesh band instead
        // of the road band; "wander" (tourists, unlisted archetypes) is the original
        // free-roam-anywhere behavior. Config-driven per archetype, not per figure, so a
        // scene declares this once per profession, not per crowd instance.
        const behavior = archetype.behavior || "wander";
        const patrolAxis = Math.random() < 0.5 ? "x" : "z";
        const patrolRange = 2 + Math.random() * 2;
        const prop = archetype.prop || null;
        const territoryRadius = archetype.territoryRadius ?? 1.4;
        // Speed by role, not one shared random range for everyone: an adult walking pace
        // is roughly 1.2-1.6 world units/sec, a bike/courier considerably faster at
        // 3-4 units/sec -- tying speed to prop (bike vs not) rather than to behavior
        // directly means a future bike-riding "footpath" archetype would still move like
        // a bike, and a future non-bike "fixed-route" archetype would still walk, without
        // needing a third axis of configuration.
        const speed = prop === "bike" ? 3 + Math.random() * 1 : 1.2 + Math.random() * 0.4;

        // Road/footpath-band figures spawn snapped onto their band, not at their raw
        // open-ground reject-sampled (x,z) -- most of the open bounds isn't on a street at
        // all, so leaving a courier/pedestrian at its raw spawn point would both render it
        // standing in the middle of a plaza/off-street gap AND make its very first
        // pathfinding request fail (see the checkPolygon note on findPathOnZone above).
        // "stationary" (vendors/baristas/shop-owners/street-musician) is snapped onto the
        // footpath band too, even though it never calls findPath/gets a pathFollower --
        // without this it spawned anywhere not inside a building, including the middle of
        // the road, which is exactly the "shops are on the road" bug a real user report
        // caught: a shop belongs on the sidewalk in front of a building, not blocking a
        // bike lane. spawnFootpathBound is deliberately a separate flag from
        // needsPathFollower so a stationary figure gets the spawn snap without also
        // getting an unused, never-invoked pathFollower built for it.
        const isRoadBound = behavior === "fixed-route" || behavior === "random-route";
        const spawnFootpathBound = behavior === "footpath" || behavior === "stationary";
        const needsPathFollower = isRoadBound || behavior === "footpath";
        if (streetPathfinder && isRoadBound) {
          const snapped = streetPathfinder.nearestRoadPoint(x, z);
          x = snapped.x;
          z = snapped.z;
        } else if (streetPathfinder && spawnFootpathBound) {
          const snapped = streetPathfinder.nearestFootpathPoint(x, z);
          x = snapped.x;
          z = snapped.z;
        }

        const pathFollower = streetPathfinder && needsPathFollower
          ? buildPathFollower(streetPathfinder, behavior, x, z)
          : null;

        if (prop === "stall") {
          // Stationary figures don't move (only a tiny idle sway), so the stall/awning
          // transform is set once here rather than every frame in updateCrowd.
          dummy.position.set(x, 0.3, z);
          dummy.rotation.set(0, heading, 0);
          dummy.updateMatrix();
          stallMesh.setMatrixAt(i, dummy.matrix);

          dummy.position.set(x, 0.64, z);
          dummy.updateMatrix();
          stallAwningMesh.setMatrixAt(i, dummy.matrix);
        }

        const propKit = archetype.propKit || null;
        if (propKit && !isRoadBound) {
          // Every current propKit archetype is stationary or patrol (never fixed-route/
          // random-route), so its kit is placed once here and never moves again --
          // delivery-courier's basket is the one exception, handled per-frame in
          // updateCrowd instead since it rides along with a moving bike.
          placePropKit(propKit, i, x, z, heading);
        }

        figures.push({
          x, z, homeX: x, homeZ: z, heading, speed,
          wanderTimer: Math.random() * 4,
          behavior, patrolAxis, patrolRange, patrolDirection: 1,
          idlePhase: Math.random() * Math.PI * 2,
          prop, propKit,
          territoryRadius,
          pathFollower,
          npcId: archetype.id,
          label: archetype.label || archetype.id,
          greeting: archetype.greeting || "Oh, hey there.",
          inRange: false
        });

        torsoMesh.setColorAt(i, new THREE.Color(color));
      }

      scene.add(torsoMesh);
      scene.add(headMesh);
      scene.add(stallMesh);
      scene.add(stallAwningMesh);
      scene.add(bikeMesh);
      scene.add(bikeWheelMesh);
      stallMesh.instanceMatrix.needsUpdate = true;
      stallAwningMesh.instanceMatrix.needsUpdate = true;
      bikeMesh.instanceMatrix.needsUpdate = true;
      bikeWheelMesh.instanceMatrix.needsUpdate = true;
      propPools.forEach(({ mesh }) => scene.add(mesh));
      crowdInstances = { torsoMesh, headMesh, stallMesh, stallAwningMesh, bikeMesh, bikeWheelMesh, dummy, figures, bounds, count, placePropKit };
    }

    // Renders one figure's bike prop (frame + two wheels) into its InstancedMesh pool
    // slots. Shared by updateCrowd and renderCrowdInstance so the bike's per-frame
    // transform composition lives in exactly one place -- both callers set the same
    // frame/wheel matrices from the same figure.x/z/heading, so a fix here can't silently
    // drift out of sync between the two call sites the way duplicated inline code could.
    function renderBikeInstance(dummy, bikeMesh, bikeWheelMesh, index, x, z, heading) {
      dummy.position.set(x, 0.34, z);
      dummy.rotation.set(0, heading, 0);
      dummy.updateMatrix();
      bikeMesh.setMatrixAt(index, dummy.matrix);

      const wheelOffsetX = Math.sin(heading) * 0.44;
      const wheelOffsetZ = Math.cos(heading) * 0.44;
      // Only a heading (Y-axis) rotation is applied here -- the wheel geometry itself
      // already has its cylinder axis baked horizontal via a one-time rotateZ (see
      // bikeWheelGeometry in addCrowd), so this never recomposes the old X-then-Y Euler
      // pair that caused the flattened-disc "self-balancing scooter" bug.
      dummy.position.set(x + wheelOffsetX, 0.3, z + wheelOffsetZ);
      dummy.rotation.set(0, heading, 0);
      dummy.updateMatrix();
      bikeWheelMesh.setMatrixAt(index * 2, dummy.matrix);

      dummy.position.set(x - wheelOffsetX, 0.3, z - wheelOffsetZ);
      dummy.updateMatrix();
      bikeWheelMesh.setMatrixAt(index * 2 + 1, dummy.matrix);
    }

    function updateCrowd(dt) {
      if (!crowdInstances) return;

      const { torsoMesh, headMesh, bikeMesh, bikeWheelMesh, dummy, figures, bounds, placePropKit } = crowdInstances;

      for (let i = 0; i < figures.length; i++) {
        const f = figures[i];

        if (f.behavior === "stationary") {
          // Wanders within territoryRadius of its spawn point (its stall, or for a
          // stall-less archetype like a street musician, just its patch of footpath) --
          // a small sway/shuffle reads as idle activity (serving customers, playing an
          // instrument) rather than a frozen prop, without it drifting away from its spot.
          // Unchanged from before: this is already correct (a vendor should stay at their
          // stall) and wasn't one of the reported problems.
          f.idlePhase += dt * 2;
          const swayRadius = Math.min(f.territoryRadius, 0.5);
          f.x = f.homeX + Math.sin(f.idlePhase) * swayRadius;
          f.z = f.homeZ + Math.cos(f.idlePhase * 0.7) * swayRadius;
          f.heading += dt * 0.3;
        } else if (f.pathFollower) {
          // "fixed-route" (bike couriers), "random-route" (other couriers), and
          // "footpath" (pedestrians) all walk a real three-pathfinding navmesh path via
          // the same pathFollower.step() -- see buildPathFollower above for exactly how
          // each behavior differs in when it requests a new path. heading here always
          // comes directly from step()'s own atan2 of the vector to the next path
          // waypoint, i.e. the actual direction this frame's movement takes -- not a
          // separately-tracked value that could desync from real motion, which was the
          // root cause of the old "crab walk" bug (confirmed by reading the old
          // fixed-route/random-route code above: it happened to set heading correctly
          // there, but footpath's old back-and-forth branch set heading to a hardcoded
          // +/-90 degrees or 0/180 regardless of footpathOffset drift, and wander's
          // heading could lag a full bounce behind actual position on a collision frame).
          const result = f.pathFollower.step(dt, f.x, f.z, f.speed);
          f.x = result.x;
          f.z = result.z;
          if (result.heading !== null) {
            f.heading = result.heading;
          }
        } else if (f.behavior === "footpath" || (f.behavior === "fixed-route" || f.behavior === "random-route")) {
          // No streetPathfinder was available (three-pathfinding script failed to load,
          // or this scene didn't pass roadGrid) -- fall back to the same simple
          // back-and-forth-through-spawn-point movement "patrol" uses, rather than a
          // figure that never moves at all or (worse) silently reverts to the old
          // straight-through-buildings point-graph hopping.
          const axisPos = f.patrolAxis === "x" ? f.x - f.homeX : f.z - f.homeZ;
          if (axisPos >= f.patrolRange) {
            f.patrolDirection = -1;
          } else if (axisPos <= -f.patrolRange) {
            f.patrolDirection = 1;
          }
          const delta = f.patrolDirection * f.speed * dt;
          if (f.patrolAxis === "x") {
            f.x += delta;
            f.heading = f.patrolDirection > 0 ? Math.PI / 2 : -Math.PI / 2;
          } else {
            f.z += delta;
            f.heading = f.patrolDirection > 0 ? 0 : Math.PI;
          }
        } else if (f.behavior === "patrol") {
          // Walks a short back-and-forth line through its spawn point along one axis --
          // "left goes right, right goes left" -- instead of wandering the whole map.
          // Unlike fixed-route/random-route/footpath, patrol was deliberately kept on
          // this simple axis logic rather than migrated to the navmesh pathfinder (guards/
          // maintenance workers weren't part of the reported crab-walk/scooter/clipping
          // complaints), but a real headless-browser check still found ~1-2% of samples
          // landing inside a building -- a patrol spawn point near a building edge can
          // have its patrolRange extend into the building with nothing stopping it. This
          // collision check (same pattern as the old/current wander branch below) closes
          // that gap without pulling patrol onto the pathfinder.
          const axisPos = f.patrolAxis === "x" ? f.x - f.homeX : f.z - f.homeZ;
          if (axisPos >= f.patrolRange) {
            f.patrolDirection = -1;
          } else if (axisPos <= -f.patrolRange) {
            f.patrolDirection = 1;
          }
          const delta = f.patrolDirection * f.speed * dt;
          if (f.patrolAxis === "x") {
            const nextX = f.x + delta;
            if (collidesWithObstacle(nextX, f.z)) {
              f.patrolDirection = -f.patrolDirection;
            } else {
              f.x = nextX;
            }
            f.heading = f.patrolDirection > 0 ? Math.PI / 2 : -Math.PI / 2;
          } else {
            const nextZ = f.z + delta;
            if (collidesWithObstacle(f.x, nextZ)) {
              f.patrolDirection = -f.patrolDirection;
            } else {
              f.z = nextZ;
            }
            f.heading = f.patrolDirection > 0 ? 0 : Math.PI;
          }
        } else {
          // Free-roam wander (tourists/unlisted archetypes, or any archetype when no
          // streetPathfinder exists at all). Heading changes are deliberately infrequent
          // and small now -- a real pedestrian walks smoothly toward wherever it's headed
          // and only changes direction occasionally, it doesn't twitch its facing every
          // second or two. The old 1.5-4.5s timer with a +/-0.7 rad swing changed
          // direction noticeably more often and more sharply than an actual person would.
          f.wanderTimer -= dt;
          if (f.wanderTimer <= 0) {
            f.heading += (Math.random() - 0.5) * 0.8;
            f.wanderTimer = 3 + Math.random() * 4;
          }

          const nextX = f.x + Math.sin(f.heading) * f.speed * dt;
          const nextZ = f.z + Math.cos(f.heading) * f.speed * dt;

          // A pure axis reflection (heading = pi - heading / -heading) can wedge a figure
          // into a permanent back-and-forth loop against a corner or a border+building
          // combo, since reflecting twice in a row can return it to the exact heading it
          // started with. Adding a small random jitter on every bounce breaks that
          // symmetry so a stuck figure always eventually finds an open heading instead of
          // oscillating forever.
          if (nextX < bounds.minX || nextX > bounds.maxX || collidesWithObstacle(nextX, f.z)) {
            f.heading = Math.PI - f.heading + (Math.random() - 0.5) * 0.6;
          } else {
            f.x = nextX;
          }

          if (nextZ < bounds.minZ || nextZ > bounds.maxZ || collidesWithObstacle(f.x, nextZ)) {
            f.heading = -f.heading + (Math.random() - 0.5) * 0.6;
          } else {
            f.z = nextZ;
          }
        }

        dummy.position.set(f.x, 0.55, f.z);
        dummy.rotation.set(0, f.heading, 0);
        dummy.updateMatrix();
        torsoMesh.setMatrixAt(i, dummy.matrix);

        dummy.position.set(f.x, 1.05, f.z);
        dummy.updateMatrix();
        headMesh.setMatrixAt(i, dummy.matrix);

        if (f.prop === "bike") {
          renderBikeInstance(dummy, bikeMesh, bikeWheelMesh, i, f.x, f.z, f.heading);
          if (f.propKit) {
            // delivery-courier's basket: the only propKit attached to a MOVING figure, so
            // unlike every other kit (placed once at spawn and left alone), this one is
            // re-placed every frame to ride along with the bike.
            placePropKit(f.propKit, i, f.x, f.z, f.heading);
          }
        }
      }

      torsoMesh.instanceMatrix.needsUpdate = true;
      headMesh.instanceMatrix.needsUpdate = true;
      bikeMesh.instanceMatrix.needsUpdate = true;
      bikeWheelMesh.instanceMatrix.needsUpdate = true;
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
    let dialogueOpen = false;
    const listeners = { proximityChange: [] };

    function onProximityChange(callback) {
      listeners.proximityChange.push(callback);
    }

    // Called by the dialogue controller when a conversation opens/closes. While a
    // conversation is open the crowd is paused (see updateCrowd(dt) call below) and
    // proximity retargeting is skipped, so the NPC/figure the player is talking to can't
    // wander off or get silently swapped for a different one mid-conversation.
    function setDialogueOpen(isOpen) {
      dialogueOpen = isOpen;

      // Turn the figure being talked to toward the player so it reads as "paying
      // attention to you," not just a frozen prop mid-stride. Only crowd figures need
      // this explicitly -- addNpc() actors don't rotate to face the player at all today,
      // but they're static single models, not a wandering instanced crowd, so a fixed
      // facing direction already looks intentional rather than frozen.
      if (isOpen && closestNpcInRange && crowdInstances && crowdInstances.figures.includes(closestNpcInRange)) {
        const figure = closestNpcInRange;
        const dx = player.position.x - figure.x;
        const dz = player.position.z - figure.z;
        figure.heading = Math.atan2(dx, dz);
        renderCrowdInstance(figure, crowdInstances.figures.indexOf(figure));
      }
    }

    // Renders one crowd figure's current x/z/heading into its InstancedMesh slot,
    // without touching any other figure -- used to reflect an out-of-band change (like
    // snapping to face the player on dialogue open) immediately, since updateCrowd(dt)
    // itself is skipped entirely whenever dialogueOpen is true.
    function renderCrowdInstance(figure, index) {
      const { torsoMesh, headMesh, dummy } = crowdInstances;
      dummy.position.set(figure.x, 0.55, figure.z);
      dummy.rotation.set(0, figure.heading, 0);
      dummy.updateMatrix();
      torsoMesh.setMatrixAt(index, dummy.matrix);

      dummy.position.set(figure.x, 1.05, figure.z);
      dummy.updateMatrix();
      headMesh.setMatrixAt(index, dummy.matrix);

      torsoMesh.instanceMatrix.needsUpdate = true;
      headMesh.instanceMatrix.needsUpdate = true;

      if (figure.prop === "bike") {
        renderBikeInstance(dummy, crowdInstances.bikeMesh, crowdInstances.bikeWheelMesh, index, figure.x, figure.z, figure.heading);
        crowdInstances.bikeMesh.instanceMatrix.needsUpdate = true;
        crowdInstances.bikeWheelMesh.instanceMatrix.needsUpdate = true;
        if (figure.propKit) {
          crowdInstances.placePropKit(figure.propKit, index, figure.x, figure.z, figure.heading);
        }
      }
    }

    function tick() {
      const now = performance.now();
      const dt = Math.min(0.05, (now - lastTime) / 1000);
      lastTime = now;

      // Advance every active model animation (NPC idle/walk clips -- see loadNpcModel
      // above) by the same dt used for the player and crowd this frame, not a
      // separately-computed delta. activeMixers stays empty for a scene with no animated
      // models at all (e.g. a model with no baked clips, or before any model has finished
      // loading yet), so this loop is simply a no-op rather than something to special-case.
      for (let i = 0; i < activeMixers.length; i++) {
        activeMixers[i].update(dt);
      }

      // Movement, jumping, and look-direction changes are all suspended while a
      // conversation is open -- WASD/Space would otherwise both move the player and type
      // into the dialogue input box at the same time, since keydown isn't scoped to the
      // canvas.
      if (!dialogueOpen) {
        // Movement relative to look direction, flattened to the XZ plane. At yaw=0 the
        // camera looks down -Z (see camera.rotateY below), so forward here must also
        // point -Z at yaw=0 -- verified against the actual camera rotation, not assumed.
        const dirZ = -Math.cos(player.yaw);
        const dirX = -Math.sin(player.yaw);
        const fwd = new THREE.Vector3(dirX, 0, dirZ);
        // right = fwd x up (up = (0,1,0)), verified numerically: at fwd=(0,0,-1) (yaw=0)
        // this gives (1,0,0), the true camera-right direction, where "D"/strafe-right
        // should move the player. The previous (fwd.z, 0, -fwd.x) was the negation of
        // this -- A and D (and left/right arrow) were swapped for every yaw, confirmed by
        // the user pressing D and moving left.
        const right = new THREE.Vector3(-fwd.z, 0, fwd.x);

        let moveX = 0;
        let moveZ = 0;
        if (keysDown.has("KeyW") || keysDown.has("ArrowUp")) { moveX += fwd.x; moveZ += fwd.z; }
        if (keysDown.has("KeyS") || keysDown.has("ArrowDown")) { moveX -= fwd.x; moveZ -= fwd.z; }
        if (keysDown.has("KeyA") || keysDown.has("ArrowLeft")) { moveX -= right.x; moveZ -= right.z; }
        if (keysDown.has("KeyD") || keysDown.has("ArrowRight")) { moveX += right.x; moveZ += right.z; }

        const moveLen = Math.hypot(moveX, moveZ);
        if (moveLen > 0.0001) {
          const isSprinting = keysDown.has("ShiftLeft") || keysDown.has("ShiftRight");
          const speed = PLAYER_SPEED * (isSprinting ? SPRINT_MULTIPLIER : 1);
          moveX = (moveX / moveLen) * speed * dt;
          moveZ = (moveZ / moveLen) * speed * dt;

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

        if (keysDown.has("Space") && player.grounded) {
          player.verticalVelocity = JUMP_SPEED;
          player.grounded = false;
        }
      }

      // Gravity/vertical position update runs even while dialogue is open so a jump
      // already in progress finishes landing instead of freezing mid-air.
      player.verticalVelocity -= GRAVITY * dt;
      player.position.y += player.verticalVelocity * dt;
      if (player.position.y <= PLAYER_EYE_HEIGHT) {
        player.position.y = PLAYER_EYE_HEIGHT;
        player.verticalVelocity = 0;
        player.grounded = true;
      }

      camera.position.copy(player.position);
      camera.rotation.set(0, 0, 0);
      camera.rotateY(player.yaw);
      camera.rotateX(player.pitch);

      // Proximity check against every NPC actor plus every crowd figure; track the single
      // closest one in range so the "press E to talk" prompt only ever targets one at a
      // time. Crowd figures use the same { npcId, label, greeting, inRange } shape as
      // addNpc() actors so both can share this one proximity loop and the dialogue
      // controller doesn't need to know which kind of thing it's talking to. Skipped
      // entirely while a conversation is open so the active NPC/figure can't be silently
      // swapped out from under the player mid-conversation.
      let closest = closestNpcInRange;
      if (!dialogueOpen) {
        closest = null;
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
      }

      // Billboard every NPC actor's label sprite toward the camera (always, even during
      // dialogue, so labels don't visibly freeze at a stale angle).
      for (const npc of npcs) {
        npc.group.children.forEach((child) => {
          if (child.isSprite) {
            child.lookAt(camera.position);
          }
        });
      }

      if (!dialogueOpen) {
        updateCrowd(dt);
      }

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
      addPointLight,
      onProximityChange,
      setDialogueOpen,
      getClosestNpc: () => closestNpcInRange,
      // transcript must be forwarded here -- askNpc() folds it into options.systemOverride
      // so an archetype NPC stays consistent (e.g. keeps the same self-given name) across
      // turns of the SAME conversation; dropping it here would silently defeat that fix
      // even though askNpc itself still accepts the parameter.
      ask: (npcId, question, transcript) => askNpc(serverUrl, npcId, question, transcript),
      exitPointerLock: () => document.exitPointerLock()
    };
  }

  global.GameRagDemo = { createGame };
})(window);
