# 3D assets used in the WebGL demo

The demo scenes use real, free, CC0-licensed low-poly glTF/GLB models for every named NPC
and every building in `single-npc.html` and `city.html`, plus real building models in
`metropolis.html`'s city grid. This doc lists exactly what was downloaded, where from, its
license, and how each file is wired into the scenes. `metropolis.html`'s 900-figure ambient
crowd is the one deliberate exception left as placeholder primitives — see "What's still
placeholder" below for why.

## Source packs

Character models are from **[KayKit](https://kaylousberg.com) — "Character Pack:
Adventurers"** by Kay Lousberg, licensed **CC0 1.0 Universal (public domain)** — free for
personal, educational, and commercial use, no attribution required. Buildings are from
**[Kenney.nl](https://kenney.nl)**, also CC0.

| Pack | URL | Used for |
|---|---|---|
| KayKit — Character Pack: Adventurers | https://kaylousberg.itch.io/kaykit-adventurers (mirrored at https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0) | Every named NPC (guard, blacksmith, tavern keeper, priest, merchant, town crier) |
| Castle Kit | https://kenney.nl/assets/castle-kit | The North Gate structure (`single-npc.html`) and all four town-square buildings (`city.html`) |
| City Kit: Commercial | https://kenney.nl/assets/city-kit-commercial | The modern building grid in `metropolis.html` |

### Why KayKit instead of Kenney's "Mini Characters" (previous pack)

The demo previously used Kenney's **Mini Characters** pack for all six named NPCs. That
pack is a modern/urban character set with no robed, armored, or otherwise
medieval/fantasy-appropriate figures — most visibly, its "priest" NPC used a dark
uniform-and-cap model that a real viewer identified as looking like a police officer, not
clergy (confirmed via an actual Puppeteer screenshot of the running scene, not just
reasoning about the model's name). Kenney has no dedicated medieval-characters pack.
KayKit's **Adventurers** pack was chosen after checking several alternatives (Quaternius's
"RPG Character Pack" and "Ultimate RPG Pack" are CC0 with the right theme, but their only
distribution is a Google Drive folder link with no direct per-file URL, unreliable to fetch
in an unattended/scripted way; Quaternius's "Modular Character Outfits — Fantasy" is CC0
and itch.io-hosted with a scriptable download, but its genuinely-free "Standard" tier ships
only Ranger and Peasant outfits — armor/robe variety is behind a $20 paid tier). KayKit
Adventurers is CC0, hosted directly on GitHub as ready-to-use `.glb` files (no scraping or
download-flow automation needed), and every model is self-contained (embedded texture, see
below) — no separate texture-atlas step was needed at all, unlike every other pack in this
doc.

It ships 5 distinct character bodies (Knight, Barbarian, Mage, Rogue, Rogue_Hooded) — a
"dungeon adventurer" theme rather than a dedicated "medieval villager" theme, so the
role-to-model matches below are reasonable rather than exact (there is no robed-clergy or
apron-blacksmith model in this pack specifically) — but every one is clearly
medieval-fantasy-coded, and **none reads as a modern police/military uniform**, which was
the actual defect being fixed.

Building packs (Castle Kit, City Kit: Commercial) are unchanged from before and still
appropriate for their roles; only the character pack changed.

### The KayKit switch's own defect, and how it was actually fixed (not a pack swap)

Switching to KayKit fixed the "looks like a modern police officer" problem but introduced
a new one: a real viewer's exact complaint was **"the tavern keeper has a crossbow"** —
`tavern-keeper.glb` is KayKit's `Rogue.glb`, an armed adventurer class, and it renders in
city.html visibly carrying a crossbow across its arms (confirmed via an actual Puppeteer
screenshot of the running scene, `city-tavern-merchant.png` from that session, not just
reasoning about the model's class name). `merchant.glb` (`Rogue_Hooded.glb`, the same base
body) had the identical problem. `blacksmith.glb` (`Barbarian.glb`) visibly carried an axe
+ round shield, and `priest.glb` (`Mage.glb`) visibly carried a wand/staff/spellbook —
all four non-guard civilian roles read as armed combatants, which is the actual defect a
second look at KayKit needed to fix.

**A full pack replacement was investigated and rejected.** Before touching the character
pack again, every plausible free/CC0 alternative was re-checked, this time with actual
license reads and direct download attempts, not just reputation:

- **Quaternius "RPG Character Pack"** (quaternius.com/packs/rpgcharacters.html) — the
  single best thematic fit found anywhere: "6 different rigged, animated and textured
  fantasy characters," CC0, glTF export available. Re-verified directly (not just taking
  the earlier session's word for it) by fetching the page's raw HTML and inspecting the
  actual `href` behind its `#inline` download modal: it resolves to
  `drive.google.com/drive/folders/1MIRQXLfTd21HMI5rwOb6Xy0rv0xv1m8b` — a Google Drive
  folder, not a direct per-file URL or an itch.io button, despite the page also loading an
  itch.io widget script for an unrelated element. Confirmed genuinely not scriptable, same
  conclusion as the prior session reached, now with the literal URL evidence in hand.
- **Quaternius "Universal Base Characters"** (quaternius.itch.io/universal-base-characters)
  — itch.io-hosted, CC0 ("Creative Commons Zero v1.0 Universal"), scriptable direct
  download. But its free "Standard" tier ships bare underwear/base-mesh bodies with no
  clothing at all (confirmed against its own preview images: "minimal mesh characters...
  in underwear or foundational states") and no baked animations — combining it with actual
  clothing requires either the $20+ source tier or hand-assembling
  "Modular Character Outfits — Fantasy" (below) in a 3D editor, well beyond a scripted
  asset fetch.
- **Quaternius "Modular Character Outfits — Fantasy"** (quaternius.itch.io/modular-character-outfits-fantasy)
  — re-confirmed CC0, itch.io-scriptable, but the free tier ships only Ranger and Peasant
  outfits as static modular pieces meant to attach to a separate base-character rig (no
  baked animations of its own); the interesting armor/robe variety is behind a $20+ tier.
  Same conclusion as the prior session, re-verified.
- **poly.pizza** (already had a working Puppeteer download script in this repo's
  scratchpad tooling from a prior session) — searched directly for blacksmith/priest/
  merchant/villager-type individual character models. Every civilian-sounding search term
  returned only props, buildings, or paid Unity Asset Store packages, never a standalone
  free civilian character model; Quaternius's own individual uploads there are combat
  classes (Adventurer, Wizard) or generic named base bodies, not villager roles.
- **KayKit's own other packs** (kaylousberg.itch.io) — checked whether any other KayKit
  release ships civilian bodies. The Adventurers pack's free tier is Knight/Barbarian/
  Rogue/Mage/Ranger; its paid EXTRA tier adds Engineer/Druid/Barbarian_Large — still no
  peasant/civilian body. The "Mystery Monthly Series" packs are paid-only and explicitly
  documented by their own author as "technically identical to the Adventurer characters"
  (same rig/classes, different reskins) — not a source of new civilian archetypes.
- **Kenney "Mini Characters"** (already fetched in a prior session, re-examined here) —
  confirmed CC0 via its own `License.txt`. Its `character-male-*.glb`/`character-female-*.glb`
  files carry only 2 animation clips each (`static`, `idle`) — no walk cycle — and its
  preview thumbnails confirm a modern/chibi urban style (casual clothes, backpack-shaped
  silhouette) that reads out of place next to Castle Kit's medieval buildings, the same
  conclusion the original Kenney-to-KayKit switch already reached.

**Conclusion: no CC0 pack exists that is both a better civilian-role fit than KayKit AND
genuinely fetchable by an unattended script.** Per this task's own decision rule (civilian
role fit matters more than animation, and a full pack swap wasn't achievable without
either paying, hand-assembling models in a 3D editor, or giving up scriptability), the fix
applied instead is to **keep the exact same KayKit `.glb` files** (all 6 filenames and
contents are unchanged) and **hide each civilian NPC's held weapon/prop mesh at load
time**, rather than swap the source pack.

This is possible because every KayKit Adventurers character attaches its held item(s) as
their own separately-named glTF nodes under the hand bones (e.g. `1H_Crossbow`,
`Knife_Offhand`, `Spellbook`, `Barbarian_Round_Shield`) — siblings of the body meshes
(`Rogue_Body`, `Rogue_Head`, `Rogue_ArmLeft`/`Rogue_ArmRight`, etc.), not merged into them.
`shared/game-engine.js`'s `loadNpcModel` now accepts an optional `hideNodes: string[]`
config (case-insensitive substring match against every node name in the loaded model,
via a new `hideMatchingNodes()` helper) and sets `.visible = false` on every match before
adding the model to the scene. The skeleton/animation still drives the hidden node's bone
transform every frame — it's simply never rendered — so this has no effect on rig
correctness or the animation-mixer wiring described below.

| NPC | Role | `hideNodes` | What it hides |
|---|---|---|---|
| `blacksmith-bram` | blacksmith | `["axe", "shield"]` | `1H_Axe`, `2H_Axe`, `1H_Axe_Offhand`, `Barbarian_Round_Shield` |
| `tavern-keeper-mira` | tavern keeper | `["crossbow", "knife", "throwable"]` | `1H_Crossbow`, `2H_Crossbow`, `Knife`, `Knife_Offhand`, `Throwable` — the exact reported defect |
| `priest-aldric` | priest | `["wand", "staff", "spellbook"]` | `1H_Wand`, `2H_Staff`, `Spellbook`, `Spellbook_open` |
| `merchant-yara` | merchant | `["crossbow", "knife", "throwable"]` | same nodes as tavern-keeper (same base body, `Rogue_Hooded.glb`) |
| `guard-north-gate` | guard | *(none)* | unchanged — a guard holding a sword/shield is the correct read for this role, per this task's own instructions |
| `town-crier-oswin` | town crier | *(none)* | unchanged — reuses `Knight.glb` (same file as `guard.glb`); out of scope for this fix, not one of the reported "must not look armed" roles |

Verified with real Puppeteer screenshots of the running `city.html` scene, comparing
directly against the earlier session's own `city-tavern-merchant.png` (which shows Mira
and Yara both visibly holding a crossbow): after this change, both NPCs render with empty
hands/arms and no visible weapon mesh, while `guard-north-gate`'s Jake keeps his sword as
intended.

Each pack ships multiple export formats; the `.glb` (glTF Binary, self-contained single
file) exports were used, matching what `THREE.GLTFLoader` (already wired up in
`shared/game-engine.js`) expects.

## Files in use

All paths are relative to `demos/assets/` (i.e. `samples/webgl-demo/assets/` in this
standalone copy, and `src/GameRagKit.Cli/StudioWeb/demos/assets/` in the CLI-served copy —
kept in sync).

### `characters/` — KayKit Character Pack: Adventurers

| File | Used by | Source model | Why this model |
|---|---|---|---|
| `guard.glb` | `guard-north-gate` (single-npc.html) | `Knight.glb` | Full plate armor + helmet — the closest 1:1 fit in the pack |
| `town-crier.glb` | `town-crier-oswin` (city.html) | `Knight.glb` (same source file, reused) | See "Reused model" note below |
| `blacksmith.glb` | `blacksmith-bram` (city.html) | `Barbarian.glb` | Rugged, bearded, working-class look; not an exact apron-and-forge fit but reads as a laborer, not a modern role |
| `tavern-keeper.glb` | `tavern-keeper-mira` (city.html) | `Rogue.glb` | Plain tunic, face/hair visible (unhooded) |
| `priest.glb` | `priest-aldric` (city.html) | `Mage.glb` | Long coat + pointed hat — the pack has no robed-clergy model; this is the closest available fantasy-coded (not modern-uniform) alternative |
| `merchant.glb` | `merchant-yara` (city.html) | `Rogue_Hooded.glb` | Same base body as `tavern-keeper.glb`'s `Rogue.glb`, distinguished by its raised hood |

**Reused model note:** the pack ships only 5 distinct character bodies, and there are 6
named roles. `guard.glb` and `town-crier.glb` are both `Knight.glb`, but `guard-north-gate`
only ever appears in `single-npc.html` and `town-crier-oswin` only ever appears in
`city.html` — the two are never rendered in the same scene, so this reuse does not create
a visible "two identical NPCs" problem. All 5 NPCs that **are** simultaneously visible in
`city.html` (blacksmith, tavern-keeper, priest, merchant, town-crier) use 5 different
source models and are visually distinct from each other.

Each is loaded via `addNpc({ model: "assets/characters/<file>", modelScale: <see below>,
modelYOffset: 0 })`. Every KayKit Adventurers character is a fully rigged/skinned
character (not a static mesh), and every one is already grounded with its lowest vertex at
local y ≈ -0.00003 (effectively 0) — verified by actually loading each model in a headless
Three.js scene (Puppeteer + `THREE.Box3().setFromObject(gltf.scene)`), **not** by scanning
the glTF accessors' raw min/max directly. For a skinned rig, individual POSITION accessors
are bone-local space (IK helper/control bones go as low as local y = -1.16 despite the
character's feet being on the ground in its actual bind pose) — only a real
matrixWorld-aware bounding-box computation gives the true world-space height. `modelScale`
per character targets a body-only measured height of 1.75 world units (matching the
placeholder capsule and this pack's `guard.glb`), with held weapons/shields/hats excluded
from the measurement so a raised sword or (in the Mage's case) a tall pointed hat doesn't
skew the scale used for the body itself:

| File | Measured body-only height (local units) | `modelScale` | Scaled height |
|---|---|---|---|
| `guard.glb` / `town-crier.glb` (Knight) | 2.4666 | 0.7095 | 1.75 |
| `blacksmith.glb` (Barbarian) | 2.3978 | 0.7298 | 1.75 |
| `priest.glb` (Mage, hat/spellbook/wand/staff excluded) | 2.2027 | 0.7945 | 1.75 |
| `tavern-keeper.glb` (Rogue) | 2.1870 | 0.8002 | 1.75 |
| `merchant.glb` (Rogue_Hooded) | 2.2513 | 0.7773 | 1.75 |

### `gate/` — Castle Kit pack (North Gate structure, single-npc.html)

| File | Role |
|---|---|
| `gate.glb` | Central archway the player walks through |
| `tower-square.glb` | Tower base, one on each side of the gate |
| `tower-square-roof.glb` | Tower roof cap, stacked on each tower base |
| `wall.glb` | Short wall segments running outward from each tower |
| `wall-corner.glb` | Downloaded for future use extending the wall run (not currently placed) |

Composed in `single-npc.html` via `addBuilding({ models: [...] })` — see that file for the
exact stacking math (each piece uses `modelYOffset` to sit correctly on top of the piece
below it, since every Castle Kit piece shares the same "centered in its own module,
base at local y = -1" convention as the characters).

**Gap fix:** `gate.glb` is only the narrow archway/door module itself (local X width
0.1516 units, ~0.4 world units wide at the scene's `GATE_SCALE`), not a full 2-unit-wide
wall module like `tower-square.glb`/`wall.glb` (1.0 local unit wide, 2.6 world units at
`GATE_SCALE`). The gate building's `width` used to be set as if the gate module filled a
full `GATE_SCALE * 2` span, which left a real ~5-world-unit gap of bare ground between the
archway and each tower — confirmed via an actual screenshot of the running scene, not just
by inspecting the position numbers. Fixed by adding two `wall.glb` filler segments per side
(placed via each composed piece's own `position: { x }`, relative to the gate building's
group) that exactly close the gap from the gate's real edge out to each tower's inner face,
so the archway, filler wall, and tower now read as one continuous wall structure instead of
three disconnected pieces.

### `buildings/` — Castle Kit pack (city.html's four town-square buildings)

| File | Used in |
|---|---|
| `tower-square.glb` | Blacksmith (base), Chapel (base), Market (whole building) |
| `tower-square-top-roof.glb` | Blacksmith (roof cap) — squat, flat-topped |
| `tower-square-roof.glb` | Chapel (roof cap) — tall pointed spire |
| `tower-hexagon-base.glb` | Tavern (base) |
| `tower-hexagon-mid.glb` | Tavern (middle section) |
| `tower-hexagon-roof.glb` | Tavern (roof cap) — tallest of the four, most storeys |

Castle Kit ships modular base/mid/roof pieces rather than one single "building" model per
building type, so each of the four buildings is a different combination (via
`addBuilding({ models: [...] })`), keeping them visually distinct: a squat square forge, a
tall hexagonal tavern, a spired square chapel, and a single-storey square market stall.

### `city/` — City Kit: Commercial pack (metropolis.html's building grid)

| File |
|---|
| `building-a.glb`, `building-c.glb`, `building-e.glb`, `building-g.glb`, `building-i.glb`, `building-k.glb` |
| `building-skyscraper-a.glb`, `building-skyscraper-c.glb`, `building-skyscraper-e.glb` |

A variety pool of 9 modern building models; `metropolis.html` assigns one per grid cell
(deterministically, by grid position) via `addBuilding({ model: ..., modelScale: 4.2,
modelYOffset: 4.2 })`.

## Textures (`Textures/colormap.png`)

**Bug found and fixed (Kenney building packs only):** the initial download of the Kenney
`.glb` building files pulled only the model geometry, not each pack's shared texture atlas.
Every one of these `.glb` files references its texture via glTF `images[].uri =
"Textures/colormap.png"`, a path resolved by `THREE.GLTFLoader` relative to the `.glb`
file's own folder — and no `Textures/` subfolder existed anywhere under `assets/`. In a real
browser this made every model fail to load (`GLTFLoader` → `InvalidStateError: The source
image could not be decoded`, verified via Puppeteer against every single model), silently
falling back to the untextured placeholder capsule/box.

Fix: each pack's official Kenney download zip includes `Models/GLB format/Textures/colormap.png`
— the same shared 512×512 color atlas every model in that pack's UVs are mapped against.
Fetched both zips directly from kenney.nl (same domain the `.glb` files themselves
came from) and extracted the real `colormap.png` for each:

| Kit | Zip fetched from | `colormap.png` source path in zip | Size |
|---|---|---|---|
| Castle Kit | `https://kenney.nl/media/pages/assets/castle-kit/a395102d20-1711543616/kenney_castle-kit.zip` | `Models/GLB format/Textures/colormap.png` | 7,529 bytes (512×512 PNG) |
| City Kit: Commercial | `https://kenney.nl/media/pages/assets/city-kit-commercial/a742d900eb-1753115042/kenney_city-kit-commercial_2.1.zip` | `Models/GLB format/Textures/colormap.png` | 11,002 bytes (512×512 PNG) |

Both are the **real** Kenney-authored textures — no placeholder/generated fallback was
needed; both zips fetched cleanly (HTTP 200, valid zip archive, contained the expected
`Textures/colormap.png` at the expected path).

Placed at (identical bytes copied to both the CLI-served copy and this standalone copy):

| Path | Copy of |
|---|---|
| `buildings/Textures/colormap.png` | Castle Kit colormap |
| `gate/Textures/colormap.png` | Castle Kit colormap (same file as `buildings/` — `gate/` and `buildings/` are both Castle Kit pieces in separate folders, and `GLTFLoader` resolves `Textures/colormap.png` relative to each `.glb`'s own folder, so the same atlas needed a copy in both places) |
| `city/Textures/colormap.png` | City Kit: Commercial colormap |

**`characters/` has no `Textures/` folder** — every KayKit Adventurers `.glb` embeds its
texture directly in the binary (glTF `images[].bufferView`, not `images[].uri`), so there is
no external file dependency to resolve at all for character models. (The old
`characters/Textures/colormap.png`, copied from Kenney's Mini Characters pack, was deleted
along with the switch away from that pack — nothing references it any more.)

Verified by parsing the embedded glTF JSON out of every `.glb`'s binary header (not just
checking that files exist by name): for the building packs, every `images[].uri` is
resolved relative to the `.glb`'s own directory and confirmed to exist on disk
(`os.path.normpath(os.path.join(glb_dir, uri))` + `os.path.isfile(...)`); for the 6
character `.glb`s, every `images[]` entry was confirmed to use `bufferView` (embedded) with
no `uri` key present at all, i.e. no external file to resolve or go missing. Re-check this
the same way after any future asset change — read the first JSON chunk of each `.glb`
(12-byte header + 8-byte chunk header, then `chunk_len` bytes of JSON) and inspect
`images[]`.

## Animation playback

Every KayKit Adventurers `.glb` ships **76 baked animation clips** (`Idle`,
`Walking_A`/`B`/`C`, `Unarmed_Idle`, `1H_Melee_Attack_Chop`, `Death_A`, `Sit_Chair_Idle`,
etc. — the same combat/emote/locomotion library across all 6 files, verified by parsing
each `.glb`'s embedded glTF JSON `animations[]` array directly), but until now every model
loaded via `THREE.GLTFLoader` was added to the scene with zero animation playback — no
`AnimationMixer` was ever created, so every NPC stood frozen in its bind pose regardless of
what clips its file actually contained.

`shared/game-engine.js` now wires this up in `loadNpcModel` (the same function that already
does `new THREE.GLTFLoader().load(...)` and receives `gltf.scene`): the callback also
receives `gltf.animations` from the same load. If that array is non-empty, a
`new THREE.AnimationMixer(modelScene)` is created, a clip is chosen via `pickDefaultClip()`
(prefers a clip whose name matches `/idle/i`, falling back to `/walk/i`, falling back to
`animations[0]` if neither matches), `mixer.clipAction(clip).play()` starts it looping
(THREE's default `LoopRepeat`), and the mixer is pushed onto a module-level `activeMixers`
array. `tick()` (the main per-frame loop, already computing `dt` for player movement) now
also does `for (const m of activeMixers) m.update(dt)` — reusing that same `dt`, not a
separately computed delta. Every currently-wired NPC (`guard-north-gate`, `blacksmith-bram`,
`tavern-keeper-mira`, `priest-aldric`, `merchant-yara`, `town-crier-oswin`) picks up its
`Idle` clip this way, since all 6 `.glb`s have a literal clip named `Idle`.

**Backward compatibility:** a model with no `animations` array (every Castle Kit/City Kit
building piece loaded via `addBuilding`, and any future model that ships no clips) never
gets a mixer at all — nothing is pushed to `activeMixers`, so `tick()`'s update loop simply
has nothing to do for it. No null-checks were needed in the hot loop because the array is
only ever populated with real mixers; verified directly by loading `single-npc.html`
(gate/tower/wall building pieces, none of which have animations) and confirming no console
errors and no dead code path is hit.

**Real verification performed** (not just "the code path exists"):

- **`mixer.update()` call-count + accumulated-`dt` check.** `THREE.AnimationMixer.prototype.update`
  was monkey-patched via `page.evaluateOnNewDocument` (before game-engine.js's own script
  tag runs) to count calls and sum every `dt` passed to it, then read back via
  `page.evaluate` at two points ~1.5s apart while `city.html` (5 animated NPCs) ran. Calls
  increased by 450 and accumulated `dt` increased by ~7.51 "mixer-seconds" over that ~1.5s
  wall-clock window — consistent with 5 active mixers each being ticked once per rendered
  frame at roughly 60fps, i.e. real, continuous invocation driven by the actual per-frame
  render loop, not a one-shot or stubbed call.
- **Bone-transform check in an isolated harness.** Reading `spine`'s local rotation before/
  after showed no change — investigated further rather than assumed broken, and it turned
  out `Idle`'s `spine` keyframes are genuinely identical at t=0 and t=1.07s in the source
  data itself (confirmed by decoding the glTF accessor bytes directly: `spine`'s
  translation/rotation/scale channels all have exactly 2 keyframes with byte-identical
  output values) — a static hold, not a mixer failure. Checking every channel in the `Idle`
  clip found 17 of 123 channels do carry real motion (`hips` translation, both
  `upperarm`/`lowerarm`/`hand` chains, both `upperleg`/`lowerleg`/`foot` chains, both
  `handIK` targets — amplitudes up to ~0.21 radians on `lowerleg`). Re-running the same
  isolated-harness bone sample against `lowerleg.l` instead of `spine` showed real motion:
  `rotation.x` ranged from 0.7157 to 0.9953 (a 0.28 radian swing) across a ~3.5s sample
  window, with first-frame and last-frame values clearly different — direct, numeric proof
  the skinned rig is actually being deformed frame-by-frame by the playing clip.
- **In-scene screenshots.** `single-npc.html`'s guard and `city.html`'s 5 NPCs were
  screenshotted while running under the real ASP.NET static-file server (not a bare-file
  harness), confirming no console/page errors from the mixer/animation code path in the
  actual demo scenes, not just the isolated test page used for the bone check above.

## What's still placeholder

- **`metropolis.html`'s 900-figure ambient crowd** (`addCrowd()`) stays as simple colored
  box primitives (torso + head), rendered via `THREE.InstancedMesh` so hundreds of figures
  cost only two draw calls total. `InstancedMesh` shares one geometry buffer across every
  instance; giving each of 14 archetype groups its own loaded GLB skeleton would need a
  materially different rendering approach (e.g. per-archetype `InstancedMesh` built from a
  loaded model's geometry, with real bone/skinning handled separately from the
  position/heading update loop) rather than the `model:`/`models:` config addition that
  worked for `addNpc`/`addBuilding`. This is a deliberate, documented scope boundary, not
  an oversight — the crowd's talkability, movement behaviors (wander/patrol/stationary),
  and archetype-sharing all still work identically; only the visual mesh is primitive.

## Wiring a model into a scene (reference)

```js
// Single model, straightforward swap:
game.addNpc({
  npcId: "guard-north-gate",
  label: "Jake, North Gate Guard",
  position: { x: 0, z: -4 },
  greeting: "Halt. State your business at the North Gate.",
  model: "assets/characters/guard.glb",
  modelScale: 0.7095,
  modelYOffset: 0
});

// Civilian role using the same combat-class source model, with its held weapon hidden:
game.addNpc({
  npcId: "tavern-keeper-mira",
  label: "Mira, Tavern Keeper",
  position: { x: 14, z: -4 },
  greeting: "Welcome in! Ale's fresh, rumors are fresher.",
  model: "assets/characters/tavern-keeper.glb", // KayKit Rogue.glb -- ships a crossbow
  modelScale: 0.8002,
  modelYOffset: 0,
  hideNodes: ["crossbow", "knife", "throwable"] // hides only the held-item nodes, not the body/rig
});

// Composed structure, several kit pieces stacked into one building:
game.addBuilding({
  position: { x: 0, z: -8 },
  width: 5.2, depth: 5.2, height: 5.2,
  models: [
    { model: "assets/gate/tower-square.glb", modelScale: 2.6, modelYOffset: 2.6 },
    { model: "assets/gate/tower-square-roof.glb", modelScale: 2.6, modelYOffset: 2.6 + 2.6 * 2.31 }
  ]
});
```

If `model`/`models` is omitted, or a file fails to load (404, malformed, etc.), the scene
falls back to the placeholder capsule/box automatically — a broken or missing asset path
never breaks the scene, it just silently keeps the placeholder (a warning is logged to the
browser console).

**Server note:** if you serve these demos from a custom host rather than
`gamerag studio`/a plain static file server, make sure `.glb` is registered as a servable
static-file extension — ASP.NET Core's default `StaticFileOptions` only serves extensions
in its built-in MIME-type map, and `.glb` (`model/gltf-binary`) isn't one of them by
default (`GameRagKit.Cli/Program.cs`'s `studio` command registers it explicitly for this
reason).
