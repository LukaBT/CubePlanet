# Asset Editor (Cube Planet)

A Python / Tkinter editor for **block textures, item textures and every definition file** of Cube Planet (and the older 2D game: its projects
still load). It writes exactly the files the game loads from `Assets/`.

```
pip install pillow          # Tkinter ships with Python (Linux: apt install python3-tk)
python asset_editor.py
python test_editor.py       # headless checks (loads ../Assets, 3D rules, icons, export)
python test_gui.py          # builds the windows hidden and drives the Block 3D tab
```

## Working on the game's assets
**File > Open Cube Planet Assets folder** (shown when the editor sits in the CubePlanet repo), edit, then **File > Export game files to
folder** and pick `Assets/` (the dialog starts there). Export writes the 10 game files with freshly built atlases. *Save* also works but adds
the editor's own `block_texture/`, `item_texture/` folders and `project_settings.json` next to them. Run the game with
`--assets <folder>` to try an export that is somewhere else.

| File | Content |
|---|---|
| `block_texture_atlas.png`, `item_texture_atlas.png` | Texture grids (RGBA). Block textures 16x16 (square, power of two), item pictures 32x32 (only items with `icon` "texture": block items get their icon from the game, see below) |
| `block_texture_definitions.txt`, `item_texture_definitions.txt` | `texture_size: W H`, `textures_per_row: N`, `texture_rows: N`, then `id name x y` |
| `block_definitions.txt`, `item_definitions.txt`, `ui_definitions.txt` | `{"blocks": [...]}`, `{"items": [...]}`, `{"uis": [...]}` |
| `recipe_definitions.txt`, `sound_definitions.txt`, `entity_definitions.txt` | `{"recipes": [...]}`, `{"sounds": [...]}`, `{"entities": []}` (optional when loading) |

Loading accepts a bare list or a single object, `//` and `/* */` comments and trailing commas. IDs are 1..65535 (0 = air / empty); block IDs and
item IDs are separate spaces; the item texture ID equals the item ID. The game stops at startup with the file name and problem when something
is wrong (unknown model, texture, sound, UI, too many state bits, two parts with the same state...).

## Tabs
* **Item / Block Textures** - pixel editor (pen, eraser, fill, picker, make-transparent, undo, rotate, flip, bulk text import / export).
* **Item / Block / UI Definitions** - list + JSON editor with helpers (groups, add property, texture slot, geometry preset (3D presets first,
  then the old 2D ones), link UI, UI widgets).
* **Block 3D** - the block of the list as a form: model and attach, rotation, placement facing, behaviour, UI; **face textures** with
  thumbnails in three modes (*all*, *top / side / bottom*, *six faces*; switching fills the new slots from the current look); render flags;
  **geometry presets** (full, slabs, bottom / top slab, vertical slab, joining / upside-down stairs, connecting fence, fence post, door, bed,
  plant, no hitbox); sound slots; the **instance-state field editor** with
  a live packed-bit counter (red above 16); the **multiblock part editor**; a **3D preview** with a picker per state field (facing, open,
  half...); **Animate moving parts** (turns `moving_parts` in the preview); **Refresh item icon preview** (block items are not painted into the item atlas: the game draws their icon itself, see `icon`).
* **Sounds**, **Recipes** (smelting), **Entities** (raw JSON list; the format is reserved by the game).
* **Project Settings** - default namespace, texture sizes, atlas grid.

## Block keys
Top level: `type` "block", `namespace`, `id_name`, `id`, `name`, `stateless`, `group_tags` (game uses `log`, `leaves`), `textures`, `ui_id`
`{namespace, id_name}`, `properties.definition`, `properties.instance_state`.

`textures` - slots -> block texture IDs: `all`; or `top` / `bottom` / `side`; or `north` (-z) / `south` (+z) / `east` (+x) / `west` (-x) /
`top` / `bottom`. A face uses its own slot, then `side`, then `all`. A rotating block's `north` side is its front.

`properties.definition` (all optional, default first):

| Key | Meaning |
|---|---|
| `model` | `cube` / `cross` (crossed sprites) / `liquid` / `attached` (+ `attach`: `vine` / `pod`) / `boxes` |
| `boxes` | `[{"x","y","z","w","h","d", "textures": {...}}]` block units 0..1, y up; textures are cut to the box |
| `geometry.collision` | boxes the player collides with; missing = full block for cubes, none otherwise; `[]` = walk through |
| `geometry.selection` | boxes the cursor targets; missing = full block (none for liquids) |
| `geometry.state_overrides` | `[{"when": {field: value}, "boxes", "add_boxes", "collision", "selection", "textures"}]` in order, matching ones win; `add_boxes` adds boxes instead of replacing them (cable / pipe arms: one override per side) |
| `rotation` | `none` / `horizontal` / `all`; adds a `facing` state field (north, east, south, west [, up, down]) |
| `placement_facing` | `toward_player` (front faces you) / `away` (stairs, doors, beds) |
| `moving_parts` | `[{"axis": "x"/"y"/"z" (default z), "pivot": [x, y, z] (default the centre), "speed": 1, "boxes": [boxes as in "boxes", may reach out of the block]}]`: boxes turning around the axis through the pivot (block units, before the facing turn; north = -z is the front) at the block's rotation network speed x `speed` (block entity numbers `angle`, `speed`: add them to `instance_state`). Drawn turning in the game and in the Block 3D preview (Animate). A `boxes` model may have no fixed boxes if it has moving parts (shaft). |
| `multiblock.parts` | `[{"offset": [x, y, z], "state": {...}}]` relative to the facing; every part needs a different state |
| `behavior` | `door`, `bed`, `container`, `smelting`, `oxygen_generator`, `stairs` (shape from neighbours, `half` from the click), `connecting` (fences, cables, pipes: fields `north` `east` `south` `west`, optional `up` `down`), water pipes: group tag `water_pipe` + key `pipe`: `{"diameter": m}` (0.35 if missing), `turbine`, `pump` (key `pump`: `{"type": "centrifugal" / "piston", "max_head", "flow", "efficiency"}`), `battery`, `spring`, `drain` (hydro power), `wind_turbine` (key `wind_turbine`: `{"diameter": blade span in blocks, "power": E/s at 10 m/s straight into the blades}`), `shaft`, `generator` (rotation; see `moving_parts`), `coal_generator` (inventory `fuel`, numbers `burn` `burn_max`), cables: group tag `energy_cable` + key `cable`: `{"capacity": E/s at LV, "loss": share lost per block at full current, "max_voltage": "LV" / "MV" / "HV" (insulation), "upkeep": E/s per block (cooling)}` (64, 0.003, LV, 0 if missing), `lamp` (key `lamp`: `{"level" (<= 9), "power"}`, state `lit`), `electric_furnace` (key `electric_furnace`: `{"power", "speed"}`, inventories `input` `output`), `motor`, `gearbox`, `processor` (millstone, sawmill: key `processing`: `{"power", "recipes": [{"input", "output", "count", "work"}]}`, inventories `input` `output`), `mechanical_pump`, `tank_valve` (with group tag `tank_wall` on every tank block), `compressor`, `pipe_valve`, `solar_panel` (key `solar`: `{"power", "tilt_max"}`, a moving part as the panel; multiblock state `part`, first part = main), `transformer` (key `transformer`: `{"low": "LV", "high": "MV", "rating": E/s, "loss": share (0.02)}`; back = low side), any block: key `wire_anchor`: `{"x", "y", "z" attach point, "when": {state field: value}}` (wires attach there; a pylon uses `when` for its top part), `farmland` (state `moisture` 0..8), `crop` (state `age`; key `crop`: `{"seed": item, "harvest": [{"item", "min", "max"}]}`; see the game README) |
| `connects_to` | connecting blocks: group tags of the neighbours they join (full solid cubes count unless `connect_solid` is false) |
| `connect_solid` | `true` (default, fences) / `false` (cables, pipes): whether connecting blocks join any full solid cube |
| `geometry.collision`, `geometry.selection` = `"boxes"` | collide / select with the visible boxes of the current state (cables, pipes) |
| group tags `energy`, `energy_cable`, `water_pipe` | `energy`: cables connect to it; `energy_cable`: carries energy between machines; `water_pipe`: pipes connect to it and carry water for turbines / pumps |
| `render.layer` | `opaque` (hides neighbour faces) / `cutout` / `translucent` / `liquid` |
| `render.connect_group` | faces between equal groups are hidden (`leaves`, `glass`, `ice`, any name) |
| `render.sway` | `none` / `leaves` / `plant` / `underwater` |
| `render.emissive` | > 0: self-lit and glowing |
| `render.casts_shadow` | `true` |
| `random_offset` | `true` (cross sprites jitter per block) |
| `waterlogged` | `false` (underwater plants) |
| `lod_color` | `[r, g, b]` colour on distant terrain |
| `sounds` | `{"break", "place", "step", "use"}` -> sound `id_name`s |
| `hardness`, `drops`, `default_state` | written by the editor; the game does not use them yet (creative rules) |

`properties.instance_state` - one entry per field:
`{"type": "boolean", "default": false}`, `{"type": "string", "values": [...], "default": "..."}`, `{"type": "integer", "min": 1, "max": 8,
"default": 8}` are **packed** into 16 bits per block; `{"type": "number", "default": 0}`, `{"type": "inventory", "slots": 27}` and any field
with `"storage": "entity"` live in the block's **entity** (saved with the chunk).

## Item keys
`type` "item", `namespace`, `id_name`, `id`, `name`, `group_tags`, `properties.definition`: `stack_size` (64), `places_block`
`{namespace, id_name}`, `fuel_time` (seconds in a furnace), `behavior` (`bucket`, `hoe`, `multimeter`, `wire_spool` with key `wire`: `{"capacity": E/s of current at LV, "loss", "max_voltage", "max_length": blocks, "texture": block texture of the drawn wire}`), `capacity`; `properties.instance_state`: packed fields only
(32 bits per stack, e.g. the bucket's `fill`).

Item icons (`properties.definition.icon`), like Minecraft: `"model"` = the game draws the placed block's 3D model live in the slot, like Minecraft (default for `cube` / `boxes` blocks; multiblocks such as beds show all parts), `"sprite"` = the block's texture drawn flat (default for `cross` / `attached` / `liquid` blocks: flowers, saplings, vines), `"texture"` = the item's own picture in the item atlas (default for items that place no block; the door uses it for its flat door picture). `icon_state` sets block state values for the icon, e.g. `{"east": true, "west": true}` shows a fence with bars; rotating blocks show their front unless `facing` is given.

## UI keys
`window {width, height}` in UI units, `widgets`: `label` (`text`), `slot` (`storage {scope: block | player, key}`, `mode: output_only`),
`slot_grid` (`rows`, `columns`, `slot_size`, `start_index`, `storage {scope, key_pattern}` with `{index}`), `progress_bar` (`size`,
`current_from: block.<number field>`, `max` or `max_from`, `orientation: vertical`, `color [r,g,b]`, `label`), `ui_panel` (`ui_id`, embeds another UI).
Player storage keys: `hotbar[i]` (0..9), `main[i]` (0..26). Block storage keys: an inventory field, `name` or `name[i]`.

## Recipes, sounds, entities
```json
{"recipes": [{"type": "smelting", "id_name": "iron_ingot_from_iron_ore", "input": {"namespace": "base", "id_name": "iron_ore"},
              "output": {"namespace": "base", "id_name": "iron_ingot", "count": 1}, "time": 8}]}
{"sounds": [{"id": 1, "id_name": "door_open", "file": "sounds/door_open.ogg"}]}
{"entities": []}
```

## Examples
Texture IDs below are those of the current `Assets/`; `properties.definition` keys the game does not need are left out.

**Dirt** (plain cube):
```json
{"type": "block", "namespace": "base", "id_name": "dirt", "id": 3, "name": "Dirt", "stateless": true, "group_tags": [],
 "textures": {"all": 3},
 "properties": {"definition": {"model": "cube", "render": {"layer": "opaque"}, "lod_color": [121, 85, 58]}, "instance_state": {}}}
```
**Grass block with top / side / bottom** (the current grass uses one texture; a grass top texture would go into `top`):
```json
{"type": "block", "id_name": "grass", "id": 1, "textures": {"top": 1, "side": 1, "bottom": 3},
 "properties": {"definition": {"model": "cube", "lod_color": [89, 161, 61]}, "instance_state": {}}}
```
**Flower**:
```json
{"type": "block", "id_name": "poppy", "id": 47, "textures": {"all": 47},
 "properties": {"definition": {"model": "cross", "render": {"layer": "cutout", "sway": "plant", "casts_shadow": false},
   "geometry": {"collision": [], "selection": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 1, "d": 1}]}}, "instance_state": {}}}
```
**Slab**:
```json
{"type": "block", "id_name": "oak_slab", "id": 118, "textures": {"all": 31},
 "properties": {"definition": {"model": "boxes", "render": {"layer": "cutout"},
   "boxes": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5, "d": 1}],
   "geometry": {"collision": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5, "d": 1}], "selection": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5, "d": 1}]}},
  "instance_state": {}}}
```
**Door** (2 tall, opens, upper half texture by state):
```json
{"type": "block", "id_name": "oak_door", "id": 124, "stateless": false, "textures": {"all": 125},
 "properties": {"definition": {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "behavior": "door",
   "boxes": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 1, "d": 0.1875}],
   "multiblock": {"parts": [{"offset": [0, 0, 0], "state": {"half": "lower"}}, {"offset": [0, 1, 0], "state": {"half": "upper"}}]},
   "sounds": {"use": "door_open", "break": "wood_break"},
   "geometry": {"collision": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 1, "d": 0.1875}], "selection": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 1, "d": 0.1875}],
     "state_overrides": [{"when": {"half": "upper"}, "textures": {"all": 126}},
                         {"when": {"open": true}, "boxes": [{"x": 0, "y": 0, "z": 0, "w": 0.1875, "h": 1, "d": 1}],
                          "collision": [{"x": 0, "y": 0, "z": 0, "w": 0.1875, "h": 1, "d": 1}], "selection": [{"x": 0, "y": 0, "z": 0, "w": 0.1875, "h": 1, "d": 1}]}]}},
  "instance_state": {"half": {"type": "string", "values": ["lower", "upper"], "default": "lower"}, "open": {"type": "boolean", "default": false}}}}
```
**Bed** (2 long, pillow on the head part):
```json
{"type": "block", "id_name": "red_bed", "id": 125, "stateless": false, "textures": {"top": 127, "side": 129, "bottom": 31},
 "properties": {"definition": {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "behavior": "bed",
   "boxes": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5625, "d": 1}],
   "multiblock": {"parts": [{"offset": [0, 0, 0], "state": {"part": "foot"}}, {"offset": [0, 0, -1], "state": {"part": "head"}}]},
   "geometry": {"collision": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5625, "d": 1}], "selection": [{"x": 0, "y": 0, "z": 0, "w": 1, "h": 0.5625, "d": 1}],
     "state_overrides": [{"when": {"part": "head"}, "textures": {"top": 128}}]}},
  "instance_state": {"part": {"type": "string", "values": ["foot", "head"], "default": "foot"}}}}
```
**Chest** (block entity with 27 slots, opens the `chest` UI):
```json
{"type": "block", "id_name": "chest", "id": 126, "stateless": false, "ui_id": {"namespace": "base", "id_name": "chest"},
 "textures": {"north": 130, "side": 131, "top": 132, "bottom": 132},
 "properties": {"definition": {"model": "boxes", "rotation": "horizontal", "behavior": "container",
   "boxes": [{"x": 0.0625, "y": 0, "z": 0.0625, "w": 0.875, "h": 0.875, "d": 0.875}]},
  "instance_state": {"slots": {"type": "inventory", "slots": 27}}}}
```
**Furnace** (lit front by state, smelting behaviour):
```json
{"type": "block", "id_name": "furnace", "id": 127, "stateless": false, "ui_id": {"namespace": "base", "id_name": "furnace"},
 "textures": {"north": 133, "side": 135, "top": 136, "bottom": 136},
 "properties": {"definition": {"model": "cube", "rotation": "horizontal", "behavior": "smelting",
   "geometry": {"state_overrides": [{"when": {"lit": true}, "textures": {"north": 134}}]}},
  "instance_state": {"lit": {"type": "boolean", "default": false}, "input": {"type": "inventory", "slots": 1}, "fuel": {"type": "inventory", "slots": 1},
   "output": {"type": "inventory", "slots": 1}, "progress": {"type": "number", "default": 0}, "cook_time": {"type": "number", "default": 10},
   "burn": {"type": "number", "default": 0}, "burn_max": {"type": "number", "default": 1}}}}
```
**Oxygen generator** (machine: energy + water -> oxygen canisters):
```json
{"type": "block", "id_name": "oxygen_generator", "id": 128, "stateless": false, "ui_id": {"namespace": "base", "id_name": "oxygen_generator"},
 "textures": {"north": 137, "side": 138, "top": 139, "bottom": 139},
 "properties": {"definition": {"model": "cube", "rotation": "horizontal", "behavior": "oxygen_generator"},
  "instance_state": {"fuel": {"type": "inventory", "slots": 1}, "water_in": {"type": "inventory", "slots": 1}, "output": {"type": "inventory", "slots": 1},
   "energy": {"type": "number", "default": 0}, "water": {"type": "number", "default": 0}, "oxygen": {"type": "number", "default": 0}}}}
```

## Notes
* Texture and id_names are lowercased `a-z 0-9 _`. New blocks get the lowest free ID and an item that places them.
* The 2D game's projects (2D geometry `{"x","y","width","height"}`, 2D multiblock offsets `[x, y]`) load unchanged; the game reads 2D boxes as
  full-depth 3D boxes and 2D offsets with z = 0.
* `PythonAssetToolManager.txt` is the original design notes of the 2D editor.
