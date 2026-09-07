# Sourcing real 3D assets for the WebGL demo

The demo scenes currently use placeholder geometry (colored capsule+sphere for NPCs,
colored boxes for buildings) so the interaction mechanic (walk up, press E, get a real
GameRagKit reply) is provable without depending on any art. This doc describes exactly
what to source if you want to swap in real models.

## File format & where files go

Format: **glTF Binary (`.glb`)**, single self-contained file (embedded textures), Three.js
`GLTFLoader` already wired up in `shared/game-engine.js` to load it. Avoid `.gltf` +
separate `.bin`/texture files — more files to manage for no benefit here.

Put character models in `samples/webgl-demo/assets/characters/` and building models in
`samples/webgl-demo/assets/buildings/` (create these folders — they don't exist yet).

## What to source

### Characters (6 needed)

| File | Used by | Rough vibe |
|---|---|---|
| `assets/characters/guard.glb` | `guard-north-gate` (single-npc scene) | Medieval town guard, simple armor, spear or sword optional |
| `assets/characters/blacksmith.glb` | `blacksmith-bram` (city scene) | Stocky, apron, medieval blacksmith |
| `assets/characters/tavern-keeper.glb` | `tavern-keeper-mira` (city scene) | Medieval tavern keeper, apron |
| `assets/characters/priest.glb` | `priest-aldric` (city scene) | Medieval priest/cleric robes |
| `assets/characters/merchant.glb` | `merchant-yara` (city scene) | Medieval merchant, colorful traveling clothes |
| `assets/characters/town-crier.glb` | `town-crier-oswin` (city scene) | Medieval town crier, fancier/formal clothing |

Each model should be **rigged in T-pose or idle-pose is fine (no animation needed for
this demo)**, roughly human-proportioned, sized so the model's feet sit at y=0 and its
head is roughly 1.7-1.8 world units up (the engine's `modelScale` config option can
rescale if the source model uses different units — see below).

### Buildings (4 needed, single-npc scene needs none beyond the 2 already-placeholder gate posts)

| File | Used by |
|---|---|
| `assets/buildings/blacksmith-shop.glb` | Blacksmith's building, city scene |
| `assets/buildings/tavern.glb` | Tavern building, city scene |
| `assets/buildings/chapel.glb` | Chapel building, city scene |
| `assets/buildings/market-stall.glb` | Market building, city scene |

Medieval/fantasy town aesthetic, roughly 6-9 world units wide/deep, 4-7 tall (matches the
current placeholder box dimensions in `city.html` — check that file for exact per-building
sizes before sourcing so proportions look right next to the NPCs).

## Where to get them

**Free asset sites** (web search, download, drop the `.glb` into the folders above):
- [Kenney.nl](https://kenney.nl) — free, CC0-licensed, has a "Mini Characters" and several
  medieval/fantasy town/building asset packs, exports to glTF.
- [Poly Haven](https://polyhaven.com) — free, CC0, mostly props/environment rather than
  rigged characters.
- [Sketchfab](https://sketchfab.com) — huge range, filter by "Downloadable" + CC license,
  many free medieval character/building models in glTF format directly.
- [Quaternius](https://quaternius.com) — free low-poly character packs, good stylistic
  match for a game-scene demo like this (not photorealistic).

**AI-generated** (if using a tool like the one you mentioned): ask for a **low-poly,
stylized, rigged, T-pose humanoid character in glTF/GLB format**, medieval fantasy
setting. Keep the prompt explicit about glTF/GLB export — many AI 3D generators default
to `.obj`/`.fbx`, which Three.js's `GLTFLoader` can't load directly.

## Wiring a model into a scene

Once a file exists at e.g. `assets/characters/guard.glb`, add `model` (and optionally
`modelScale` if the source model's units don't match) to that NPC's `addNpc(...)` call in
the scene's inline `<script>`:

```js
game.addNpc({
  npcId: "guard-north-gate",
  label: "Jake, North Gate Guard",
  position: { x: 0, z: -4 },
  greeting: "Halt. State your business at the North Gate.",
  model: "assets/characters/guard.glb",
  modelScale: 1.0 // adjust if the model appears too large/small next to the placeholder
});
```

Same pattern for `addBuilding(...)` with `model` pointing at a building `.glb`. If
`model` is omitted, or the file fails to load (404, malformed, etc.), the scene falls
back to the placeholder capsule/box automatically — a broken or missing asset path never
breaks the scene, it just silently keeps the placeholder (a warning is logged to the
browser console).
