"""
Builds the editor's windows without showing them (the main window is withdrawn before Tk ever draws it) and drives the Block 3D tab
through the same methods its buttons call: python AssetEditor/test_gui.py
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import asset_editor as ae  # noqa: E402
import block3d as b3  # noqa: E402

fails = 0
def check(ok, what):
    global fails
    print(("ok   " if ok else "FAIL ") + what)
    if not ok: fails += 1

app = ae.App(); app.withdraw()
try:
    app.project, _ = ae.load_project(ae.GAME_ASSETS); app.build_tabs()
    titles = [app.nb.tab(i, "text") for i in range(len(app.tabs))]
    check(titles[4] == "Block 3D" and "Sounds" in titles and "Recipes" in titles and "Entities" in titles, f"tabs: {', '.join(titles)}")
    des = app.tabs[4]; p = app.project
    door_id = next(i for i, d in p.defs["block"].items() if d["id_name"] == "oak_door")
    des.cur = door_id; des.load()
    check(des.v["model"].get() == "boxes" and des.v["rotation"].get() == "horizontal" and des.v["behavior"].get() == "door", "door loads into the form")
    check(set(des.sv) == {"half", "open", "facing"}, f"preview state controls: {sorted(des.sv)}")
    des.sv["open"][0].set("true"); des.update_preview()
    check(des._ph is not None, "preview renders the open door")
    check("4 / 16" in des.l_bits.cget("text"), f"bit counter: {des.l_bits.cget('text')}")
    # edit a copy of stone: preset, field, part, render flag, sound, icon
    sid = max(p.defs["block"]) + 1; p.defs["block"][sid] = copy.deepcopy(next(d for d in p.defs["block"].values() if d["id_name"] == "stone"))
    p.defs["block"][sid].update(id=sid, id_name="test_block")
    des.refresh(); des.cur = sid; des.load()
    des.c_preset.set("Stairs"); des.apply_preset()
    check(b3.defn(p.defs["block"][sid])["rotation"] == "horizontal" and len(b3.defn(p.defs["block"][sid])["boxes"]) == 2, "stairs preset applied")
    for k, v in {"name": "power", "type": "integer", "min": "0", "max": "15", "default": "0", "storage": "packed"}.items(): des.fv[k].set(v)
    des.field_set()
    check(b3.schema(p.defs["block"][sid])[1] == 6, f"integer 0..15 + facing = 6 bits ({des.l_bits.cget('text')})")
    for k, v in {"name": "big", "type": "integer", "min": "0", "max": "70000", "storage": "packed"}.items(): des.fv[k].set(v)
    des.field_set()
    check("16" in des.l_bits.cget("text") and str(des.l_bits.cget("foreground")) == "#a00", "over 16 bits is flagged")
    des.flb.selection_clear(0, "end"); des.flb.selection_set(1); des.field_remove()
    des.pv["x"].set("0"); des.pv["y"].set("1"); des.pv["z"].set("0"); des.pv["state"].set('{"power": 1}'); des.part_add()
    check(b3.defn(p.defs["block"][sid])["multiblock"]["parts"][0]["offset"] == [0, 1, 0], "multiblock part added")
    des.v["render.sway"].set("plant"); des.changed("render.sway")
    check(b3.defn(p.defs["block"][sid])["render"]["sway"] == "plant", "render flag written")
    des.v_texmode.set("six faces"); des.tex_mode()
    check(all(n in p.defs["block"][sid]["textures"] for n in b3.FACE_NAMES), "six-face texture slots filled from the current look")
    # icon for an item placing a real block
    fid = next(i for i, d in p.defs["block"].items() if d["id_name"] == "furnace"); des.cur = fid; des.load()
    iid = next(i for i, d in p.defs["item"].items() if d["id_name"] == "furnace"); before = p.tex["item"][iid]["img"].tobytes()
    des.make_icon()
    check(p.tex["item"][iid]["img"].tobytes() == before, "furnace icon regenerated identically")
    # list tabs
    app.tabs[6].refresh(); app.tabs[7].refresh(); app.tabs[8].refresh()
    check(app.tabs[6].lb.size() == len(p.extra["sound"]) and app.tabs[7].lb.size() == len(p.extra["recipe"]), "sounds and recipes listed")
finally:
    app.destroy()
print("GUI TEST " + ("OK" if fails == 0 else f"FAILED ({fails})"))
sys.exit(1 if fails else 0)
