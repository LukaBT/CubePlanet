# Cube Planet (C# / .NET 8 / Vulkan)

Desktop port of `CubePlanet.html`: a round, fully editable voxel planet built from a cube-sphere with
polar "bands", a Cartesian core, caves, biomes, trees, villages and other structures, distant LOD terrain,
sun shadows, day/night sky with stars, aurora, clouds, atmosphere limb glow, ocean waves, glass/ice shading
and radial-gravity player physics.

## Run
```
dotnet run -c Release                       # start screen lets you type radius + seed
dotnet run -c Release -- --radius 1200 --seed 1337
```
Requires the .NET 8 SDK and a GPU/driver with Vulkan 1.1. Packages (OpenTK windowing, Silk.NET Vulkan + shaderc) are restored from NuGet.
The previous OpenGL version is kept in `../CubePlanet_OpenGL_backup`.
Settings are saved to `%APPDATA%/CubePlanet/settings.json` (or the platform equivalent).

## Build a single exe
```
dotnet publish -c Release            # Windows: win-x64 is the default
dotnet publish -c Release -r linux-x64   # or osx-arm64, osx-x64, win-arm64 ...
```
Output: `bin/Release/net8.0/<rid>/publish/CubePlanet.exe` - one self-contained file (no .NET install needed, glfw3.dll is
embedded and unpacked to a temp folder on first launch, no .pdb) plus the `Assets` folder next to it (ship both). Delete the old `bin/` and `obj/` folders first if you built before.

## Controls
WASD walk, Space jump (double-tap = fly), Ctrl / double-tap W sprint, Shift sneak / fly down,
mouse wheel or 1-9,0 hotbar, LMB break (hold repeats), RMB place / use (doors, beds, chests, furnaces: one use per click;
Shift + RMB places against them), MMB pick block, V player / free camera, F fly (free cam: level), O planet orbit view
(wheel / W,S zoom, zoom in to land), E inventory + item palette, Esc or Tab settings (Esc closes an open window first),
F11 fullscreen, arrow keys also look.
In windows: LMB pick up / put down / swap a stack, RMB take half / put one, Shift + LMB move a stack between the block and
your inventory; in the palette LMB takes a full stack, RMB one item, clicking it with a stack on the mouse deletes that stack.
Creative rules for now: placing does not use up items, breaking drops nothing (a chest's contents are lost).

## Code layout
The old single `static partial class W` is split into static classes by area; `GlobalUsings.cs` imports them all (`global using static`), so code
keeps using short names (`R`, `chunks`, `Blk(...)`, `C`, `P`, `SET`, ...). A class's own members and locals still win over those imports.
```
Program.cs, Game.cs, Settings.cs, GlobalUsings.cs
Assets/                the 7 AssetEditor files (blocks, items, UIs, both atlases + their indexes), copied next to the exe
AssetLoading/          AssetPaths, DefinitionFile (tolerant JSON), TextureDefinitionFile, AtlasLoader, GameAssets (loads everything at startup)
Blocks/                BlockDefinition, BlockBox, BlockRegistry, BlockTables (per-block arrays built from the definitions), BlockIds (named IDs: Stone, Water, ...),
                       StateSchema (instance_state fields), BlockVariant (look / geometry per state word), BlockEntity, Behaviors/ (door, bed, container,
                       smelting, oxygen_generator)
Items/                 ItemDefinition, ItemRegistry, ItemStack, PlayerInventory, RecipeRegistry
Sounds/, Entities/     SoundRegistry (IDs only, nothing plays), EntityRegistry (format reserved)
Worlds/                World (radius, bands, chunks, edits, world creation), BlockAccess (position <-> block, Blk / SetCell), Cube (face axes, seams),
                       Band, Chunk, CoreGrid (Cartesian core), ChunkLoader, ChunkWorkers (worker threads), WaterSim (+ bucket), BlockEditing,
                       BlockPos (band or core cell), BlockStates (state words + block entities), BlockActions (place / break / use, multiblocks, ticking)
Generation/            Noise, Terrain (TerrainGen: heights + biomes), Caves, Columns (caches), Strata, Ores, ChunkGenerator, Features, Structures, GroundCover
Physics/               Camera, Player (PlayerPhysics), Collision, camera / player state, RayHit
Rendering/             Renderer (pipelines + frame), FrameUniforms, Highlight, Glsl (loads the embedded shaders)
Rendering/Chunks/      Mesher, ChunkMeshes (upload + mesh jobs), LodTerrain, mesh types
Rendering/Effects/     Sky (sun, time, atmosphere), Shadows, CloudNoise, NoiseVolume
Rendering/Vulkan/      Gpu as partial files: Gpu (device), Swapchain, RenderPasses, Descriptors, Memory, Images, Frame, ShaderCompiler, Pipelines
Rendering/Shaders/     .glsl / .vert / .frag sources, embedded into the exe as "shaders/<file>"
UI/                    UI as partial files (Font, Draw2D, Hotbar, Screens (windows from ui_definitions), SettingsPanel, Compass, StartScreen) + Hud, UiRegistry
Util/                  V3, M (math)
Diagnostics/           Fingerprint (--fingerprint), SelfTest (--selftest, --showcase), Png (screenshots)
checks/                check.ps1, fingerprint references, reference screenshots
```

## Assets (blocks, items, textures)
Everything about blocks comes from `Assets/`, in exactly the format the Python AssetEditor exports: `block_texture_atlas.png`,
`item_texture_atlas.png`, `block_texture_definitions.txt`, `item_texture_definitions.txt`, `block_definitions.txt`, `item_definitions.txt`,
`ui_definitions.txt`. They are loaded before anything else; a broken file stops the game with a message naming the file and the problem,
an unknown block name in code (`Blocks.Stone` ...) stops it too, and a texture ID without a texture shows a magenta / black checkerboard.
* Block IDs are 2 bytes (1..65535, 0 = air); chunk data is `ushort[]`. Item IDs are a separate space; item texture ID = item ID.
* Texture layer = block texture ID; atlas rows are flipped on load (row 0 = bottom of a face). Textures must be square, power-of-two sized.
* Today's 117 blocks were exported once from the old procedural textures (identical bytes); each block has an item that places it, plus `bucket`.
* Game keys in `properties.definition` (all optional): `model` (cube / cross / liquid / attached + `attach`: vine / pod / boxes), `random_offset`,
  `waterlogged` (underwater plants), `lod_color`, `render.layer` (opaque / cutout / translucent / liquid), `render.connect_group`
  (faces between equal groups are hidden: leaves, glass, ice), `render.sway` (none / leaves / plant / underwater), `render.emissive`,
  `render.casts_shadow`. `group_tags` used: `log`, `leaves`. The full key list with defaults is the comment at the top of `Blocks/BlockDefinition.cs`.
* Textures: `all`, or `top` / `bottom` / `side`, or per face `north` (-z) / `south` (+z) / `east` (+x) / `west` (-x) / `top` / `bottom`
  (a face uses its own slot, then `side`, then `all`). Logs use this for their ring tops.
* 3D shapes: `"model": "boxes"` with `"boxes": [{"x","y","z","w","h","d", "textures": {...}}]` in block units (0..1, y = up). Boxes are drawn
  through the same curved-cube mapping as full blocks, textures are cut to the box (a slab shows the lower half of the texture).
* `geometry.collision` / `geometry.selection` are lists of 3D boxes (the editor's 2D `{"x","y","width","height"}` form also works and spans
  the full depth). Missing collision = full block for cubes, none for other models; missing selection = full block (none for liquids).
  Player collision, the targeted-block raycast, the highlight outline and block placement all use them.
* `rotation` (none / horizontal / all) is read and checked; turning boxes by the block's facing needs per-block state, which comes in phase 4.
* Example box blocks: oak / stone / cobblestone slab, oak / stone brick stairs, oak fence (post only; connections are not implemented).

## Block state, items, windows, behaviours
* **State** (`properties.instance_state`, see `Blocks/StateSchema.cs`): booleans, strings with `values` and integers with `min` / `max` are packed
  into one 16-bit word per block (checked at load; 0 = every field at its default, so chunks without state allocate nothing); inventories,
  numbers and fields with `"storage": "entity"` live in a block entity. Water's level (1..8) is its `level` state. State words and block
  entities survive chunks being dropped and regenerated (like player edits; phase 5 saves them in chunk files).
* **Rotation**: `"rotation": "horizontal"` / `"all"` adds a `facing` field; boxes, geometry and face textures turn with it. `placement_facing`:
  `toward_player` (a furnace's north texture faces you) or `away` (stairs, doors, beds).
* **state_overrides** (`geometry.state_overrides`): `{"when": {...}, "boxes", "collision", "selection", "textures"}` - e.g. an open door's box,
  the upper door half's texture, a lit furnace front.
* **Multiblocks**: `multiblock.parts` `[{"offset": [x, y, z], "state": {...}}]` relative to the facing; placing checks every part fits, breaking
  any part removes all; each part is told apart by its state (`half`, `part`).
* **Behaviours** (`"behavior": "name"`, `Blocks/Behaviors/`): hooks OnPlace, OnBreak, OnUse, OnTick (10 per second for block entities),
  OnNeighborChanged. `door` (both halves open / close), `bed` (sleep through the night), `container`, `smelting` (furnace: input / fuel / output,
  progress, lit front), `oxygen_generator` (fuel -> energy, buckets -> water tank, energy + water -> oxygen -> oxygen canisters). A block
  with `ui_id` opens that window on right click.
* **Items**: `ItemStack {item, count, state}`; item state is packed into 32 bits (the bucket's `fill`). Item `fuel_time` (seconds in a furnace),
  `behavior: bucket` + `capacity`. The hotbar and 27 slots are the player inventory; icons come from the item atlas (second texture array).
* **Windows** (`ui_definitions.txt`): `window {width, height}` + widgets `label`, `slot` (`storage {scope: block|player, key}`, `mode: output_only`),
  `slot_grid` (`rows`, `columns`, `slot_size`, `storage.key_pattern` with `{index}`), `progress_bar` (`current_from: block.<number>`, `max` or
  `max_from`, `orientation`, `color`, `label`), `ui_panel` (embeds another UI, e.g. `player_inventory`). Built in: player_inventory, chest,
  furnace, oxygen_generator.
* **More files** (next to the editor's 7, edited by hand for now): `recipe_definitions.txt` (smelting recipes), `sound_definitions.txt` (blocks
  name sounds in `properties.definition.sounds {break, place, step, use}`; nothing plays yet), `entity_definitions.txt` (reserved, empty).
* New content: oak door, red bed, chest, furnace, oxygen generator; items coal, iron / gold / copper ingot, oxygen canister.
* Shaders no longer know block IDs: the mesher writes material flags (sway, emissive, no shadow, ice) into each vertex.
* Editing: open the `Assets` folder in the AssetEditor ("Open project folder"), change things, then **Export** into `Assets/`
  (Save also works but adds the editor's own per-texture PNG folders and project_settings.json there).

## What was ported 1:1
Terrain/biome/cave noise (same seed gives the same planet), column & cave caches, cube-face seam wrapping,
band system, Cartesian core, features (trees, plants, 7 structure types), ground cover, mesher with skylight
propagation and band-seam handling, procedural 16x16 texture atlas (now exported to Assets/), all original GLSL (sky, clouds, aurora,
water waves/foam, glass & ice, LOD terrain + sea, shadow map, atmosphere), LOD quadtree, camera, orbit mode,
player & free-cam physics, editing, HUD, compass / UP-DOWN markers, hotbar, palette, settings panel
(render distance, LOD, FOV, sensitivity, fly speed, shadows, clouds, fog, time of day, day length, axial tilt,
and all wave settings).

## Player collision
Falling uses the same hitbox width as walking (it used to be 0.03 narrower per side, which let you drop into gaps the walking box does not
fit through, e.g. digging down while standing near a block's edge, and then you were stuck inside the walls). As a safety net, a player found
overlapping blocks (a chunk finished loading around them, an edit) is pushed out by the smallest free move: sideways first, then upwards.
Tested by dropping the player into dug shafts at 780 positions on radius 800 / 1200 / 20,000 / 70,000 planets: 0 stuck (was ~22%).

## Water
Finite water flow is back, ported from the original: every water block is 8 units, water falls, spreads and equalises, smooths slopes, and
equalises through pressure (U-tubes, tunnels). Oceans and lakes cost nothing until disturbed. Hotbar slot 9 is the bucket again
(break = scoop up to 8 units, place = pour). The water block (slot 10) places 8 units that flow.
Water surfaces are closed everywhere: where a full-height water cell (under water, ice or a block) meets open water whose surface sits
lower (~0.8), the step between them gets a face, and underwater plants (seagrass, kelp, ...) count as water for all surface checks and
get water faces themselves, so they no longer leave holes or gaps in the surface. Waves only move surface vertices, so every surface corner that touches
something other than open water (a block, ice, a capped or higher water column) is pinned: waves fade out within one block of edges
instead of pulling the surface away from the faces next to it.

## Deliberately left out (as requested)
* Weather (rain, snow, wind sway control, fog/storm presets, lightning, thunder, weather sounds, snow cover / wet look).
  The shaders still contain those uniforms; they are fixed to a clear sky with a few clouds, a light breeze that drifts
  the clouds and gently sways leaves, and calm-ish seas.
* Touch controls (this is a desktop build). Mouse capture uses native cursor grabbing, so the "drag to look" fallback is gone.

## Renderer (Vulkan)
* Window/input: OpenTK (GLFW, no GL context). Rendering: Vulkan via Silk.NET. The GLSL is compiled to SPIR-V at startup with shaderc and
  cached in `%APPDATA%/CubePlanet/vkcache` (plus a Vulkan pipeline cache), so only the first start compiles anything.
* Reverse-Z depth (D32 float, infinite far plane): no z-fighting at planet scale.
* All per-frame values sit in one uniform buffer; per-draw values are push constants. Chunk meshes live in pooled 64 MB GPU blocks
  (no per-chunk allocations), uploaded through a staging ring, freed once the GPU is done with them. All LOD patches share one index buffer.
* Chunk vertices are packed into 16 bytes (was 40) and use 16-bit indices when possible: about 2.5x less mesh memory and upload bandwidth.
* Same view, same settings as the old OpenGL build on an RX 5700 XT: 102 FPS (OpenGL) vs 273 FPS (Vulkan).

## Graphics settings (Esc / Tab -> GRAPHICS)
**Render resolution** (30-100 %, changes live): the 3D world is drawn at a fraction of the window size and upscaled with light sharpening,
the HUD and menus stay at full resolution. This is the biggest single win on weak / integrated graphics, where the cost scales with pixels.
**First start on integrated graphics** (school / office PCs, laptops) automatically picks the LOW preset at 75 % resolution and render
distance 6, and says so on screen; everything can be changed afterwards.
Preset button: LOW / MEDIUM / HIGH / ULTRA (or mix your own; the button shows CUSTOM). Every feature can be switched off for slower PCs:
* **Shadows** off / near / medium / far: 1-3 shadow cascades. Near = 112 blocks around you, medium covers the whole chunk range,
  far (1800 blocks) lets distant mountains cast shadows on the LOD terrain. Cascades are cached and only redrawn when needed.
* **Ambient occlusion**: darkens block corners and crevices (computed in the mesher; toggling re-meshes the world in the background).
* **Anisotropic filtering**: sharp block textures at grazing angles.
* **Bloom**: glow around the sun, lava, magma and sea lanterns (HDR scene target + 6-level blur chain).
* **God rays**: light shafts around the sun through gaps in clouds / trees.
* **Water reflections (SSR)**: water reflects the terrain using the previous frame (falls back to the sky reflection).
* **Clouds** off / low / high / ultra (24 / 40 / 64 raymarch steps): volumetric cumulus rendered at half resolution in their own pass.
  A 128^3 Perlin-Worley + Worley noise volume (built on first start, cached as `vkcache/cloudnoise_v1.bin`) gives the shapes;
  a large-scale weather pattern at two non-matching scales decides where clouds form, so the sky doesn't repeat. Flat bases, rounded
  towering tops, wispy undersides, light marched toward the sun (Beer + powder + forward scattering), shadows on the ground. They stop
  at terrain (scene distance buffer), and bloom and god rays respect cloud cover, so sun rays shine through the gaps. Cost on an RX 5700 XT
  looking at a cloudy sky: 273 FPS off, 213 low, 201 high, 190 ultra.
* **FXAA**: cheap edge smoothing. **MSAA** 0/2/4/8x (applies on restart; `--msaa N` overrides it).
Measured on an RX 5700 XT at render distance 12: LOW 373 FPS, MEDIUM 334, HIGH 310, ULTRA 253.

## Distant terrain (LOD)
LOD patches are built on thread-pool threads; until a patch is ready its parent patch is drawn, so turning around no longer freezes
(worst frame after a 180 degree turn on a radius-70,000 planet: 224 ms before, 6 ms now). The LOD terrain sits behind the voxel terrain
through a small proportional depth offset in the vertex shader (the slope-scaled depth bias used before pushed steep patch skirts and horizon
patches to the far plane, where the sky was drawn over them: the white seam lines on big planets). The LOD sea keeps real skirts too.

## Command line
`--radius N --seed N`, `--msaa 0|2|4|8`, `--novsync`, and for testing: `--bench SECONDS` (starts immediately with a fixed free camera,
ignores input, prints FPS + chunk pipeline timings, does not save settings), `--shot file.png`, `--set name=value` (any settings field,
this run only, e.g. `--set ssr=0`), `--hour H --yaw DEG --pitch DEG --up N` (bench camera), `--turnat S` (turn 180 degrees after S seconds and report the worst frame), `--syncmesh` (mesh on the main thread, for debugging).
`--selftest` (no window): rule checks (slab collision, the player landing on a slab, raycast / selection boxes, placing next to a slab, box meshing, doors as multiblocks, facing of stairs / beds / furnaces, moving items in a chest window, furnace smelting, the oxygen generator, state surviving chunk regeneration, water level as state); exit code 1 on failure. `--showcase` (with `--bench`): places a row of example blocks in front of the camera; `--showui inventory|chest|furnace|oxygen_generator` opens a window over it.
`--fingerprint [--radius N] [--seed N] [--fprd CHUNKS]` (no window): generates every chunk around the bench spawn point (default 12 chunks) plus the core and prints a hash of the block data. `checks/check.ps1` builds, compares the hashes with `checks/fingerprint.txt`, runs the self-test and takes the screenshots in `checks/out` (references in `checks/ref`); refactors must keep the hashes identical.

## World generation (v2)
* **Terrain:** erosion noise decides flat vs rugged regions, domain-warped ridged noise gives sharper peaks, valleys are cut
  into highlands and worn-down uplands get terraced plateaus. Tuning lives in `World.Terrain()`.
* **Caves (Caves & Cliffs style):** cheese caverns, spaghetti tunnels with varying thickness, a wide tunnel network in
  some regions, and ravines that open to the surface in lowlands. Caves reach about 100 blocks below sea level
  (`CAVB` in `World.cs`); `World.CaveS()` holds all the thresholds.
* **Aquifers and lava:** cave air below a per-region water level is flooded; the deepest caves hold lava lakes (static blocks, self-lit, solid).
* **Deepslate:** stone changes to deepslate 56-72 blocks below the surface (and the whole core is deepslate).
* **Ores:** coal, copper, iron, gold, redstone, lapis, diamond and (mountains only) emerald, each in its own depth range with
  a deepslate variant. Vein counts and depth ranges are the `ORES` table in `Chunks.cs`.
* New blocks are ids 58-75 (all in the block palette, E).

## Biomes v3 (new)
New blocks are ids 76-117 (all in the E palette). New noise slots 27-42 are used by the biome code (`World.Terrain()`), the biome list is `BNM`.

**Land:** Lush Forest (big vine oaks, flowering azalea trees, moss, bluebells), Cherry Grove (cherry trees, pink petals), Oasis (pool + grass ring + palms inside the desert),
Red Canyon (terraced plateaus cut by winding slot canyons with banded strata), Snowy Taiga, Old Growth Taiga (giant 2x2 spruce, podzol, berry bushes), Glacier Peaks (snow / packed ice / blue ice),
Bamboo Jungle, Flower Meadow, Stony Shore (cold gravel coast with boulders), Tropical Beach (pale coral sand + palms), Volcano (basalt cone, ash, obsidian rim, lava crater).

**Sea:** the sea floor now has sand bars, ridges and deep trenches. Warm Ocean (lagoons, tropical islands with palms), Coral Reef (coral trees / domes / tubes in 5 colours, fans, sea pickles, seagrass),
Kelp Forest (cold sea, tall kelp), Frozen Ocean (icebergs), Deep Ocean (trenches, clay), plus sunken ruins (prismarine + sea lanterns) next to the shipwrecks.

**Mechanics:** underwater plants (ids 110-117) live inside the water body: water faces next to them are hidden, you can still swim through them, breaking one leaves water (not an air pocket),
they sway gently in the shader. Magma blocks and sea lanterns are self-lit like lava.

## World-size scaling (radius 200 - 10,000,000)
`InitWorld` sets `BSC` (biome-region size, ~sqrt of radius), `LSC` (mountain / river / ocean-relief size, ~radius^0.3), `LCN` (continent size, ~radius^0.85) and a taller `HS` / deeper `OD` on big worlds.
Small things (oases, islands, icebergs, coral, ice patches) keep their size. A little small-scale roughness is added back on big worlds so terrain does not turn smooth.
Chunk keys were widened (19/18/19 bits) and block-edit/water/cave keys are now 64-bit hashes, which is what allows radii past 20,000.

## Chunk loading
Worker threads (cores - 2, max 14) do both jobs, meshing first: block data generation (terrain, caves, ores, trees, structures) and meshing
(faces, skylight, ambient occlusion). A chunk is meshed only once its 26 neighbours have block data, so its lighting is right the first time
(no second relight pass). The main thread only uploads finished meshes, so loading costs it almost nothing. Edited chunks are still meshed on
the main thread, so block edits show up the same frame. The scan for which chunks to load also runs in the background. Chunks behind you are
meshed too (at lower priority), so turning around doesn't show holes.
Measured (render distance 12, 2967 chunks): everything generated and meshed 3.5 s after start, 0 ms of meshing on the main thread
(before: 1446 visible chunks meshed on the main thread at about 2 ms each, inside a 5-9 ms per-frame budget).
Thread safety: terrain result slots and the block-lookup scratch values (`P*`, `N*`, `W*` results, the one-entry chunk cache) are
`[ThreadStatic]`, workers find chunks through a `ConcurrentDictionary` mirror (`chunkMap`), player edits and water levels are read under locks,
and each chunk is claimed through `Chunk.gst` (generation) / `Chunk.mst` (meshing).
