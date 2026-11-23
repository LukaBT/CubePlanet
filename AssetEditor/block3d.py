"""
3D block helpers for the Asset Editor (no Tkinter, so they can be tested headless). They mirror the game's rules (CubePlanet
Blocks/BlockDefinition.cs, BlockVariant.cs, StateSchema.cs): face texture slots, boxes, state_overrides, facing rotation, packed state bits,
plus 3D geometry presets and an isometric renderer for previews and item icons.

Block-local axes: x = east, y = up, z = south (north = -z). Boxes are {"x","y","z","w","h","d"} in block units (0..1).
"""
import copy
import math

from PIL import Image

FACE_NAMES = ["east", "west", "top", "bottom", "south", "north"]  # the game's face order: +x, -x, +y, -y, +z, -z
MODELS = ["cube", "cross", "liquid", "attached", "boxes"]
RENDER_LAYERS = ["opaque", "cutout", "translucent", "liquid"]
SWAYS = ["none", "leaves", "plant", "underwater"]
ROTATIONS = ["none", "horizontal", "all"]
BEHAVIORS = ["", "door", "bed", "container", "smelting", "oxygen_generator", "stairs", "connecting"]
FIELD_TYPES = ["boolean", "string", "integer", "number", "inventory"]
SOUND_SLOTS = ["break", "place", "step", "use"]


def box(x, y, z, w, h, d):
    return {"x": x, "y": y, "z": z, "w": w, "h": h, "d": d}


FULL = [box(0, 0, 0, 1, 1, 1)]
DOOR_CLOSED, DOOR_OPEN = [box(0, 0, 0, 1, 1, .1875)], [box(0, 0, 0, .1875, 1, 1)]
# Minecraft-like stairs (frame: high side north; "left" = west) and connecting fences, generated as state_overrides
_Q = {"NW": (0, 0), "NE": (.5, 0), "SW": (0, .5), "SE": (.5, .5)}
STAIR_SHAPES = {"straight": ["N"], "outer_left": ["NW"], "outer_right": ["NE"], "inner_left": ["N", "SW"], "inner_right": ["N", "SE"]}  # order = state values
def stair_boxes(half, shape):
    base_y, step_y = (0, .5) if half == "bottom" else (.5, 0)
    out = [box(0, base_y, 0, 1, .5, 1)]
    for q in STAIR_SHAPES[shape]:
        out.append(box(0, step_y, 0, 1, .5, .5) if q == "N" else box(_Q[q][0], step_y, _Q[q][1], .5, .5, .5))
    return out
def stairs_preset():
    ovs = [{"when": {"half": h, "shape": s}, "boxes": stair_boxes(h, s), "collision": stair_boxes(h, s), "selection": stair_boxes(h, s)}
           for h in ("bottom", "top") for s in STAIR_SHAPES if not (h == "bottom" and s == "straight")]
    return {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "behavior": "stairs", "boxes": stair_boxes("bottom", "straight"),
            "geometry": {"collision": stair_boxes("bottom", "straight"), "selection": stair_boxes("bottom", "straight"), "state_overrides": ovs},
            "_state": {"half": {"type": "string", "values": ["bottom", "top"], "default": "bottom"},
                       "shape": {"type": "string", "values": list(STAIR_SHAPES), "default": "straight"}}}
_POST = box(.375, 0, .375, .25, 1, .25)
_ARM = {"north": (.4375, 0, .125, .375), "south": (.4375, .625, .125, .375), "east": (.625, .4375, .375, .125), "west": (0, .4375, .375, .125)}
def fence_preset():
    import itertools
    rails = lambda ds: [_POST] + [box(x, y, z, w, .1875, d) for dn in ds for (x, z, w, d) in [_ARM[dn]] for y in (.375, .75)]
    tall = box(.375, 0, .375, .25, 1.5, .25)  # collision 1.5 high like Minecraft: fences cannot be jumped over
    solid = lambda ds: [tall] + [box(x, 0, z, w, 1.5, d) for dn in ds for (x, z, w, d) in [_ARM[dn]]]
    ovs = []
    for combo in itertools.product([False, True], repeat=4):
        ds = [dn for dn, on in zip(("north", "east", "south", "west"), combo) if on]
        if ds: ovs.append({"when": dict(zip(("north", "east", "south", "west"), combo)), "boxes": rails(ds), "collision": solid(ds), "selection": rails(ds)})
    return {"model": "boxes", "behavior": "connecting", "connects_to": ["fence"], "boxes": rails([]),
            "geometry": {"collision": [tall], "selection": [_POST], "state_overrides": ovs},
            "_state": {dn: {"type": "boolean", "default": False} for dn in ("north", "east", "south", "west")}, "_tags": ["fence"]}


# preset -> keys written into properties.definition (model, boxes, geometry, rotation, multiblock, ...)
GEOMETRY_PRESETS_3D = {
    "Full block": {"model": "cube", "geometry": {"collision": FULL, "selection": FULL, "state_overrides": []}},
    "Slab (lower)": {"model": "boxes", "boxes": [box(0, 0, 0, 1, .5, 1)],
                     "geometry": {"collision": [box(0, 0, 0, 1, .5, 1)], "selection": [box(0, 0, 0, 1, .5, 1)], "state_overrides": []}},
    "Slab (upper)": {"model": "boxes", "boxes": [box(0, .5, 0, 1, .5, 1)],
                     "geometry": {"collision": [box(0, .5, 0, 1, .5, 1)], "selection": [box(0, .5, 0, 1, .5, 1)], "state_overrides": []}},
    "Slab (bottom / top)": {"model": "boxes", "boxes": [box(0, 0, 0, 1, .5, 1)],
                            "geometry": {"collision": [box(0, 0, 0, 1, .5, 1)], "selection": [box(0, 0, 0, 1, .5, 1)], "state_overrides": [
                                {"when": {"half": "top"}, "boxes": [box(0, .5, 0, 1, .5, 1)], "collision": [box(0, .5, 0, 1, .5, 1)], "selection": [box(0, .5, 0, 1, .5, 1)]}]},
                            "_state": {"half": {"type": "string", "values": ["bottom", "top"], "default": "bottom"}}},
    "Vertical slab": {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "boxes": [box(0, 0, 0, 1, 1, .5)],
                      "geometry": {"collision": [box(0, 0, 0, 1, 1, .5)], "selection": [box(0, 0, 0, 1, 1, .5)], "state_overrides": []}},
    "Stairs (joining, upside down)": stairs_preset(),
    "Fence (connecting)": fence_preset(),
    "Door (2 tall, opens)": {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "behavior": "door", "boxes": DOOR_CLOSED,
                             "multiblock": {"parts": [{"offset": [0, 0, 0], "state": {"half": "lower"}}, {"offset": [0, 1, 0], "state": {"half": "upper"}}]},
                             "geometry": {"collision": DOOR_CLOSED, "selection": DOOR_CLOSED, "state_overrides": [
                                 {"when": {"open": True}, "boxes": DOOR_OPEN, "collision": DOOR_OPEN, "selection": DOOR_OPEN}]},
                             "_state": {"half": {"type": "string", "values": ["lower", "upper"], "default": "lower"}, "open": {"type": "boolean", "default": False}}},
    "Bed (2 long)": {"model": "boxes", "rotation": "horizontal", "placement_facing": "away", "behavior": "bed", "boxes": [box(0, 0, 0, 1, .5625, 1)],
                     "multiblock": {"parts": [{"offset": [0, 0, 0], "state": {"part": "foot"}}, {"offset": [0, 0, -1], "state": {"part": "head"}}]},
                     "geometry": {"collision": [box(0, 0, 0, 1, .5625, 1)], "selection": [box(0, 0, 0, 1, .5625, 1)], "state_overrides": []},
                     "_state": {"part": {"type": "string", "values": ["foot", "head"], "default": "foot"}}},
    "Plant (no collision)": {"model": "cross", "render": {"layer": "cutout", "sway": "plant", "casts_shadow": False},
                             "geometry": {"collision": [], "selection": FULL, "state_overrides": []}},
    "No hitbox at all": {"geometry": {"collision": [], "selection": [], "state_overrides": []}},
}


def apply_preset(d, name):
    """Writes a 3D geometry preset into a block definition (keeps other keys; adds the preset's state fields)."""
    p = copy.deepcopy(GEOMETRY_PRESETS_3D[name])
    st = p.pop("_state", None)
    pd = d.setdefault("properties", {}).setdefault("definition", {})
    for k in ("boxes", "multiblock"):
        if k not in p: pd.pop(k, None)
    for k, v in p.items():
        if k == "render": pd.setdefault("render", {}).update(v)
        else: pd[k] = v
    tags = p.pop("_tags", None)
    if tags: d["group_tags"] = sorted(set(d.get("group_tags", []) + tags))
    for k in ("boxes", "multiblock", "connects_to"):
        if k not in p: pd.pop(k, None)
    if st:
        ist = d["properties"].setdefault("instance_state", {})
        for k, v in st.items(): ist.setdefault(k, v)
        d["stateless"] = False
    return d


# ---------------------------------------------------------------- definitions
def flatten(tex, prefix="", out=None):
    out = {} if out is None else out
    for k, v in (tex or {}).items():
        key = k if not prefix else prefix + "." + k
        if isinstance(v, (int, float)) and not isinstance(v, bool): out[key] = int(v)
        elif isinstance(v, dict): flatten(v, key, out)
    return out


def texture_of(tex, slot="all"):
    for s in (slot, "all", "default"):
        if s in tex: return tex[s]
    return next(iter(tex.values()), 0)


def faces_of(tex):
    """Face textures from a flat slot map: the face's own slot, then side (horizontal faces), then all."""
    return [tex[n] if n in tex else (tex["side"] if i not in (2, 3) and "side" in tex else texture_of(tex, "all")) for i, n in enumerate(FACE_NAMES)]


def defn(d): return d.get("properties", {}).get("definition", {})


def norm_box(b):
    """A box in 3D form (accepts the editor's 2D {x, y, width, height} too)."""
    w = b.get("w", b.get("width", 1)); h = b.get("h", b.get("height", 1)); dd = b.get("d", b.get("depth", 1))
    x, y, z = b.get("x", 0), b.get("y", 0), b.get("z", 0)
    return (max(0, x), max(0, y), max(0, z), min(1, x + w), min(1.5, y + h), min(1, z + dd))  # collision may reach 1.5 (fences)


# ---------------------------------------------------------------- state (packed bits like the game)
def field_bits(f):
    t = f.get("type", "string")
    if f.get("storage") == "entity" or t in ("number", "inventory"): return 0
    if t == "boolean": n = 2
    elif t == "string": n = len(f.get("values") or [])
    elif t == "integer":
        if "min" not in f or "max" not in f: return 0
        n = int(f["max"]) - int(f["min"]) + 1
    else: return 0
    return max(1, math.ceil(math.log2(n))) if n > 0 else 0


def schema(d):
    """[(name, field, bits)] of the packed fields (+ an automatic facing field for rotating blocks), and the total bit count."""
    ist = d.get("properties", {}).get("instance_state", {}) or {}
    out = [(k, f, field_bits(f)) for k, f in ist.items() if isinstance(f, dict) and field_bits(f) > 0]
    rot = defn(d).get("rotation", "none")
    if rot != "none" and "facing" not in ist:
        vals = ["north", "east", "south", "west"] + (["up", "down"] if rot == "all" else [])
        out.append(("facing", {"type": "string", "values": vals, "default": "north"}, 3 if len(vals) > 4 else 2))
    return out, sum(b for _, _, b in out)


def field_values(f):
    t = f.get("type", "string")
    if t == "boolean": return [False, True]
    if t == "integer": return list(range(int(f["min"]), int(f["max"]) + 1))
    return list(f.get("values") or [])


def default_values(d):
    vals = {}
    for name, f, _ in schema(d)[0]:
        fv = field_values(f); dv = f.get("default", fv[0] if fv else None)
        vals[name] = dv if dv in fv else (fv[0] if fv else None)
    return vals


def matches(state, when):
    return all(state.get(k) == v for k, v in (when or {}).items())


# ---------------------------------------------------------------- variants (overrides, then facing rotation)
CW_FACE = [5, 4, 2, 3, 0, 1]   # quarter turn clockwise from above: east <- north, south <- east, west <- south, north <- west
UP_FACE = [0, 1, 5, 4, 2, 3]   # north turned up: top <- north, south <- top, bottom <- south, north <- bottom


def _cw(b): x0, y0, z0, x1, y1, z1 = b; return (1 - z1, y0, x0, 1 - z0, y1, x1)
def _up(b): x0, y0, z0, x1, y1, z1 = b; return (x0, 1 - z1, y0, x1, 1 - z0, y1)


def variant(d, state):
    """{'boxes': [(box tuple, [6 face textures])], 'collision': [...] or None, 'selection': [...] or None, 'faces': [6]} for a state dict."""
    pd = defn(d); tex = flatten(d.get("textures"))
    boxes = pd.get("boxes") or []; geo = pd.get("geometry", {}) or {}
    model = pd.get("model", "cube")
    coll = geo.get("collision", None if model == "cube" else []); sel = geo.get("selection", [] if model == "liquid" else None)
    for ov in geo.get("state_overrides", []) or []:
        if not matches(state, ov.get("when")): continue
        if "boxes" in ov: boxes = ov["boxes"]
        if "collision" in ov: coll = ov["collision"]
        if "selection" in ov: sel = ov["selection"]
        if "textures" in ov: tex = {**tex, **flatten(ov["textures"])}
    v = {"faces": faces_of(tex), "boxes": [(norm_box(b), faces_of({**tex, **flatten(b.get("textures"))})) for b in boxes],
         "collision": None if coll is None else [norm_box(b) for b in coll], "selection": None if sel is None else [norm_box(b) for b in sel]}
    f = state.get("facing", "north") if pd.get("rotation", "none") != "none" else "north"
    ups = {"up": 1, "down": 3}.get(f, 0); turns = {"east": 1, "south": 2, "west": 3}.get(f, 0)
    for fn, fm, n in ((_up, UP_FACE, ups), (_cw, CW_FACE, turns)):
        for _ in range(n):
            mp = lambda fs: [fs[fm[i]] for i in range(6)]
            v["faces"] = mp(v["faces"]); v["boxes"] = [(fn(b), mp(fs)) for b, fs in v["boxes"]]
            for k in ("collision", "selection"):
                if v[k] is not None: v[k] = [fn(b) for b in v[k]]
    return v


# ---------------------------------------------------------------- isometric rendering
def render_iso(boxes, texture, size=32, shade=(1.0, .78, .6)):
    """Orthographic isometric picture of boxes [(box tuple, [6 face textures])]: top lit, south (+z) side .78, east (+x) side .6.
    texture(tid) -> 16x16 RGBA PIL image or None. Same projection as the game's item icons (size 32: top corner at (16, 2))."""
    k = size / 32.0; out = Image.new("RGBA", (size, size)); cache = {}
    def tx(tid):
        if tid not in cache:
            im = texture(tid); cache[tid] = im.convert("RGBA").load() if im is not None else None
        return cache[tid]
    for py in range(size):
        for px in range(size):
            sx, sy = (px + .5) / k, (py + .5) / k; best = None
            for (X0, Y0, Z0, X1, Y1, Z1), fs in boxes:
                a, s = (sx - 16) / 14, (sy - 2 - 15 * (1 - Y1)) / 7; x, z = (a + s) / 2, (s - a) / 2
                if X0 <= x <= X1 and Z0 <= z <= Z1: best = max(best or (-9,), (x + Y1 * 14 / 15 + z, 2, x, Y1, z, fs[2]))
                z = X1 - (sx - 16) / 14; y = 1 - (sy - 2 - 7 * (X1 + z)) / 15
                if Z0 <= z <= Z1 and Y0 <= y <= Y1: best = max(best or (-9,), (X1 + y * 14 / 15 + z, 0, X1, y, z, fs[0]))
                x = Z1 + (sx - 16) / 14; y = 1 - (sy - 2 - 7 * (x + Z1)) / 15
                if X0 <= x <= X1 and Y0 <= y <= Y1: best = max(best or (-9,), (x + y * 14 / 15 + Z1, 4, x, y, Z1, fs[4]))
            if not best: continue
            _, face, x, y, z, tid = best
            u, v, sh = (x, z, shade[0]) if face == 2 else ((1 - z, 1 - y, shade[2]) if face == 0 else (x, 1 - y, shade[1]))
            t = tx(tid)
            if t is None: c = (255, 0, 255, 255) if (int(u * 2) + int(v * 2)) % 2 == 0 else (0, 0, 0, 255)
            else: c = t[min(15, int(u * 16)), min(15, int(v * 16))]
            if c[3] == 0: continue
            out.putpixel((px, py), tuple(int(round(c[j] * sh)) for j in range(3)) + (c[3],))
    return out


def block_picture(d, texture, size=32, state=None):
    """Picture of a block in a state: cubes and boxes isometric, sprites (cross / attached) flat; the front (north) turned to the viewer."""
    turn_front = state is None
    state = dict(default_values(d) if state is None else state)
    pd = defn(d); model = pd.get("model", "cube")
    if turn_front and pd.get("rotation", "none") != "none": state["facing"] = "south"  # icons show the front (north side) to the viewer
    if model in ("cross", "attached"):
        im = texture(texture_of(flatten(d.get("textures")), "all"))
        return (im or Image.new("RGBA", (16, 16), (255, 0, 255, 255))).convert("RGBA").resize((size, size), Image.NEAREST)
    v = variant(d, state)
    boxes = v["boxes"] if model == "boxes" else [((0, 0, 0, 1, 1, 1), v["faces"])]
    return render_iso(boxes, texture, size)


def icon_kind(item, block):
    """How the game makes an item's inventory icon (properties.definition.icon): "model" = the block's 3D model drawn in-game at 128 px,
    "sprite" = the block's texture drawn flat (plants), "texture" = the item's own picture in the item atlas. Same rules as Items/ItemIcons.cs."""
    k = defn(item).get("icon", "")
    if block is None: return "texture"
    if k: return k
    return "model" if defn(block).get("model", "cube") in ("cube", "boxes") else "sprite"


def item_picture(item, block, texture, size=32):
    """Editor preview of a block item's icon (the game draws the real one): the block in the item's icon_state, front to the viewer."""
    if icon_kind(item, block) == "sprite":
        return block_picture(block, texture, size)
    st = dict(default_values(block)); st.update(defn(item).get("icon_state") or {})
    if defn(block).get("rotation", "none") != "none" and "facing" not in (defn(item).get("icon_state") or {}): st["facing"] = "south"
    return block_picture(block, texture, size, st)
