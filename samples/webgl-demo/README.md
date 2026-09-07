# GameRagKit WebGL game-scene demo

Three real Three.js game scenes proving GameRagKit NPCs work inside an actual first-person
game environment, not just an admin tool — walk around, approach an NPC, and talk to it
using the real `/ask` endpoint.

- **`single-npc.html`** — one NPC (the North Gate guard) in a small courtyard.
- **`city.html`** — five NPCs with distinct personas placed around a town square, proving
  multiple concurrent NPCs work correctly with no cross-talk between them.
- **`metropolis.html`** — a full city block grid with hundreds of talkable, wandering
  crowd figures. Proves the *cluster* pattern: every figure is individually clickable, but
  each is tagged with one of 14 profession archetypes, and talking to any figure calls
  `/ask` against that archetype's single shared `NpcAgent`. So the crowd can scale to
  hundreds/thousands of figures in the browser while the real backend load (LLM calls,
  embeddings, per-NPC memory) stays fixed at the archetype count — clicking any of 900
  figures fans in to just 14 real agents, not 900. See
  [`scripts/generate-metropolis-npcs.js`](scripts/generate-metropolis-npcs.js) for how the
  archetype roster is generated and
  [`metropolis-npcs/`](metropolis-npcs/) for the resulting NPC configs.

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
- **3D visuals are placeholder primitives** — NPCs are a colored capsule + sphere with a
  floating name label; crowd figures are simpler still (a colored box torso + head, via
  `InstancedMesh` so hundreds of them cost only two draw calls total); buildings are
  colored boxes. No textures, no animated character models, no real building meshes. This
  proves the mechanic (walk up, talk, get a real in-character reply) works; it is not
  meant to look finished.
- **The archetype/cluster pattern in `metropolis.html` is a demo-side convention, not a
  GameRagKit library feature** — it's implemented entirely in `addCrowd()`
  (`shared/game-engine.js`) by tagging each crowd figure with a shared `npcId` at spawn.
  If you want the *server* to understand a shared hierarchy of NPCs (e.g. many NPCs
  inheriting a common persona from a shared world/region/faction, or any custom tier
  names you declare), see the `Tiers`/persona-inheritance feature on the
  `feature/persona-inheritance` branch — a different, complementary mechanism for a
  different problem (shared personality context vs. shared crowd identity).

### Swapping in real assets

If you'd like to replace the placeholder geometry with real 3D models, see
[`ASSETS.md`](ASSETS.md) for exactly what to source and where each file goes. Both
`addNpc({ model: "..." })` and `addBuilding({ model: "..." })` already load a glTF/GLB
model via `THREE.GLTFLoader` and swap it in for the placeholder mesh once it finishes
loading (falling back to the placeholder if `model` is unset or loading fails), so
dropping real assets in and setting `model:` on the relevant scene's `addNpc`/`addBuilding`
calls is enough — no engine changes needed.
