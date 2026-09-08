# GameRagKit WebGL game-scene demo

Three real Three.js game scenes proving GameRagKit NPCs work inside an actual first-person
game environment, not just an admin tool — walk around, approach an NPC, and talk to it
using the real `/ask` endpoint.

- **`single-npc.html`** — one NPC (the North Gate guard) in a small courtyard, standing in
  front of a real gate structure (two castle-kit towers + wall segments flanking a gate
  archway). Warm, low-angle dawn/dusk lighting and a muted autumn-toned ground give this
  scene a quiet, intimate mood, deliberately distinct from `city.html`'s bright midday.
- **`city.html`** — five NPCs with distinct personas placed around a town square, proving
  multiple concurrent NPCs work correctly with no cross-talk between them. Bright midday
  market-town daylight and livelier ground/sky colors set this apart from
  `single-npc.html`'s hushed dawn courtyard even though both are the same medieval town.
- **`metropolis.html`** — a full city block grid with hundreds of talkable, wandering
  crowd figures. Proves the *cluster* pattern: every figure is individually clickable, but
  each is tagged with one of 14 profession archetypes, and talking to any figure calls
  `/ask` against that archetype's single shared `NpcAgent`. So the crowd can scale to
  hundreds/thousands of figures in the browser while the real backend load (LLM calls,
  embeddings, per-NPC memory) stays fixed at the archetype count — clicking any of 900
  figures fans in to just 14 real agents, not 900. See
  [`scripts/generate-metropolis-npcs.js`](scripts/generate-metropolis-npcs.js) for how the
  archetype roster is generated and
  [`metropolis-npcs/`](metropolis-npcs/) for the resulting NPC configs. Modern night-time
  city mood (cool moonlit ambient/fog + warm scattered streetlamp point lights) makes this
  scene read as nothing like the two medieval demos.

All three scenes share the same engine ([`shared/game-engine.js`](shared/game-engine.js)): a
Three.js first-person controller (WASD movement, mouse-look via pointer lock, simple
building collision), an NPC actor system (capsule+sphere avatar, name label, proximity
detection), an `InstancedMesh`-based crowd system for the archetype/cluster pattern, and a
dialogue overlay ([`shared/dialogue-controller.js`](shared/dialogue-controller.js))
that opens when you approach an NPC or crowd figure and press `E`, then calls GameRagKit's
real HTTP API.

## Running it

1. Start a GameRagKit server pointed at this demo's NPCs — `./npcs` for `single-npc.html`/
   `city.html`, or `./metropolis-npcs` for `metropolis.html`:

   ```bash
   dotnet run --project ../../src/GameRagKit.Cli -- serve --config ./npcs --port 5280
   # or, for the metropolis scene:
   dotnet run --project ../../src/GameRagKit.Cli -- serve --config ./metropolis-npcs --port 5280
   ```

   (run from `samples/webgl-demo/`, or adjust the `--config` path)

2. Serve this folder as static files — the scenes fetch Three.js from a CDN and call
   `/ask` via `fetch()`, both of which need `http://` rather than `file://` to work
   reliably. Any static file server works, e.g.:

   ```bash
   python3 -m http.server 8000
   ```

3. Open `http://localhost:8000/single-npc.html`, `http://localhost:8000/city.html`, or
   `http://localhost:8000/metropolis.html`.

4. Click the canvas to lock the mouse and look around. **WASD** to move. Walk close to
   an NPC until "Press E to talk" appears, then press **E** to open the dialogue panel.
   Type a question and press Enter/Send — the NPC's reply comes from a real call to
   GameRagKit's `/ask` endpoint. **Esc** closes the dialogue and returns to walking.

### Pointing at a different server

All three scenes default to `http://localhost:5280`. To point at a different GameRagKit
instance (e.g. a Cloud Run deployment — see
[`docs/deploy-cloud-run.md`](../../docs/deploy-cloud-run.md)), set
`window.GAMERAG_SERVER_URL` before the scene's inline script runs, e.g. by adding a
`<script>window.GAMERAG_SERVER_URL = "https://your-instance.run.app";</script>` tag
before the `game-engine.js` include.

## What's placeholder vs. real

- **NPC dialogue is 100% real** — every response comes from an actual GameRagKit
  `/ask` call against the configured LLM provider, not scripted/canned text. This includes
  every crowd figure in `metropolis.html`, not just the named actors in the other two
  scenes — clicking any figure talks to its archetype's real `NpcAgent`.
- **3D visuals use real, free, CC0 assets** for every named actor and building in
  `single-npc.html` and `city.html` — real low-poly character models (Kenney "Mini
  Characters") for every named NPC, a real gate/tower/wall structure and composed
  building models (Kenney "Castle Kit") for the North Gate and the four town-square
  buildings. `metropolis.html`'s grid buildings use real Kenney "City Kit: Commercial"
  models. See [`ASSETS.md`](ASSETS.md) for exact files, sources, and license.
  A colored placeholder capsule/sphere (NPCs) or box (buildings) still renders
  synchronously and is swapped out once the real model finishes loading — so if a model
  ever fails to load (bad path, network hiccup), the scene degrades to the placeholder
  instead of showing a hole.
- **`metropolis.html`'s 900 ambient crowd figures stay simple placeholder primitives** (a
  colored box torso + head, via `InstancedMesh` so hundreds of them cost only two draw
  calls total) — deliberately left as-is: `InstancedMesh` shares one geometry buffer
  across every instance, and swapping that for a full per-archetype loaded GLB skeleton
  would be a materially bigger rewrite of the crowd renderer, not a `model:` config
  addition like `addNpc`/`addBuilding` get. The named actors and every building in all
  three scenes are real models; only this ambient crowd is still primitive.
- **The archetype/cluster pattern in `metropolis.html` is a demo-side convention, not a
  GameRagKit library feature** — it's implemented entirely in `addCrowd()`
  (`shared/game-engine.js`) by tagging each crowd figure with a shared `npcId` at spawn.
  If you want the *server* to understand a shared hierarchy of NPCs (e.g. many NPCs
  inheriting a common persona from a shared world/region/faction, or any custom tier
  names you declare), see the `Tiers`/persona-inheritance feature on the
  `feature/persona-inheritance` branch — a different, complementary mechanism for a
  different problem (shared personality context vs. shared crowd identity).

### The asset-loading mechanism

`addNpc({ model: "..." })` and `addBuilding({ model: "..." })` load a glTF/GLB model via
`THREE.GLTFLoader` and swap it in for the placeholder mesh once it finishes loading
(falling back to the placeholder if `model` is unset or loading fails).
`addBuilding({ models: [...] })` additionally supports composing several small kit pieces
(e.g. a tower base + roof, or a wall + gate arch) into one bigger structure — see the
North Gate in `single-npc.html` or any of the four buildings in `city.html` for examples.
`modelScale` rescales a loaded model uniformly; `modelYOffset` shifts it vertically after
scaling, needed because Kenney's kit pieces are exported centered in a normalized
bounding cube rather than resting with their base at y=0. See [`ASSETS.md`](ASSETS.md) for
exactly which files are in use, where they came from, and their license — and for what to
source if you want to swap in a different pack.
