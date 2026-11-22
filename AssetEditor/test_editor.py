"""
Headless checks of the Asset Editor (no window is opened):
  python AssetEditor/test_editor.py [export_dir]
Loads the game's Assets folder, checks the 3D rules against what the game does, renders block icons, loads an old 2D project if one is
around, and exports every file to export_dir (default: a temp folder) so the game can be run on the export:
  CubePlanet --assets <export_dir> --selftest   /   --fingerprint
"""
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import asset_editor as ae  # noqa: E402
import block3d as b3  # noqa: E402

fails = 0
def check(ok, what):
    global fails
    print(("ok   " if ok else "FAIL ") + what)
    if not ok: fails += 1

p, warnings = ae.load_project(ae.GAME_ASSETS)
blocks = {d["id_name"]: d for d in p.defs["block"].values()}
items = {d["id_name"]: d for d in p.defs["item"].values()}
check(len(blocks) >= 128 and len(items) >= 133, f"game assets load: {len(blocks)} blocks, {len(items)} items, {len(p.defs['ui'])} UIs, "
      f"{len(p.extra['recipe'])} recipes, {len(p.extra['sound'])} sounds ({len(warnings)} notes)")
tex = lambda tid: p.tex["block"][tid]["img"] if tid in p.tex["block"] else None

# ---- the 3D rules match the game
door = blocks["oak_door"]
_, bits = b3.schema(door)
check(bits == 4, f"door packs half + open + facing into {bits} bits")
v = b3.variant(door, {"half": "lower", "open": True, "facing": "north"})
check(v["boxes"][0][0] == (0, 0, 0, .1875, 1, 1), f"open door box {v['boxes'][0][0]}")
v = b3.variant(door, {"half": "upper", "open": False, "facing": "north"})
check(v["faces"][4] == ae_tex if (ae_tex := door["properties"]["definition"]["geometry"]["state_overrides"][0]["textures"]["all"]) else False, "upper door half uses its own texture")
st = b3.variant(blocks["oak_stairs"], {"facing": "east"})
hi = [b for b, _ in st["boxes"] if b[1] >= .5][0]
check(hi[0] >= .5 and hi[3] == 1, f"stairs facing east: high box on the east side {hi}")
fu = blocks["furnace"]
check(b3.variant(fu, {"lit": False, "facing": "south"})["faces"][4] == fu["textures"]["north"], "furnace facing south shows its front on the south face")
check(b3.variant(fu, {"lit": True, "facing": "north"})["faces"][5] != fu["textures"]["north"], "lit furnace front texture (state override)")
check(b3.schema(blocks["water"])[1] == 3, "water level 1..8 needs 3 bits")
check(b3.faces_of(b3.flatten(blocks["oak_log"]["textures"]))[2] != b3.faces_of(b3.flatten(blocks["oak_log"]["textures"]))[0], "log top differs from side")

# ---- icons rendered by the editor equal the game's item icons (same projection)
for n in ("furnace", "chest", "oxygen_generator", "stone", "oak_slab"):
    gen = b3.block_picture(blocks[n], tex, 32)
    have = p.tex["item"][items[n]["id"]]["img"].convert("RGBA")
    diff = sum(1 for a, b in zip(gen.getdata(), have.getdata()) if a != b)
    check(diff <= 16, f"generated {n} icon matches the game's icon ({diff} pixels differ)")

# ---- a preset on a copy
import copy  # noqa: E402
d = b3.apply_preset(copy.deepcopy(blocks["stone"]), "Door (2 tall, opens)")
check(d["properties"]["definition"]["behavior"] == "door" and "half" in d["properties"]["instance_state"] and b3.schema(d)[1] == 4, "door preset adds behaviour, parts and state")

# ---- old 2D projects still load
old = os.path.normpath(os.path.join(ae.ROOT, "..", "..", "refs", "AssetEditor", "projects", "Game"))
if os.path.isdir(old):
    p2, w2 = ae.load_project(old)
    check(len(p2.tex["block"]) > 0 and p2.extra["recipe"] == [], f"old 2D project loads ({len(p2.tex['block'])} block textures, {len(w2)} notes)")

# ---- export everything
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), "cubeplanet_editor_export")
p.save(out, project=False, atlases=True)
names = sorted(os.listdir(out))
check(len([n for n in names if n.endswith(".txt") or n.endswith(".png")]) >= 10, f"export wrote {len(names)} files to {out}")
print("EDITOR TEST " + ("OK" if fails == 0 else f"FAILED ({fails})"))
sys.exit(1 if fails else 0)
