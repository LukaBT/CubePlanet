# Cube Planet (C# / .NET 8 / Vulkan)

Desktop port of `CubePlanet.html`: a round, fully editable voxel planet built from a cube-sphere with
polar "bands", a Cartesian core, caves, biomes, trees, villages and other structures, distant LOD terrain,
sun shadows, day/night sky with stars, aurora, clouds, atmosphere limb glow, ocean waves, glass/ice shading
and radial-gravity player physics.

## Run
The start screen lists saved worlds (PLAY / DELETE, delete asks for a second click) and creates new ones (name, radius, seed; Tab switches
field, Enter creates). Worlds are saved every minute, from the settings panel (SAVE NOW) and when the game closes.
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
your inventory; in the palette LMB takes a full stack, RMB one item, clicking it with a stack on the mouse deletes that stack, the wheel
scrolls, and the search box (click it, type; Esc / Enter leaves it) filters blocks and items by name.
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
                       BlockPos (band or core cell), BlockStates (state words + block entities), BlockActions (place / break / use, multiblocks, ticking),
                       Saving/ (WorldSave: world.json, world list, load / save; RegionFile: 8x8x8-chunk region files)
Generation/            Noise, Terrain (TerrainGen: heights + biomes), Caves, Columns (caches), Strata, Ores, ChunkGenerator, Features, Structures, GroundCover
Physics/               Camera, Player (PlayerPhysics), Collision, camera / player state, RayHit
Rendering/             Renderer (pipelines + frame), FrameUniforms, Highlight, Glsl (loads the embedded shaders)
Rendering/Chunks/      Mesher, ChunkMeshes (upload + mesh jobs), LodTerrain, mesh types
Rendering/Effects/     Sky (sun, time, atmosphere), Shadows, CloudNoise, NoiseVolume
Rendering/Vulkan/      Gpu as partial files: Gpu (device), Swapchain, RenderPasses, Descriptors, Memory, Images, Frame, ShaderCompiler, Pipelines
Rendering/Shaders/     .glsl / .vert / .frag sources, embedded into the exe as "shaders/<file>"
UI/                    UI as partial files (Font, Draw2D, Hotbar, Screens (windows from ui_definitions), SettingsPanel, Compass, StartScreen) + Hud, UiRegistry
Util/                  V3, M (math)
Energy/                EnergyNet (cable networks, balancing), WaterMachines (turbine, pump, battery, spring, pipe water paths),
                       Kinetics (rotation networks: wind turbine, shaft, generator; coal generator)
Diagnostics/           Fingerprint (--fingerprint), SelfTest (--selftest, --showcase), EditBench (--benchedit), Png, item icon sheet (--iconsheet) (screenshots)
checks/                check.ps1, fingerprint references, reference screenshots
AssetEditor/           the Python / Tkinter asset editor (block3d.py = 3D rules + icon renderer, editor3d_tabs.py = Block 3D / Sounds / Recipes / Entities)
```

## Assets (blocks, items, textures)
Everything about blocks comes from `Assets/`, in exactly the format the Python AssetEditor exports: `block_texture_atlas.png`,
`item_texture_atlas.png`, `block_texture_definitions.txt`, `item_texture_definitions.txt`, `block_definitions.txt`, `item_definitions.txt`,
`ui_definitions.txt`. They are loaded before anything else; a broken file stops the game with a message naming the file and the problem,
an unknown block name in code (`Blocks.Stone` ...) stops it too, and a texture ID without a texture shows a magenta / black checkerboard.
* Block IDs are 2 bytes (1..65535, 0 = air); chunk data is `ushort[]`. Item IDs are a separate space; item texture ID = item ID.
* Texture layer = block texture ID; atlas rows are flipped on load (row 0 = bottom of a face). Textures must be square, power-of-two sized.
* Room: both atlases have 16 x 128 cells = 2048 textures (blocks 256 x 2048 px, items 512 x 4096 px); a texture's position is written
  in its index, so the image can grow further. Block and item IDs go up to 65535. The GPU holds the textures as texture arrays, one
  layer per texture up to the highest ID used (no cost for empty cells); most desktop GPUs allow 2048+ layers, and the game says so
  clearly at start if a GPU allows fewer. Item pictures sit at id % 16, id / 16 by convention; the bucket keeps id 200, so new items
  take the next id above the highest one.
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
* Example box blocks: oak / stone / cobblestone slab (`half` bottom / top, chosen like stairs from where you click), oak / stone /
  cobblestone vertical slab (half-thick, rotating, stands against the side you look at), oak / stone brick stairs, oak fence.
* Any block with a packed `half` field whose values are `bottom` and `top` is placed upside down by an upper-half click (BlockActions.Place).
* **Stairs** (`behavior: stairs`): `half` bottom / top (placed upside down when you click the upper half of a side or the underside of a block)
  and `shape` straight / inner_left / inner_right / outer_left / outer_right, joined like Minecraft's from the neighbouring stairs; the boxes
  of each shape are state_overrides. **Fences** (`behavior: connecting`, `connects_to: ["fence"]`): `north` / `east` / `south` / `west`
  arms towards fences and full solid blocks. The **bed** has a solid mattress and legs at the outer corners.

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
* Editing: `AssetEditor/` in this repo is the Python editor extended for 3D blocks (Block 3D tab with face-texture thumbnails, a 3D preview
  per state, state / multiblock / geometry editors, item icons from blocks; Sounds, Recipes, Entities tabs). File > Open Cube Planet Assets
  folder, edit, File > Export into `Assets/`. See `AssetEditor/README.md` (every key, complete examples). `python AssetEditor/test_editor.py`
  exports to a temp folder; `CubePlanet --assets <folder> --selftest` runs the game on such an export.

## Saving
`%APPDATA%/CubePlanet/worlds/<name>/`: `world.json` (name, version, seed, radius, camera, player, sun angle, inventory, and the block / item ID
tables of the save), `regions/r.<band*6+face>.<cx/8>.<cy/8>.<cz/8>.bin` (up to 8x8x8 chunks each, every chunk deflate-compressed) and `core.bin`
(edits inside the Cartesian core). Every touched chunk (player edits, water flow, state changes, block entities) is stored whole: 2-byte block
IDs, state words and its block entities. Untouched chunks are not stored; they are regenerated from the seed. Saved chunks are used exactly as
saved (a later change to world generation does not change what was built), and IDs are remapped by name when the asset files changed.

## What was ported 1:1
Terrain/biome/cave noise (same seed gives the same planet), column & cave caches, cube-face seam wrapping,
band system, Cartesian core, features (trees, plants, 7 structure types), ground cover, mesher with skylight
propagation and band-seam handling, procedural 16x16 texture atlas (now exported to Assets/), all original GLSL (sky, clouds, aurora,
water waves/foam, glass & ice, LOD terrain + sea, shadow map, atmosphere), LOD quadtree, camera, orbit mode,
player & free-cam physics, editing, HUD, compass / UP-DOWN markers, hotbar, palette, settings panel
(render distance, LOD, FOV, sensitivity, fly speed, shadows, clouds, fog, time of day, day length, axial tilt,
and all wave settings).

## Player collision
The player walks up ledges up to 0.6 blocks high without jumping (stairs, slabs), like Minecraft's step height: when walking is blocked on
the ground, the move is tried 0.6 higher and the player settles onto the step if that gets further. Full blocks still need a jump. The camera glides up after
the step (the body moves at once), so stairs feel smooth. Collision boxes may reach y = 1.5: fences use that, so they cannot be jumped over.
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

Performance: the simulation runs on the main thread (it changes the same block data the player, physics and renderer use) but costs at most 1.5 ms per frame: each tick's work (the cells active when the tick starts, at most 400, and 6 pressure searches; 5 ticks/s) is spread over frames. A cell that moved waits for the next tick, so water falls 5 blocks/s and spreads about 3 blocks/s (Minecraft: about 4). Its changes are meshed by the worker threads (a chunk version redoes a mesh that went stale meanwhile), and water only moves in loaded chunks. `--benchwater` (no window) checks generated water is at rest and times a spreading pool.

## Hydro power and energy
**Blocks** (all in the creative palette, search "water" / "cable"):
* **Cables** join machines into an energy network (they connect to blocks with the group tag `energy`, in all 6 directions, and to
  each other); machines touching each other are connected too. Each cable block carries a limited amount: **Copper** 64 E/s, **Gold**
  256, **Diamond** 1024, **Superconductor** 8192. Energy is routed through the network as a maximum flow, so a thin cable limits only
  its own branch and parallel lines add up; a machine makes / takes only what gets through. Looking at a cable shows its flow, e.g.
  "64.0 / 64 E/S AT ITS LIMIT". (Energy/CableFlow.cs)
* **Liquids**: water, **lava**, **oil** and **liquid oxygen** all flow with the same finite-liquid rules (8 units a block, never created
  or lost). They do not mix: a heavier liquid sinks under a lighter one (water under oil). Lava is thick (moves every 6th tick) and glows;
  lava touching water freezes (a full cell to obsidian, else cobblestone) and boils a unit of the water off into the water cycle. Liquid
  oxygen open to the air boils away (about 0.2 units a second per open cell); keep it in a tank. The bucket holds one liquid at a time.
  Pipes, pumps, turbines, valves and tanks work with any liquid (one liquid per tank); energy uses the liquid's density (oil 850 kg/m^3,
  lava 3100, liquid oxygen 1141), so the same drop of oil gives 85 % of water's energy.
* **Oil reservoirs**: lenses of oil 35..90 blocks below the surface under a cap of **Oil Shale** (about one per 300 x 300 blocks). Dig
  or drill down to the shale, run a liquid pipe into the oil and pump it up into a tank. The oil only moves once something reaches it.
* **Liquid Pipe** carries water for turbines and pumps. A pipe opens at its ends only (the side opposite its single connection).
* **Liquid Turbine**: water flows from its back (intake) to its front (outlet; it faces you when placed, up or down when you look up or
  down). Energy = units x head x 0.1 x 0.8 E, head = the intake's water surface over the outlet's (a pipe from a lake's bottom down to the
  turbine is a penstock). At most 16 units (2 blocks) of water a second.
* **Liquid Pump**: lifts water from its back to its front (or to the top end of a pipe column) for units x lift x 0.1 / 0.75 E.
* **Battery**: 2000 E, 50 E/s in or out, 95 % each way.
* **Spring**: water comes out of its front (right click: 1, 2, 4, 8, 16 units/s) for streams, waterfalls and fountains you build.
* **Drain**: takes up to 32 units/s from the cell in front of it back into the water cycle (where a turbine's water goes without a river
  or sea nearby).
* Pipe and machine outlets push water into the body they open into (filling its column, then its lowest surface cells nearby) but never
  over a basin's walls: a pump stops when its tank is full.

`--demoworld NAME [--demosecs S]` (no window) builds a saved world with two plants next to spawn and runs them for S seconds (default 200):
plant A (a reservoir on pillars fed by 3 springs, 3 penstocks to 3 turbines on "on", a tailwater basin with drains, 4 batteries and an
oxygen generator) and plant B, joined by a cable (a pool, a pump on auto lifting water 19 blocks into a tank, a turbine on auto letting it
back down). It refuses to touch an existing world folder.
* The **oxygen generator** also takes energy from a network now (besides burning fuel).

Right click a turbine or pump to switch **auto / on / off**. Auto is the useful one:
* the network is balanced every 0.1 s: production of turbines set to "on" against demand (machines, pumps set to "on");
* surplus charges batteries, then runs pumps on auto (pumped storage: water goes up while energy is spare), the rest is wasted;
* a deficit opens turbines on auto (a dam keeps its water until energy is needed: the lake is the storage), then batteries; if still short,
  every consumer gets the same share.

Pump up + turbine down gives back about 50 % (0.75 x 0.8 and the batteries): storage, never free energy. Looking at a machine shows its
mode, head / lift, flow and power in the HUD.

**Water cycle** (no water is ever created): the world's water + `WaterSim.Reservoir` (saved in world.json) stays constant. Springs draw
from the reservoir; water arriving on top of the sea goes back into it. The reservoir starts at about an ocean's volume, so the sea never
visibly changes.

**Flow**: every move of the water simulation (and of machines) is measured per cell (Worlds/WaterFlow.cs, units/s smoothed over 1.5 s).
The mesher puts each moving cell's flow into the free vertex bits of its water faces (direction in 8 steps + strength 0..7 on top faces,
falling strength on side faces) and the water shader draws it: ripples and foam streaks drifting with the flow, faster and whiter the
stronger it is, and white streaks running down waterfalls. A steady stream does not re-mesh: chunks are re-meshed (in the background) only
when the drawn flow of a cell changes. Liquid turbines and pumps idle while their chunk is unloaded (water only moves in loaded land).

## The grid: voltage, transformers, power lines
The grid is AC with three voltage levels (Energy/Grid.cs): **LV** (x1, all machines), **MV** (x8), **HV** (x64).
* Every part of a network between transformers has one level, set by the transformer sides touching it (LV if none).
* A cable carries a *current*: its capacity times the level, so copper carries 64 E/s at LV, diamond 1024 at LV / 8192 at MV.
* **Voltage ratings** (insulation): copper and gold cable take LV only, diamond and superconductor up to MV, wires and wire connectors
  up to HV (in real grids high voltage goes overhead, where air is the insulation). A cable on a higher level than its rating carries
  nothing (HUD: ITS INSULATION FAILS). So HV is only possible on power lines.
* Losses are I^2 R: a block at its full current loses a share of what it carries (copper 0.3 %, gold 0.2 %, diamond 0.1 %, wire 0.01 %
  per block of span; wire current 512). The same energy at a higher level is a smaller current, so it loses (level)^2 less: 80 E/s over
  60 blocks arrives as 52.5 E/s on copper at LV (capped at 64, then losses), 76.8 E/s on diamond at MV (after two transformers).
* **Superconductor** has no loss but needs cooling: 0.05 E/s per block while its network has a source (100 blocks: 5 E/s), so it pays
  off for big power over short distances; over 2000 blocks the cooling alone is 100 E/s.
* **Transformer LV-MV** (up to 1024 E/s) and **Transformer MV-HV** (8192 E/s): the back is the low side (green / yellow face), the front
  the high side (yellow / red). LV-MV loses 2 %, the big MV-HV 0.5 % (like real large transformers). A side touching a part at another level (two transformers fighting) passes nothing.
* **Machines run on LV only**: on an MV / HV part they do nothing (HUD: WRONG VOLTAGE). Step down with a transformer first.
* **Power lines**: a **Pylon** (5 blocks tall) holds a wire at its top; a **Wire Connector** (an insulator, joins cables like a cable)
  holds one anywhere. Right click two of them with the **Wire Spool** to string a wire (at most 48 blocks, counted in blocks); again on
  the same pair removes it. A wire is like a cable as long as its span: 512 E/s of current x the level (32768 E/s at HV), 0.01 % loss
  per block at full current: the best way to go far. Wires are drawn sagging, saved with the world, and fall when an end is broken.
* Looking at a cable shows its level, flow, capacity and loss; at a pylon / connector its wires; at a transformer its sides and flow.
* **The grid works in unloaded land.** Cables, machines, transformers, pylon tops and rotation parts are remembered when their chunk
  unloads (Energy/Grid.cs GridCache, saved in world.json as `grid_cache`), so a long power line, a far wind farm or a coal plant keeps
  delivering wherever you are. Machines (block entities) always stay in memory; only liquid turbines and pumps need their chunk loaded.
* `--demogrid NAME [--demosecs S]` (no window) builds a world with a wind farm stepping up to MV, wires over 3 pylons to a town 80 blocks
  away (transformer back to LV, batteries, an oxygen generator), and beside it 4 coal generators feeding 110 blocks of plain LV copper:
  the MV line loses almost nothing, the LV line a third of what it carries.
* `--demogrid2 NAME [--demosecs S]` (no window) builds a world with every level and a very long line: a plant (4 large wind turbines,
  8 coal generators, a diamond LV bus) with an LV-MV-HV substation; an HV line on pylons standing on the real ground (stone foundations
  in water) up to ~1000 blocks long; a town substation HV-MV-LV with 10 batteries and 2 oxygen generators; and from the town's MV bus
  an MV branch line ~160 blocks to a village (LV-MV, 3 batteries). It then reloads the saved world with no chunks loaded and checks
  that the town and the village still get their power.

## Hydraulics with real physics (Energy/WaterMachines.cs)
* **Player gravity** is a game value, 30 m/s^2 (~3 g, like other block games: a snappy 1.35-block jump, 0.6 s in the air). Settings
  (Esc): "Realistic gravity" switches it to 9.81 m/s^2 with a 4.95 m/s jump that still clears a block (1.25 blocks, ~1 s in the
  air). Both scale with the planet's gravity factor. Machines always use real gravity.
* Gravity where a machine is: g = 9.81 m/s^2 x the planet's gravity factor (full on the surface, falling linearly toward the centre
  inside the planet, with the square of the distance out in space). 1 block = 1 m, 1 unit = 125 kg of water, 1 E = 12,262 J (a unit falling 10 blocks on the surface).
* Energy per kg water gains or gives between two ends: g x the height difference + the pressure difference / 1000 (a sealed tank's
  pressure; open water 0). A turbine gets 80 % of what the water gives; with less gravity the same drop gives less, a tank's pressure
  the same anywhere. Heads are shown in blocks at the local gravity (and in J/kg).
* **Suction**: a pump can only pull water up to itself as far as the outside air pushes it: (1 atm + a source's pressure - water's
  vapour pressure - a margin against cavitation) / (1000 x g) = ~7.5 blocks on the surface, twice that at half gravity. Put pumps low
  (down in the water) and push up.
* **Pump curve**: the Liquid Pump and the Mechanical Pump are centrifugal: they give water at most a fixed energy per kg (60 blocks of
  head at surface gravity, so 120 at half gravity); their flow falls as the head rises (16 units/s at no head, 0 at the maximum) and
  their efficiency is best at ~60 % of the maximum (75 %), poorer at low and high heads. Against a sealed tank they stall near 6 bar.
* **High-Pressure Pump** (piston, positive displacement): a steady 4 units/s up to 265 blocks of head at surface gravity (~26 bar),
  85 % efficient: the pump for filling pressurized tanks (up to their 20 bar).
* **Pipes have friction** (Darcy-Weisbach, f = 0.015): each pipe block of bore D loses f/D x v^2/2 J/kg, an open end 0.5 x v^2/2,
  v = flow / area. **Liquid Pipe**: 0.35 m bore; **Large Liquid Pipe**: 0.8 m bore (5x the area, ~1/30 the friction per unit of flow).
  A turbine runs at the flow giving the most power (friction = a third of its head, at most 16 units/s): 20 small pipes from a
  2 bar tank let it take 5.5 u/s, 20 large ones the full 16. A pump must also overcome the friction (its curve meets lift + friction).
  The HUD shows how much of the head the pipes take.
* **Liquid Pipe Valve** (in a pipe line, back to front, one way): water flows on its own when the far end is lower or the near end under
  pressure, as fast as friction allows (all the available energy goes into friction: Q = sqrt(available / K)): a pressurized tank
  pushing water up a pipe like a water tower, a siphon between lakes. A 3 bar tank pushed water 12 blocks up into a basin and
  stopped where its pressure balanced the height. Right click: OPEN / CLOSED.
* The HUD on a pump says why it stops: too high a suction ("CAN'T PULL WATER UP MORE THAN X BLOCKS HERE") or too much head ("TOO HIGH").
  Definition key "pump": {"type": "centrifugal" / "piston", "max_head" (blocks at surface gravity), "flow", "efficiency"}.

## Water tanks, pressure and the compressor (Energy/Tanks.cs)
* **A tank** is a cuboid room up to 12 x 12 x 12 whose six faces are tank blocks (**Liquid Tank Wall**, **Liquid Tank Window**, **Liquid Tank Valve**); edges and
  corners do not matter. A valve forms it (right after placing, then every second; water already inside is taken in). 1 block = 1 m^3 =
  8 units (1 unit = 125 l). Pipes and machines connect at valves (a valve right behind / in front of a turbine or pump, or at the end of
  its pipes). The water is drawn inside, seen through windows; the HUD on a valve shows the tank.
* **Real physics**: a sealed tank holds water and an air cushion following Boyle's law (p x V_air = constant at one temperature), so
  pumping water into it squeezes the air and raises the pressure (half full from empty at 1 bar = 2 bar). Water itself barely
  compresses (bulk modulus 2.2 GPa: half a unit more in a full 27 m^3 tank = 52 bar), so **pressure stores energy, not water**: every
  bar over the outside air is 10.2 blocks of head for a turbine taking from it, and the same lift for a pump pushing into it.
* A **safety valve** lets air out above 20 bar (the tank's rating); a sealed tank can only be filled until then. Right click a valve:
  **VENTED** (open to the air, 1 bar, fills to the brim) / **SEALED**.
* **Compressor** (touching a tank valve): pumps up to 0.5 m^3 of free air a second into the sealed tank, up to its target (right click:
  10 / 20 bar / OFF / 2 / 5), using energy for the isothermal compression work p0 V ln(p) at 70 % (about 13 E/s at 10 bar). The air
  gives that energy back as head: water leaving the tank lets the air expand, the pressure falls.
* Breaking a face (or putting a block inside) **bursts** the tank: its water spills into the room.
* `--demotank NAME [--demosecs S]` (no window) builds a pumped-storage plant: a sealed 5 x 5 x 5 tank at 8 bar with windows, a pump
  lifting water from a pool into it with a high-pressure (piston) pump (coal powered), a compressor on its top valve, and a turbine on its low valve sending the water back
  down into the pool and charging batteries; the hotbar has a multimeter.

## Machines that use energy (Energy/Utilities.cs)
* **Automatic Miner** (20 E/s): digs the 9 x 9 area under it layer by layer (48 deep) into a chest touching it; 2 E + 6 E x hardness
  a block; leaves machines, water and air alone; stops when the chest is full. Right click: ON / OFF.
* **Electrolyzer** (100 E/s) + **Gas Tank** + **Fuel Cell**: water from a liquid pipe or tank valve at the electrolyzer's back (not loose
  water) is split with real numbers (141.8 MJ per kg of hydrogen, 70 %, 9 kg of water per kg): **hydrogen** leaves through its front,
  **oxygen** (half as many mol) through its sides, each along **Gas Pipes** into gas tanks (or a tank touching that side); with no oxygen line
  the oxygen goes into the air. A gas tank is 1 m^3 up to 30 bar (~1230 mol), one gas at a time; right click sets which gas it takes. The fuel
  cell (40 E/s on demand, 55 %, oxygen from the air) burns hydrogen from tanks touching it or on its gas pipes. Round trip ~33 %, but it keeps
  for ever: one full tank of hydrogen = ~133,000 E through the fuel cell.
* **Pipes carry one thing at a time**: a single liquid or gas pipe line holds one liquid / gas (the first to flow claims it; free again after
  60 s of nothing). A **Dual Liquid Pipe** / **Dual Gas Pipe** has two channels, so two liquids (water and oil) or two gases (hydrogen and
  oxygen) can share one line and split into single pipes at the ends without mixing.
* **Charger** + **Electric Drill** / **Chainsaw** (items with a charge, up to 4000 E): the charger fills the tool in its slot (40 E/s,
  95 %). The drill digs 3 x 3 across the face you look at (2 E + 6 E x hardness a block), the chainsaw fells a whole tree (2 E a block);
  what they dig goes into your inventory. The HUD shows the charge of the tool in your hand.
* **Grow Light** (1 E/s): a lamp; while lit, crops within 4 blocks grow twice as fast.
* **Sprinkler**: takes water only from a liquid pipe (with water) at its back (the pipe's far end in water or at a tank valve). The water needs pressure:
  the source's surface (plus a tank's air pressure) must stand above the nozzle, so feed it from a higher basin or a pressurised tank.
  Every 4 s it checks the farmland within a radius of 4 blocks (its row and two below); when any of it is at moisture 6 of 8 or less it
  sprays (you see the drops), filling the dry farmland back to 8: 1 unit of water per moisture level, 0.3 E a unit. Wet soil: no spray.
* **Elevator** (built like a real traction lift): a straight column of **Guide Rails** up one side of the shaft, an **Elevator Motor** (the
  hoist, on the grid) on top of the rails, and an **Elevator Car 1x1 / 3x3 / 5x5** placed against the rails at any height: it hangs on the
  rails from that side (guide shoes, a frame, railings), hangs from a cable to the motor and has a counterweight running down the rails as it
  rises. Its floor is solid and carries you. Floors: the bottom of the rails, the top (2 blocks under the motor) and each **Elevator Stop**
  on a landing beside the shaft (next to the rails or anywhere along the car's sides): the car stops level with the block the stop stands on
  (a stop mounted on a wall at chest height: one block below it). In the car (walking or flying): jump = next floor up, sneak = next floor down; right
  click a stop to call the car, the motor to send it to the bottom. It will not move into blocks in the shaft. Energy: the counterweight
  balances the car, so the motor lifts you: 80 kg x gravity x height / 80 % (10 blocks: 0.76 E), going down gives half back, plus 0.01 E per
  block per car cell; it keeps 50 E from the grid (5 E/s). Breaking the motor gives the car back.
* **Conveyor Belt** + **Extractor**: items really ride on belts (4 per belt, 1.5 blocks a second, the belt surface moves). Belts shape
  themselves from their neighbours: a belt fed from its side becomes a curve; a belt with a belt one up in front climbs to it; a belt below and
  in front of a belt's end takes the items down. At the end items go onto the next belt or into the chest / machine in front (a furnace input
  if it can smelt them, fuel slots for fuel), else they wait. The extractor (0.2 E an item, 4 a second) takes items from the chest / machine
  output at its back and puts them on the belt (or into the inventory) in front. Right click a belt with an item to put it on, with an empty
  hand to take its items. Standing on a flat belt carries you.
* **Grid Status Lamp**: green above half of the grid's storage, yellow above 10 %, red below (or no storage); glows.
* `--demosprinkler NAME [--demosecs S]` (no window) shows the sprinkler: A works (a water tower higher than the nozzle feeds it through a
  liquid pipe), B has a pipe but its basin is lower than the nozzle (no pressure), C stands on a water block (no pipe).
* `--demoutility NAME [--demosecs S]` (no window) builds a world with all of them on a coal grid; the hotbar has the charged tools.
* Units: 1 E = 12,262 J (a unit of water falling 10 blocks on the surface), so 1 E/s is about 12 kW.

## Rotation machines and the multimeter (Energy/Mechanical.cs, Energy/Multimeter.cs)
* Rotation lines now have **loads** besides generators; a line shares its sources' power between them in proportion to what each wants.
* **Gearbox**: joins shafts on all six sides (a turbine's horizontal shaft can turn down a tower, or split to several machines).
* **Millstone** (15 E/s of rotation): iron / gold / copper ore (and deepslate ores) -> 2 dust each, 120 E per ore; dust smelts to an
  ingot (furnace or electric furnace), so ore gives twice the ingots. **Sawmill** (15 E/s): a log -> 6 planks, 60 E each. Both take a
  shaft on any side but the top; windows with input, progress and output. Driven by a wind turbine, a motor or a gearbox line.
* **Mechanical Pump**: lifts water like the electric pump (same cost per unit and lift, up to 16 units/s), driven by a shaft on a side
  across its axis: a wind pump. Right click: ON / OFF.
* **Multimeter** (item): hold it and look at any block of an energy network: the HUD shows the whole network (machines, cable blocks,
  wires, transformers, voltage levels), what it makes and could make, what is used and wanted, losses, storage, and what limits it in
  plain words: machines short of power, cables / wires / transformers at their limit, cables above their voltage rating, generators at
  their 30 E/s with rotation left over, batteries at their 50 E/s charge rate, full or running empty, production nobody takes, machines on
  the wrong voltage, high losses. On a shaft or rotation machine: its line (sources, what the loads want and get).
* `--demomill NAME [--demosecs S]` (no window) builds a world with a large windmill on a tower, a gearbox and a vertical shaft down to a
  gearbox on the ground driving a millstone (iron ore), a sawmill (oak logs) and a wind pump (a pipe down into a pool, a pipe up into a
  raised glass tank); the hotbar has a multimeter.

## Electric machines, lamps, solar (Energy/Electric.cs)
* **Electric Lamp** (0.5 E/s): lights up when powered and gives **block light** around it (level 9 at the lamp, one less per block,
  separate from sky light so it shows at night; Rendering/Chunks/Mesher.cs block light, packed into the top 4 bits of the vertex sky
  word, `lamp` term in common.glsl). Right click: AUTO (lit at night only, like a street light) / ON / OFF. No power: dark.
* **Electric Furnace** (12 E/s while working): smelts the furnace recipes without fuel, twice as fast; slower if the grid is short.
* **Electric Motor**: the reverse of a generator: up to 30 E/s in, 90 % out as rotation at its front (a shaft line, generators or other
  rotation machines). Right click: 100 % / 50 % / 25 % / OFF.
* **Solar Panels**: small (1 block, 3 E/s), medium (2 x 2, 14 E/s), large (3 x 3, 36 E/s) in full sun straight on. Output = that x the
  share of sunlight falling straight on the panel (cos of the angle) with a fade near the horizon; nothing at night. Right click: FIXED (flat) /
  1-AXIS (turns about its north-south axis to follow the sun east to west, 2 % for its motor) / 2-AXIS (also tilts north-south, so it
  faces the sun straight on, 4 %). Tilting stops where the panel's corners would touch the ground (a big panel on its short post tilts
  less). 1-axis more than doubles the output at a low sun in the east or west; 2-axis also wins when the sun stays to the north or south
  (another x2.4 in the self-test). Every part of a big panel joins cables, so panels side by
  side form one array; the HUD works on any part.
* **Shade**: every 2 s a panel traces 3 rays (across its surface) toward the sun: through blocks nearby (opaque blocks and leaves block it,
  glass and water do not) and over the terrain further out, up to ~420 blocks, also in unloaded land (mountains, valleys). A shaded part
  still gets 12 % (sky light); the HUD says IN SHADE / PARTLY SHADED. At a low sun even a tree 40 blocks away or a lamp post shades.
* `--demoelectric NAME [--demosecs S]` (no window) builds a world at sunset: a solar farm on a deck 10 up, clear of hills and trees (fixed and tracking panels of all sizes) into a
  battery bank with 2 coal generators as night backup, a street of lamps on AUTO, 2 electric furnaces smelting iron ore, and a motor
  turning a shaft line into a generator.

## Wind, shafts, generators, coal
Rotation is its own network (Energy/Kinetics.cs), turned into energy by a generator:
* **Wind** is one for the whole world: a speed (0..16 m/s, about 8 on average, gusting) and a direction it comes from, in compass terms of
  the block grid (north = -z), turning slowly (a full turn in about 2.3 hours). It is saved with the world (`wind_clock` in world.json).
  Looking at a turbine shows it: "WIND 9.5 M/S FROM NW, 71% INTO THE FRONT" (or FROM BEHIND).
  **Settings (Esc) -> WIND**: "Natural wind" on = it changes by itself; moving "Wind speed" (0..20 m/s) or "Wind from" (compass
  degrees, 0 = north, 90 = east) sets it to stay there (natural off). The set wind is saved with the world (`wind_set` in world.json).
* **Wind Turbines** (small 5, medium 7, large 11 blocks across): a hub block; its blades turn in front of it and need a free
  diameter x diameter area there (air or plants), else it stops (HUD: BLOCKED). What it makes depends on:
  * the wind along its axis: v = wind speed x |cos(angle between the wind and the way the blades face)|; wind from the side gives
    nothing (it does not turn at all). Wind from the front turns it one way, from behind the other way (same power). Two turbines
    back to back on one shaft line both push the same way; every part of a line is drawn turning the same way;
  * height: the wind is x0.8 at sea level up to x1.26 at 60 blocks above it;
  * size: at 10 m/s straight in, small 10, medium 20, large 48 E/s (the swept area); power grows with v^3 (half the wind = an eighth
    of the power), at most 1.5 x that, nothing below 2.5 m/s. Bigger blades turn slower.
  Rotation comes out of its back only (shafts on its sides or top do nothing). Place it on the end of a shaft (blades toward you).
* **Shaft**: a rod carrying rotation along its axis (placed along your look), drawn turning. Ends join the next shaft, a turbine's back
  or a generator. Looking at one that no turbine drives says NOT DRIVEN.
* **Generator**: the shaft runs through it (either end); it takes up to 30 E/s of rotation and gives 90 % of it to the cable network.
  Several turbines on one shaft line add up; several generators on it share (a large turbine needs two). Load slows the rotor (it turns
  fastest with nothing to drive).
* **Coal Generator**: right click opens a window with a fuel slot (coal 40 s, planks 15 s...). 20 E/s while burning, and it burns only
  when its network needs energy or a battery has room, so no fuel is wasted: one coal = 800 E. Its front glows while it burns.
* All parts of a rotation network share one angle and speed (block entity numbers `angle`, `speed`, updated 10 times a second); the
  renderer turns the moving parts every frame from them (Rendering/MovingParts.cs: blocks with `moving_parts` within 96 blocks, built on
  the CPU, drawn with the terrain pipeline). Any block can have moving parts (see the AssetEditor README, `moving_parts`).
* `--demowind NAME [--demosecs S]` (no window) builds a saved world with a wind farm (turbines on 14-block towers: large, medium and small
  facing north, small ones facing east, south and west, each with a shaft and generator, cables down to 3 batteries and an oxygen
  generator) and a coal plant (coal generator with 16 coal charging a battery), run for S s (60). The wind starts from about WNW.

## Farming (water really used)
Water goes soil -> plant -> air; nothing is created or lost (Blocks/Behaviors/FarmingBehaviors.cs).
* **Wooden Hoe** (right click): tills dirt or grass (air above) into **Farmland**.
* **Farmland** holds water (`moisture` 0..8 units, darker when wet). Every 8-14 s, if not full, it takes 1 unit from the nearest water
  within 4 blocks sideways (its own row or one below), so that water's level really drops. Dry farmland without a crop turns back to dirt.
* **Crops** (Wheat Seeds, Carrot, Potato, Beetroot Seeds plant onto farmland only): every 25-45 s a growing crop drinks 1 unit from its
  farmland and grows a stage; that water goes to the water cycle's reservoir (the plant gives it off). On dry farmland a crop pauses,
  wilts back a stage after 3 dry checks; a seed (first stage) lies dormant 3 times as long, then dries up into a dead bush. Ripe crops
  use no water.
* Breaking a ripe crop gives its harvest (wheat + 0-3 seeds, 2-4 carrots / potatoes, beetroot + 1-3 seeds) into the inventory, an unripe
  one gives its seed back. Wheat needs 7 units (about one water block) per plant per harvest, so a farm needs a real supply: a lake or
  river nearby, a spring feeding an irrigation channel, or a pump. An overflowing channel washes crops away.
* Farm blocks tick only in loaded chunks (like machines).
* **Tick speed** (settings, Esc: "Tick speed (farming)", x1 .. x50): how much faster farm blocks tick: soil soaking up water, crops growing
  and wilting. The water per stage stays the same, only time runs faster. Farm changes re-mesh in the background (no main-thread stalls).
* `--demofarm NAME [--demosecs S]` (no window) builds a saved world with three farms: A irrigated (a spring feeding a glass-covered channel),
  B the same crops with no water (half grown, wilting), C wheat around a pond with no supply (the pond sinks as they drink).

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
`--assets DIR` loads the asset files from another folder (e.g. an editor export). `--world NAME` plays a saved world right away (or creates it with `--radius` / `--seed`); with `--bench` it is loaded read-only (`--benchsave` saves it when the run ends). `--startshot file.png` saves a picture of the start screen and quits.
`--selftest` (no window): rule checks (slab collision, the player landing on a slab, raycast / selection boxes, placing next to a slab, box meshing, doors as multiblocks, facing of stairs / beds / furnaces, moving items in a chest window, furnace smelting, the oxygen generator, state surviving chunk regeneration, water level as state, save -> clear -> load giving identical chunks, state words and block entities); exit code 1 on failure. `--showcase` (with `--bench`): places a row of example blocks in front of the camera; `--showui inventory|chest|furnace|oxygen_generator` opens a window over it.
`--benchedits` (with `--bench`, opens a window): after 8 s it digs / fills a block in the ground every 0.15 s and reports frame times with and
without edits; `--editsync` meshes edited chunks on the main thread (the old way) to compare. Measured (Utility Demo, RX 5700 XT, no vsync):
old worst frame 13.8 ms while editing (4.7 ms of meshing per chunk), new 7.7 ms, the same as without edits.

`--benchedit` (no window): time per block edit (setting the block + meshing the chunks it changes; the meshing now runs on the workers) and autosave time on the main thread vs in the background.
`--bench N --flyspeed V` (window): flies the free camera at V blocks/s and lists every frame over 25 ms with what it spent its time on
(chunk work, LOD uploads, shadows, scene, update, UI, end frame, GPU wait, time between frames) and the garbage collections in it.
`--benchfly` / `--benchgen` (no window): the main thread's non-drawing work while flying, block lookups in ungenerated chunks.
Smooth flight: concurrent garbage collection with sustained low latency while playing, chunk block arrays on the pinned object heap (no
copying by short collections), the game thread above normal priority and all background work (generation, meshing, LOD, scan) below it.
`--iconsheet file.png` (no window): every inventory icon in one picture, rasterized from the same faces the UI draws. Like Minecraft, no icon pictures are stored: a block item is drawn live in its slot from its 3D model (Items/ItemIcons.cs: a cached list of faces, drawn back to front with the block textures), plants as their block texture, and only items with `icon` "texture" (door, ingots...) use the item atlas (32 px).
`--fingerprint [--radius N] [--seed N] [--fprd CHUNKS]` (no window): generates every chunk around the bench spawn point (default 12 chunks) plus the core and prints a hash of the block data. `checks/check.ps1` builds, compares the hashes with `checks/fingerprint.txt`, runs the self-test and takes the screenshots in `checks/out` (references in `checks/ref`); refactors must keep the hashes identical.

## World generation (v2)
* **Terrain:** erosion noise decides flat vs rugged regions, domain-warped ridged noise gives sharper peaks, valleys are cut
  into highlands and worn-down uplands get terraced plateaus. Tuning lives in `World.Terrain()`.
* **Caves (Caves & Cliffs style):** cheese caverns, spaghetti tunnels with varying thickness, a wide tunnel network in
  some regions, and ravines that open to the surface in lowlands. Caves reach about 100 blocks below sea level
  (`CAVB` in `World.cs`); `World.CaveS()` holds all the thresholds.
* **Lakes from the terrain** (Generation/Hydrology.cs, Generation/Lakes.cs): when a world is created, the smoothed terrain is sampled
  about every 8 blocks over all 6 cube faces (at most 512 x 512 per face on big planets) and every hollow is filled up to its lowest rim
  (priority flood from the sea; ~0.5 s, from the seed, nothing saved; `--hydrostats [--hydropng file]`). A filled hollow at least a block
  deep is a lake: columns below its surface are water up to it, the shore stays at least a block above the highest lake nearby, no caves
  within 40 blocks below lake water (nor in the top 14 blocks under low coasts), no trees in water. Lake water is plain still water with no
  special rules, exactly like a pool a player builds. There are no generated rivers with special behaviour: everything natural can be
  remade by the player (the terrain's own lowland river valleys are cut below sea level and simply hold sea water).
* **Aquifers and lava:** like Minecraft's aquifers, the planet is split into ~40-block regions (Voronoi on the sphere, Generation/Aquifers.cs); each is dry (25 %) or has one water level (-54..-2), so flooded caves are level and at rest when generated. Where a cave crosses into a region with another level, a 3-block stone barrier keeps the water apart. The deepest caves hold lava lakes (static blocks, self-lit, solid).
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
(no second relight pass). The main thread only uploads finished meshes, so loading costs it almost nothing. Edited chunks are meshed by the
workers first of all (the old mesh stays drawn until the new one arrives, a frame or two later): meshing them on the main thread cost ~4 ms
a chunk, up to ~20 ms an edit. The scan for which chunks to load also runs in the background. Chunks behind you are
meshed too (at lower priority), so turning around doesn't show holes.
Measured (render distance 12, 2967 chunks): everything generated and meshed 3.5 s after start, 0 ms of meshing on the main thread
(before: 1446 visible chunks meshed on the main thread at about 2 ms each, inside a 5-9 ms per-frame budget).
Thread safety: terrain result slots and the block-lookup scratch values (`P*`, `N*`, `W*` results, the one-entry chunk cache) are
`[ThreadStatic]`, workers find chunks through a `ConcurrentDictionary` mirror (`chunkMap`), player edits and water levels are read under locks,
and each chunk is claimed through `Chunk.gst` (generation) / `Chunk.mst` (meshing).
