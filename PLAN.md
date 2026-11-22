# Cube Planet restructure plan

Goal: restructure this 3D Vulkan voxel planet game like the older 2D project (PolarGame), load all blocks / items / UIs /
textures from asset files made with the Python AssetEditor, add block + item instance state, multiblocks, interactive blocks
with UIs, and save worlds as real chunk data. Then extend the AssetEditor for 3D blocks.

Reference material (read these first):
* `C:\Users\luka\Other\Desktop\PolarGame.zip` - old 2D game (C#, OpenTK/OpenGL). Model for project structure and asset loading.
* `C:\Users\luka\Other\Desktop\AssetEditor.zip` - Python/Tkinter asset editor. `README.md` and `PythonAssetToolManager.txt` inside
  explain every definition key in detail (items, blocks, UIs, geometry, multiblock, instance_state, UI widgets).
* This project's `README.md` - everything the 3D game already does (renderer, chunk pipeline, graphics settings, water, collision).
* Unzip the references into their own folders and only read them; don't build or run anything from them.

Work phase by phase. After every phase: build, run the checks listed below, and stop for the user to try the game after phases 2, 4 and 5.

---

## What to keep from the 2D project / editor

* Folders by area (`Blocks/`, `Worlds/`, `Rendering/Chunks`, `Rendering/Overlay`, `Generation/`, `Entities/`, `Assets/`), one class per
  file, comments that explain why.
* `AssetPaths`: the one place that names every asset file. `Assets/` is copied next to the exe (csproj `CopyToOutputDirectory`); for the
  single-file publish, ship `Assets/` next to the exe (optionally embed as a fallback).
* Registries that throw on an unknown name (typos found at startup), magenta/black checkerboard for a missing texture, textures uploaded
  as a texture array (layer = texture ID, no mipmap bleeding).
* The game loads exactly the files the editor exports (the editor format is the standard, not PolarGame's `atlas_index.txt`):
  `block_texture_atlas.png`, `item_texture_atlas.png`, `block_texture_definitions.txt`, `item_texture_definitions.txt`,
  `block_definitions.txt`, `item_definitions.txt`, `ui_definitions.txt`.
  Texture definition format: header `texture_size: W H`, `textures_per_row: N`, `texture_rows: N`, then `id name x y` lines.
  Definition files are JSON `{"blocks":[...]}` / `{"items":[...]}` / `{"uis":[...]}`; loader tolerates `//` comments and trailing commas.
* Definition shape: `type, namespace, id_name, id, name, group_tags, properties.definition, properties.instance_state`.
  IDs are 2 bytes (1..65535), 0 = air / empty. Item IDs and block IDs are separate ID spaces; every block has an item (`places_block`).
  Item texture ID = item ID. Blocks reference block texture IDs in `textures` (named, nestable slots).
* PNG loading: StbImageSharp (as in PolarGame).

---

## Phase 0 - Safety net (done)

* Ask the user, then `git init` + commit the current state (there is no repo yet).
* "World fingerprint": a bench-mode option that generates all chunks around spawn for a fixed radius/seed and prints a hash of their
  block data (and writes reference screenshots with the existing `--bench --shot`). Every later phase must keep the hash identical
  (after mapping old byte IDs to new IDs in phase 2).

## Phase 1 - Restructure (no behaviour change) (done)

Today almost everything is one big `static partial class W` spread over files, plus `Gpu` (Vulkan) and big GLSL strings.
Target layout:
```
Program.cs, Game.cs
Assets/                  editor files (+ sounds/ later)
AssetLoading/            AssetPaths, TextureDefinitionFile, AtlasLoader, DefinitionFile (tolerant JSON)
Blocks/                  BlockDefinition, BlockRegistry, BlockModel, BlockState, BlockEntity, Behaviors/
Items/                   ItemDefinition, ItemRegistry, ItemStack, Inventory
UI/                      UiDefinition, UiRegistry, UiScreen, Widgets/, Hud, SettingsPanel, Font
Sounds/                  SoundDefinition, SoundRegistry (IDs only, no audio yet)
Entities/                Entity, EntityDefinition placeholders (later: player, dropped item, arrow, chicken, zombie...)
Worlds/                  World, Band, Chunk, Core, ChunkLoader, Water, Saving/
Generation/              Terrain, Caves, Biomes, Ores, Features, Structures
Physics/                 Player, Collision, Camera
Rendering/Vulkan/        Gpu split into Device, Swapchain, Memory, Pipelines, ShaderCompiler
Rendering/Chunks/        Mesher, ChunkMesh, LodTerrain
Rendering/Effects/       Shadows, Clouds, Sky, Water, PostProcess
Rendering/Shaders/       .glsl files (instead of C# string constants), copied to output / embedded
```
Status: done as folders + static classes (see README "Code layout"); `AssetLoading/`, `Items/`, `UI` definitions, `Sounds/` and `Entities/`
arrive with phases 2 and 4. Fingerprint and shader sources are byte-identical to the baseline.
Recommended depth (default): move code into these folders and smaller, well-named classes now; do NOT rewrite the world into instance
objects (World/Chunk instances like PolarGame) in one go - do that gradually later. Fingerprint + screenshots must be unchanged.

## Phase 2 - Assets and definitions drive everything (done)

Status: done. Exporter ran once (commit "Phase 2a", then removed), tables + textures load from Assets/, named IDs in Blocks/BlockIds.cs,
`ushort` chunk data, shader material flags (checked: identical to the old layer rules on 50M vertices). Fingerprints unchanged.
* One-time exporter `--export-assets`: writes today's 117 procedurally generated blocks (`Textures.cs`, `Textures.Biomes.cs`) as the 7
  editor files into `Assets/`: block texture atlas (16x16), block definitions, item definitions, item icons (32x32, rendered from the
  block). Afterwards textures load from `Assets/` and the procedural texture code is removed.
* Replace numeric block literals in generation/features/water/mesher (2 = stone, 9 = water, 66 = deepslate, ...) with named lookups
  resolved at startup (e.g. `Blocks.Stone = registry.Id("base:stone")`). Fingerprint must stay identical.
* Chunk block data becomes `ushort[]` (2-byte IDs) instead of `byte[]`.
* The per-block tables (`OPQ`, `SPC`, `TGR`, `UWP`, `LOGID`, names...) are built from definitions at load time (arrays indexed by ID).
* The shaders currently hard-code texture layer IDs (lava/magma/sea-lantern glow 75/85/88, leaf/plant sway ranges, underwater plants
  110-117, shadow-pass cutout ranges). Replace with per-vertex material flags from definitions (the packed chunk vertex has free bits in
  its second uint16: bits 0-1 uv, 2-3 AO, rest free).

## Phase 3 - 3D block models (done, except rotation)

Status: per-face textures, `boxes` model (slabs, stairs, fence post), 3D collision / selection boxes in physics, raycast, highlight and
placement, fractional UVs in the packed vertex (flags moved to the normal's 4th byte), `--selftest` / `--showcase`. `rotation` is parsed but
turning needs the facing from block state -> do it in phase 4 together with doors / stairs placement.

Proposed definition keys (names can change):
```json
"model": "cube" | "cross" | "liquid" | "boxes" | "attached",
"textures": { "all": 1 }  |  { "top": 9, "bottom": 2, "side": 40 }  |  { "north":..,"south":..,"east":..,"west":..,"top":..,"bottom":.. },
"render": { "layer": "opaque|cutout|translucent|liquid", "emissive": 0.0, "sway": "none|leaves|plant", "ao": true, "connect_group": "glass" },
"geometry": { "collision": [ {"x":0,"y":0,"z":0,"w":1,"h":0.5,"d":1} ], "selection": [ ... ], "state_overrides": [ {"when":{...}, ...} ] },
"rotation": "none" | "horizontal" | "all"
```
* cube: 1 / 3 / 6 face textures. cross: sprites (flowers, grass). liquid: water/lava. boxes: slabs, stairs, fences, doors, beds
  (per-box face textures, state-dependent textures like the door's `lower.closed`). attached: vines, cocoa.
* Collision and selection boxes from `geometry` (3D, rotated by facing), plugged into `Physics` collision (the player hitbox samples
  in `Pcol`, which currently treats every solid block as a full cube) and the block highlight / raycast.
* Note the world is a cube-sphere: blocks are not perfect cubes (radial walls, curved tops). Boxes are in block-local units and mapped
  through the same corner mapping the mesher uses.

## Phase 4 - Instance state, items, UI, behaviours (done)

Status: done as listed (creative rules). State words + block entities are kept in tables keyed like player edits (survive chunk
regeneration); phase 5 moves them into chunk files. Rotation from phase 3 is done here. Sounds have IDs only; entity definitions are an
empty, checked file.

* Block instance state, two tiers, both declared in `instance_state`:
  - small fields (boolean, enum string, small integer: door open/facing/half, water level 1-8, slab upper/lower) are packed into a
    16-bit state word per block, `ushort[]` allocated lazily per chunk (stateless chunks pay nothing);
  - large fields (inventories, energy, water tanks, progress numbers) live in a BlockEntity object in the chunk's dictionary
    (furnace, chest, oxygen generator).
  - `stateless: true` blocks (dirt) have neither. The editor/loader checks that packed fields fit in 16 bits.
  - Finite water levels (`WL` dictionary in `Water.cs`) become the water block's state.
* Items: `ItemStack {itemId, count, state}` (bucket fill 0-8 out of 8, durability...). Player inventory (hotbar 9-10 + 27 slots)
  replaces the current block hotbar (`UI.HB`). The E palette lists items; `places_block` places the block.
* UI system driven by `ui_definitions.txt`: window, slot, slot_grid, label, progress_bar, ui_panel; drag & drop, split stacks.
  Built-in UIs: `player_inventory`, `chest`, `furnace`, `oxygen_generator`. Right-click a block with `ui_id` opens it.
* Behaviours: small C# classes selected by name in the definition (`"behavior": "door"`) with hooks OnPlace, OnBreak, OnUse, OnTick,
  OnNeighborChanged. Adding content = a definition (+ a small class only if no existing behaviour fits). First set: `door`, `bed`
  (2-block multiblocks: breaking one part removes all, using a door toggles both halves), `container` (chest), `smelting` (furnace),
  `oxygen_generator` (energy + water in, oxygen out - example for KSP / industrial-mod machines), bucket item.
* Multiblocks: `multiblock.parts` with 3D offsets `[x,y,z]` relative to facing; placement checks every part fits.
* New definition files: `sound_definitions.txt` (id, id_name, file; blocks/items reference `sounds: {break, place, step, use}`;
  nothing plays yet), `recipe_definitions.txt` (smelting, later crafting), `entity_definitions.txt` (format reserved, empty for now).

## Phase 5 - Saving (whole chunks)

* `worlds/<name>/world.json` (seed, radius, version, player position, inventory, time) + region files (8x8x8 chunks each, compressed).
* Every chunk that was touched (edit, water flow, state change) is saved in full: 2-byte block data + state words + block entities.
  Untouched chunks are regenerated from the seed instead of stored (keeps saves small).
* Autosave every minute and on exit. Start screen: world list (continue / new / delete).

## Phase 6 - AssetEditor for 3D (Python/Tkinter, same app)

* Block definition form: model picker, 6 face-texture slots with thumbnails (+ "all" and "top/side/bottom" shortcuts), and a 3D
  isometric preview of the block with its textures, including state variants (door open/closed).
* Multiblock part editor (offset + state per part), instance-state field editor (type, default, values, min/max, packed vs block
  entity, 16-bit check), render flags, 3D geometry presets (full, slabs, stairs, fence, door, bed, no collision), behaviour picker,
  sound slots.
* New tabs: Sounds, Recipes, Entities (placeholder). "Generate item icon from block" renders the block isometrically into its
  32x32 item texture.
* Export the 7 files + sound / recipe / entity files. Old 2D projects still load.
* README with every key and complete examples: dirt, grass, flower, slab, door, bed, chest, furnace, oxygen generator.

---

## Checks after every phase

* World fingerprint unchanged for the same seed (phase 2+: after the ID mapping).
* Screenshots before/after (`--bench N --shot file.png`, `--hour`, `--yaw`, `--pitch`, `--up`, `--set name=value` exist already).
* Bench FPS and chunk load time not worse (`--bench` prints FPS and the PROF chunk pipeline timings).
* Automated tests (bench hooks): place/break multiblocks, open a chest and move items, furnace smelting, save -> reload gives identical
  chunks.

## Defaults (unless the user says otherwise)

1. Restructure depth: folders + classes now, no one-shot rewrite into World/Chunk instances.
2. Game mode: creative-style for now (infinite blocks from the palette, breaking gives no drops; chests, furnaces and machines work).
   Survival rules (drops, item consumption) behind a game-mode switch later.
3. 2-byte block IDs double chunk memory (about 100 MB -> 200 MB at render distance 12); accepted for now, compress later if needed.

## Notes about the current code

* Backup of the old OpenGL version: `..\CubePlanet_OpenGL_backup`.
* Settings: `%APPDATA%/CubePlanet/settings.json`; shader / pipeline / cloud noise cache: `%APPDATA%/CubePlanet/vkcache`.
* Meshing runs on worker threads (`Chunks.cs` GenWorker, `Renderer.cs` ChunkWork); block lookups used by workers go through
  `chunkMap` and `[ThreadStatic]` scratch fields - keep that thread safety when moving code.
* Chunk vertices are packed (16 bytes, see `Mesher.cs` Pack and `MeshData` doc comment).
