#!/usr/bin/env python3
"""
Asset Editor - texture + definition editor for Cube Planet (3D) and the older 2D game.

Requires: Python 3.8+, Pillow (pip install pillow). GUI = Tkinter (bundled with Python).
Run:      python asset_editor.py
See README.md for full documentation.
"""
import copy
import json
import os
import re
import tkinter as tk
from tkinter import ttk, messagebox, filedialog, simpledialog, colorchooser

from PIL import Image, ImageTk

import block3d
import editor3d_tabs

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECTS_DIR = os.path.join(ROOT, "projects")
MAX_ID = 65535                      # IDs are 2 bytes in the game
SETTINGS_FILE = "project_settings.json"   # editor-only file (groups, namespace, sizes)

FILES = {
    "block_definitions": "block_definitions.txt",
    "item_definitions": "item_definitions.txt",
    "ui_definitions": "ui_definitions.txt",
    "item_atlas": "item_texture_atlas.png",
    "block_atlas": "block_texture_atlas.png",
    "item_texture_definitions": "item_texture_definitions.txt",
    "block_texture_definitions": "block_texture_definitions.txt",
}

# files beyond the original 7 (Cube Planet phase 4): optional when loading, always written by Save / Export
EXTRA_FILES = {"recipe": ("recipe_definitions.txt", "recipes"), "sound": ("sound_definitions.txt", "sounds"),
               "entity": ("entity_definitions.txt", "entities")}
GAME_ASSETS = os.path.normpath(os.path.join(ROOT, "..", "Assets"))  # the game's Assets folder when the editor sits in the CubePlanet repo

DEFAULT_SETTINGS = {
    "namespace": "base",
    "item_size": [32, 32], "block_size": [16, 16],
    "item_per_row": 128, "item_rows": 128,
    "block_per_row": 128, "block_rows": 128,
    "item_groups": [], "block_groups": [],
}

FULL_BOX = {"x": 0, "y": 0, "width": 1, "height": 1}
GEOMETRY_PRESETS = {
    "Full block": {"collision": [dict(FULL_BOX)], "selection": [dict(FULL_BOX)], "state_overrides": []},
    "Slab (lower)": {"collision": [{"x": 0, "y": 0, "width": 1, "height": 0.5}],
                     "selection": [{"x": 0, "y": 0, "width": 1, "height": 0.5}], "state_overrides": []},
    "Slab (upper)": {"collision": [{"x": 0, "y": 0.5, "width": 1, "height": 0.5}],
                     "selection": [{"x": 0, "y": 0.5, "width": 1, "height": 0.5}], "state_overrides": []},
    "Half width (fence-like)": {"collision": [{"x": 0.25, "y": 0, "width": 0.5, "height": 1}],
                                "selection": [{"x": 0.25, "y": 0, "width": 0.5, "height": 1}],
                                "state_overrides": []},
    "No collision (plant)": {"collision": [], "selection": [dict(FULL_BOX)], "state_overrides": []},
    "No hitbox at all": {"collision": [], "selection": [], "state_overrides": []},
}

UI_WIDGETS = {
    "label": {"type": "label", "text": "Label", "position": {"x": 8, "y": 5}},
    "slot": {"type": "slot", "name": "Slot", "position": {"x": 8, "y": 18},
             "storage": {"scope": "block", "key": "inventory.slot"}},
    "slot_grid": {"type": "slot_grid", "position": {"x": 7, "y": 18}, "rows": 3, "columns": 9,
                  "slot_size": 18, "id_name_pattern": "slot_{index}",
                  "storage": {"scope": "block", "key_pattern": "inventory.slots[{index}]"}},
    "progress_bar": {"type": "progress_bar", "name": "Progress", "position": {"x": 79, "y": 36},
                     "size": {"width": 24, "height": 16}, "current_from": "progress", "max": 100,
                     "orientation": "left_to_right"},
    "ui_panel": {"type": "ui_panel", "ui_id": {"namespace": "base", "id_name": "player_inventory"},
                 "position": {"x": 7, "y": 96}},
}

PALETTE = ["#000000", "#FFFFFF", "#7F7F7F", "#C3C3C3", "#880015", "#ED1C24", "#FF7F27", "#FFF200",
           "#22B14C", "#00A2E8", "#3F48CC", "#A349A4", "#B97A57", "#FFAEC9", "#FFC90E", "#EFE4B0",
           "#B5E61D", "#99D9EA", "#7092BE", "#C8BFE7", "#464646", "#5C3A21", "#2E6B34", "#8B4513"]

T = getattr(Image, "Transpose", Image)

# ----------------------------------------------------------------------------- helpers


def sanitize_name(s):
    return re.sub(r"[^a-z0-9_]+", "_", str(s).strip().lower()).strip("_")


def title_from(id_name):
    return id_name.replace("_", " ").title()


def unique_name(base, used):
    if base not in used:
        return base
    i = 2
    while f"{base}_{i}" in used:
        i += 1
    return f"{base}_{i}"


def lowest_free(used, lo, hi):
    """Lowest integer in [lo, hi) not in used, or None."""
    for i in range(lo, hi):
        if i not in used:
            return i
    return None


def parse_hex(s):
    s = s.strip().lstrip("#")
    if len(s) == 6:
        s += "FF"
    if not re.fullmatch(r"[0-9a-fA-F]{8}", s):
        return None
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4, 6))


def to_hex(c):
    return "#%02X%02X%02X%02X" % tuple(c)


def loads_tolerant(text):
    """json.loads that also accepts // and /* */ comments and trailing commas."""
    out, i, n, instr = [], 0, len(text), False
    while i < n:
        c = text[i]
        if instr:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 1
            elif c == '"':
                instr = False
        elif c == '"':
            instr = True
            out.append(c)
        elif c == "/" and text[i + 1:i + 2] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        elif c == "/" and text[i + 1:i + 2] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        else:
            out.append(c)
        i += 1
    s = re.sub(r",(\s*[}\]])", r"\1", "".join(out))
    return json.loads(s)


def extract_defs(data, key):
    if isinstance(data, list):
        return [d for d in data if isinstance(d, dict)]
    if isinstance(data, dict):
        if isinstance(data.get(key), list):
            return [d for d in data[key] if isinstance(d, dict)]
        if "type" in data:
            return [data]
    return []


def parse_tex_defs(text):
    """Returns (header, entries, skipped). Entry = (id, name, x|None, y|None).
    Missing/invalid x or y become None (the loader computes them from the ID)."""
    hdr, entries, skipped = {}, [], []
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        if ":" in line:
            k, v = line.split(":", 1)
            try:
                hdr[k.strip()] = [int(x) for x in v.split()]
            except ValueError:
                skipped.append(line)
            continue
        p = line.split()
        try:
            tid, name = int(p[0]), p[1]
        except (ValueError, IndexError):
            skipped.append(line)
            continue
        pos = []
        for tok in p[2:4]:
            try:
                pos.append(int(tok))
            except ValueError:
                pos.append(None)
        pos += [None] * (2 - len(pos))
        entries.append((tid, name, pos[0], pos[1]))
    return hdr, entries, skipped


def parse_paste(text, w, h):
    """Parse 'name\\n<h rows of w #RRGGBBAA tokens>' blocks. Returns ([(name, Image)], [errors])."""
    blocks, cur, errs = [], None, []
    for line in text.splitlines():
        s = line.strip()
        if not s:
            continue
        if "#" not in s:
            cur = [s, []]
            blocks.append(cur)
        elif cur is None:
            errs.append("Pixel row before any texture name: " + s[:30])
        else:
            cur[1].append(re.findall(r"#[0-9A-Fa-f]{8}|#[0-9A-Fa-f]{6}", s))
    res = []
    for name, rows in blocks:
        if len(rows) != h or any(len(r) != w for r in rows):
            errs.append(f"'{name}': expected {h} rows x {w} pixels, got {len(rows)} rows "
                        f"({', '.join(sorted({str(len(r)) for r in rows})) or '0'} px/row)")
            continue
        img = Image.new("RGBA", (w, h))
        img.putdata([parse_hex(tok) for r in rows for tok in r])
        res.append((name, img))
    return res, errs


def texture_to_text(name, img):
    w, h = img.size
    px = img.load()
    rows = ["".join(to_hex(px[x, y]) for x in range(w)) for y in range(h)]
    return name + "\n" + "\n".join(rows)


def line_points(a, b):
    (x0, y0), (x1, y1) = a, b
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err, pts = dx + dy, []
    while True:
        pts.append((x0, y0))
        if (x0, y0) == (x1, y1):
            return pts
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def collect_ints(node, out):
    if isinstance(node, dict):
        for v in node.values():
            collect_ints(v, out)
    elif isinstance(node, list):
        for v in node:
            collect_ints(v, out)
    elif isinstance(node, int) and not isinstance(node, bool):
        out.append(node)


def replace_ints(node, old, new):
    items = node.items() if isinstance(node, dict) else enumerate(node) if isinstance(node, list) else []
    for k, v in list(items):
        if isinstance(v, int) and not isinstance(v, bool):
            if v == old:
                node[k] = new
        else:
            replace_ints(v, old, new)


# ----------------------------------------------------------------------------- project model


class Project:
    def __init__(self, path=None):
        self.path = path
        self.s = copy.deepcopy(DEFAULT_SETTINGS)
        self.tex = {"item": {}, "block": {}}            # id -> {"name", "img"}
        self.defs = {"item": {}, "block": {}, "ui": {}}  # id -> definition dict
        self.extra = {"recipe": [], "sound": [], "entity": []}  # lists of definition dicts (recipe / sound / entity files)
        self.stale = {"item": set(), "block": set()}     # texture IDs whose PNG file must be deleted on save

    # --- geometry of atlases
    def size(self, k): return tuple(self.s[k + "_size"])
    def per_row(self, k): return self.s[k + "_per_row"]
    def rows(self, k): return self.s[k + "_rows"]
    def capacity(self, k): return min(MAX_ID + 1, self.per_row(k) * self.rows(k))
    def blank(self, k): return Image.new("RGBA", self.size(k), (0, 0, 0, 0))

    def pos(self, k, tid):
        w, h = self.size(k)
        return (tid % self.per_row(k)) * w, (tid // self.per_row(k)) * h

    # --- defaults / normalisation
    def default_def(self, kind, id_, name):
        ns = self.s["namespace"]
        d = {"type": kind, "namespace": ns, "id_name": name, "id": id_, "name": title_from(name)}
        if kind == "item":
            d.update(group_tags=[], properties={"definition": {"stack_size": 64}, "instance_state": {}})
        elif kind == "block":
            d.update(stateless=True, group_tags=[], textures={}, properties={
                "definition": {"default_state": "normal", "hardness": 1.0,
                               "geometry": copy.deepcopy(GEOMETRY_PRESETS["Full block"]),
                               "drops": [{"namespace": ns, "id_name": name, "count": 1}]},
                "instance_state": {}})
        else:
            d.update(window={"width": 176, "height": 166, "background": ""},
                     widgets=[{"type": "label", "id_name": "title", "text": title_from(name),
                               "position": {"x": 8, "y": 5}}])
        return d

    def normalize(self, d, kind, id_):
        d["type"], d["id"] = kind, id_
        d["id_name"] = sanitize_name(d.get("id_name") or "unnamed") or "unnamed"
        d.setdefault("namespace", self.s["namespace"])
        d.setdefault("name", title_from(d["id_name"]))
        if kind == "ui":
            if not isinstance(d.get("window"), dict):
                d["window"] = {"width": 176, "height": 166, "background": ""}
            if not isinstance(d.get("widgets"), list):
                d["widgets"] = []
        else:
            if not isinstance(d.get("group_tags"), list):
                d["group_tags"] = []
            if not isinstance(d.get("properties"), dict):
                d["properties"] = {}
            for key in ("definition", "instance_state"):
                if not isinstance(d["properties"].get(key), dict):
                    d["properties"][key] = {}
            if kind == "block":
                d["properties"]["definition"].setdefault("geometry", copy.deepcopy(GEOMETRY_PRESETS["Full block"]))
                d.setdefault("stateless", not d["properties"]["instance_state"])
                if not isinstance(d.get("textures"), dict):
                    d["textures"] = {}
        order = ["type", "namespace", "id_name", "id", "name", "stateless", "group_tags", "textures",
                 "ui_id", "window", "widgets", "properties"]
        out = {k: d[k] for k in order if k in d}
        out.update({k: v for k, v in d.items() if k not in out})
        return out

    # --- textures (+ item sync)
    def tex_names(self, k): return {t["name"] for t in self.tex[k].values()}

    def free_tex_id(self, k):
        used = set(self.tex[k]) | (set(self.defs["item"]) if k == "item" else set())
        return lowest_free(used, 1, self.capacity(k))

    def add_texture(self, k, name=None, img=None, tid=None):
        """Item texture => also creates the item. Block texture => texture only."""
        if tid is None:
            tid = self.free_tex_id(k)
        if tid is None:
            raise ValueError(f"No free {k} texture IDs left (atlas capacity {self.capacity(k)}).")
        base = sanitize_name(name or "") or f"{k}_texture"
        name = unique_name(base, self.tex_names(k))
        self.tex[k][tid] = {"name": name, "img": img if img is not None else self.blank(k)}
        if k == "item" and tid not in self.defs["item"]:
            self.defs["item"][tid] = self.default_def("item", tid, name)
        return tid

    def rename_texture(self, k, tid, name):
        name = sanitize_name(name)
        if not name:
            raise ValueError("Name cannot be empty.")
        if name in {t["name"] for i, t in self.tex[k].items() if i != tid}:
            raise ValueError(f"A {k} texture named '{name}' already exists.")
        old = self.tex[k][tid]["name"]
        self.tex[k][tid]["name"] = name
        if k == "item" and tid in self.defs["item"]:
            d = self.defs["item"][tid]
            if d.get("name") == title_from(old):
                d["name"] = title_from(name)
            d["id_name"] = name

    def move_tex_id(self, k, old, new):
        if not (1 <= new < self.capacity(k)):
            raise ValueError(f"ID must be 1..{self.capacity(k) - 1} (atlas capacity).")
        if new in self.tex[k] or (k == "item" and new in self.defs["item"]):
            raise ValueError(f"ID {new} is already used.")
        self.tex[k][new] = self.tex[k].pop(old)
        self.stale[k].add(old)
        if k == "item":
            d = self.defs["item"].pop(old, None)
            if d:
                d["id"] = new
                self.defs["item"][new] = d
        else:
            for b in self.defs["block"].values():
                replace_ints(b.get("textures", {}), old, new)

    def move_def_id(self, kind, old, new):
        if kind == "item":
            return self.move_tex_id("item", old, new)
        if not (1 <= new <= MAX_ID):
            raise ValueError(f"ID must be 1..{MAX_ID}.")
        if new in self.defs[kind]:
            raise ValueError(f"ID {new} is already used.")
        d = self.defs[kind].pop(old)
        d["id"] = new
        self.defs[kind][new] = d

    def block_refs(self, tid):
        n = 0
        for b in self.defs["block"].values():
            ints = []
            collect_ints(b.get("textures", {}), ints)
            n += ints.count(tid)
        return n

    def remove_texture(self, k, tid):
        self.tex[k].pop(tid, None)
        self.stale[k].add(tid)
        if k == "item":
            self.defs["item"].pop(tid, None)

    # --- block creation (block => item => item texture)
    def add_block(self, name, with_texture):
        bid = lowest_free(set(self.defs["block"]), 1, MAX_ID + 1)
        if bid is None:
            raise ValueError("No free block IDs left.")
        used = {d["id_name"] for d in self.defs["block"].values()}
        name = unique_name(sanitize_name(name) or "block", used)
        d = self.default_def("block", bid, name)
        if with_texture:
            d["textures"] = {"default": self.add_texture("block", name)}
        self.defs["block"][bid] = d
        if not any(i["id_name"] == name for i in self.defs["item"].values()):
            iid = self.add_texture("item", name)
            self.defs["item"][iid]["places_block"] = {"namespace": self.s["namespace"], "id_name": name}
        return bid

    # --- output
    def build_atlas(self, k):
        w, h = self.size(k)
        rows = self.rows(k)
        atlas = Image.new("RGBA", (self.per_row(k) * w, rows * h), (0, 0, 0, 0))
        for tid, t in self.tex[k].items():
            x, y = self.pos(k, tid)
            if y + h <= rows * h:
                atlas.paste(t["img"], (x, y))
        return atlas

    def tex_defs_text(self, k):
        w, h = self.size(k)
        lines = [f"texture_size: {w} {h}", f"textures_per_row: {self.per_row(k)}",
                 f"texture_rows: {self.rows(k)}"]
        for tid in sorted(self.tex[k]):
            x, y = self.pos(k, tid)
            lines.append(f"{tid} {self.tex[k][tid]['name']} {x} {y}")
        return "\n".join(lines) + "\n"

    def save(self, directory, project=True, atlases=False):
        """project=True : per-texture PNGs (item_texture/, block_texture/) + project_settings.json
        atlases=True    : (re)generate both atlas PNGs
        Always writes the texture definitions and item/block/ui definitions."""
        os.makedirs(directory, exist_ok=True)
        j = lambda name: os.path.join(directory, FILES[name])
        if project:
            for k in ("item", "block"):
                folder = os.path.join(directory, k + "_texture")
                os.makedirs(folder, exist_ok=True)
                for tid, t in self.tex[k].items():
                    t["img"].save(os.path.join(folder, f"{k}_texture_ID{tid}.png"), "PNG")
                for tid in self.stale[k] - set(self.tex[k]):
                    fp = os.path.join(folder, f"{k}_texture_ID{tid}.png")
                    if os.path.exists(fp):
                        os.remove(fp)
                self.stale[k].clear()
            with open(os.path.join(directory, SETTINGS_FILE), "w", encoding="utf-8") as f:
                json.dump(self.s, f, indent=2)
        if atlases:
            self.build_atlas("item").save(j("item_atlas"), "PNG")
            self.build_atlas("block").save(j("block_atlas"), "PNG")
        for k in ("item", "block"):
            with open(j(k + "_texture_definitions"), "w", encoding="utf-8") as f:
                f.write(self.tex_defs_text(k))
        for kind, key in (("item", "items"), ("block", "blocks"), ("ui", "uis")):
            data = {key: [self.defs[kind][i] for i in sorted(self.defs[kind])]}
            with open(j(kind + "_definitions"), "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
        for kind, (fn, key) in EXTRA_FILES.items():
            with open(os.path.join(directory, fn), "w", encoding="utf-8") as f:
                json.dump({key: self.extra[kind]}, f, indent=2, ensure_ascii=False)


def load_project(directory):
    p, warnings = Project(directory), []
    sp = os.path.join(directory, SETTINGS_FILE)
    if os.path.exists(sp):
        with open(sp, encoding="utf-8") as f:
            p.s.update(json.load(f))
    for k in ("item", "block"):
        tdp = os.path.join(directory, FILES[k + "_texture_definitions"])
        entries = []
        if os.path.exists(tdp):
            with open(tdp, encoding="utf-8") as f:
                hdr, entries, skipped = parse_tex_defs(f.read())
            if skipped:
                warnings.append(f"{FILES[k + '_texture_definitions']}: ignored unreadable line(s): "
                                + "; ".join(skipped[:3]))
            if len(hdr.get("texture_size", [])) >= 2:
                p.s[k + "_size"] = hdr["texture_size"][:2]
            if hdr.get("textures_per_row"):
                p.s[k + "_per_row"] = hdr["textures_per_row"][0]
            if hdr.get("texture_rows"):
                p.s[k + "_rows"] = hdr["texture_rows"][0]
        w, h = p.size(k)
        found = {}
        folder = os.path.join(directory, k + "_texture")
        if os.path.isdir(folder):
            for fn in os.listdir(folder):
                m = re.fullmatch(rf"{k}_texture_ID(\d+)\.png", fn)
                if m:
                    found[int(m.group(1))] = os.path.join(folder, fn)
        for tid in sorted(found.keys() - {e[0] for e in entries}):
            entries.append((tid, f"{k}_{tid}", None, None))
            warnings.append(f"{os.path.basename(found[tid])} was not in the definitions: named '{k}_{tid}'")
        ap = os.path.join(directory, FILES[k + "_atlas"])
        atlas, migrated, blank = None, 0, 0
        for tid, name, x, y in entries:
            if tid < 1 or tid >= p.capacity(k):
                warnings.append(f"Skipped {k} texture {tid} '{name}': ID outside atlas capacity")
                continue
            if tid in found:
                img = Image.open(found[tid]).convert("RGBA")
                if img.size != (w, h):
                    img = img.resize((w, h), Image.NEAREST)
                    warnings.append(f"{k} texture {tid} was {Image.open(found[tid]).size}: resized to {w}x{h}")
            else:
                if atlas is None and os.path.exists(ap):
                    atlas = Image.open(ap).convert("RGBA")
                if atlas is not None:
                    x, y = (x, y) if x is not None and y is not None else p.pos(k, tid)
                    img = atlas.crop((x, y, x + w, y + h)).copy()
                    migrated += 1
                else:
                    img, blank = p.blank(k), blank + 1
            p.tex[k][tid] = {"name": name, "img": img}
        if migrated:
            warnings.append(f"{migrated} {k} texture(s) had no PNG file and were cut from the atlas "
                            "(Save will write their PNG files)")
        if blank:
            warnings.append(f"{blank} {k} texture(s) had no PNG file and no atlas: created blank")
    for kind, key in (("item", "items"), ("block", "blocks"), ("ui", "uis")):
        path = os.path.join(directory, FILES[kind + "_definitions"])
        if not os.path.exists(path):
            warnings.append(f"Missing {FILES[kind + '_definitions']}")
            continue
        with open(path, encoding="utf-8") as f:
            raw = f.read()
        if not raw.strip():
            continue  # empty file = no definitions yet
        try:
            data = loads_tolerant(raw)
        except ValueError as e:
            raise ValueError(f"{FILES[kind + '_definitions']}: {e}")
        for d in extract_defs(data, key):
            try:
                id_ = int(d["id"])
            except (KeyError, ValueError, TypeError):
                warnings.append(f"Skipped a {kind} definition without a valid id")
                continue
            p.defs[kind][id_] = p.normalize(d, kind, id_)
    for kind, (fn, key) in EXTRA_FILES.items():  # optional (old 2D projects do not have them)
        path = os.path.join(directory, fn)
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                raw = f.read()
            if raw.strip():
                try:
                    p.extra[kind] = [d for d in extract_defs(loads_tolerant(raw), key) if isinstance(d, dict)]
                except ValueError as e:
                    raise ValueError(f"{fn}: {e}")
    # Re-establish item <-> item texture links
    for tid, t in list(p.tex["item"].items()):
        if tid not in p.defs["item"]:
            p.defs["item"][tid] = p.default_def("item", tid, t["name"])
            warnings.append(f"Item texture {tid} had no item: created '{t['name']}'")
    for iid, d in list(p.defs["item"].items()):
        if iid not in p.tex["item"]:
            p.tex["item"][iid] = {"name": d["id_name"], "img": p.blank("item")}
            warnings.append(f"Item {iid} had no item texture: created blank one")
        elif p.tex["item"][iid]["name"] != d["id_name"]:
            p.tex["item"][iid]["name"] = d["id_name"]
    return p, warnings


# ----------------------------------------------------------------------------- texture editor tab


class TextureEditor(ttk.Frame):
    def __init__(self, master, app, kind):
        super().__init__(master)
        self.app, self.kind, self.p = app, kind, app.project
        self.cur, self.img, self.undo, self.redo = None, None, [], []
        self.color, self.last, self.rects, self.cell = (0, 0, 0, 255), None, {}, 16
        self.tool = tk.StringVar(value="pen")
        self.palette = list(PALETTE)
        self._lock = False
        self._build()
        self.refresh()

    # ---- layout
    def _build(self):
        left = ttk.Frame(self)
        left.pack(side="left", fill="y", padx=4, pady=4)
        bf = ttk.Frame(left)
        bf.pack(side="bottom", fill="x")
        for i, (t, c) in enumerate((("New", self.new), ("Duplicate", self.duplicate), ("Delete", self.delete))):
            ttk.Button(bf, text=t, command=c).grid(row=0, column=i, sticky="ew")
            bf.columnconfigure(i, weight=1)
        lf = ttk.Frame(left)
        lf.pack(fill="both", expand=True)
        self.lb = tk.Listbox(lf, width=30, exportselection=False, font=("Courier", 10))
        sb = ttk.Scrollbar(lf, command=self.lb.yview)
        self.lb.config(yscrollcommand=sb.set)
        self.lb.pack(side="left", fill="y")
        sb.pack(side="left", fill="y")
        self.lb.bind("<<ListboxSelect>>", self.on_select)

        mid = ttk.Frame(self)
        mid.pack(side="left", fill="both", expand=True, padx=4, pady=4)
        top = ttk.Frame(mid)
        top.pack(fill="x")
        self.id_var, self.name_var = tk.StringVar(), tk.StringVar()
        ttk.Label(top, text="ID:").pack(side="left")
        ttk.Spinbox(top, from_=1, to=MAX_ID, width=7, textvariable=self.id_var).pack(side="left", padx=2)
        ttk.Label(top, text="Name:").pack(side="left", padx=(8, 0))
        ttk.Entry(top, width=24, textvariable=self.name_var).pack(side="left", padx=2)
        ttk.Button(top, text="Apply ID/Name", command=self.apply_meta).pack(side="left", padx=4)
        self.info = ttk.Label(top, text="")
        self.info.pack(side="left", padx=8)
        self.canvas = tk.Canvas(mid, bg="#222", highlightthickness=0)
        self.canvas.pack(anchor="nw", pady=6)
        self.canvas.bind("<Button-1>", self.press)
        self.canvas.bind("<B1-Motion>", self.drag)

        right = ttk.Frame(self)
        right.pack(side="left", fill="y", padx=4, pady=4)
        tl = ttk.LabelFrame(right, text="Tools")
        tl.pack(fill="x")
        for text, val in (("Pen", "pen"), ("Eraser", "erase"), ("Fill region", "fill"),
                          ("Picker", "pick"), ("Transparent (click color -> remove all)", "trans")):
            ttk.Radiobutton(tl, text=text, value=val, variable=self.tool).pack(anchor="w")
        ur = ttk.Frame(tl)
        ur.pack(fill="x")
        ttk.Button(ur, text="Undo", command=self.do_undo).pack(side="left", expand=True, fill="x")
        ttk.Button(ur, text="Redo", command=self.do_redo).pack(side="left", expand=True, fill="x")

        cf = ttk.LabelFrame(right, text="Color (RGBA)")
        cf.pack(fill="x", pady=4)
        self.swatch = tk.Label(cf, width=6, height=2, relief="sunken", bg="#000000")
        self.swatch.grid(row=0, column=0, rowspan=2, padx=2, pady=2)
        self.swatch.bind("<Button-1>", lambda e: self.choose_color())
        self.hex_var = tk.StringVar()
        he = ttk.Entry(cf, width=11, textvariable=self.hex_var)
        he.grid(row=0, column=1, columnspan=2)
        he.bind("<Return>", lambda e: self.apply_hex())
        ttk.Button(cf, text="Wheel...", command=self.choose_color).grid(row=1, column=1, sticky="ew")
        ttk.Button(cf, text="Clear (alpha 0)", command=lambda: self.set_color((0, 0, 0, 0))).grid(
            row=1, column=2, sticky="ew")
        self.alpha = tk.Scale(cf, from_=0, to=255, orient="horizontal", label="Alpha", command=self.on_alpha)
        self.alpha.set(255)
        self.alpha.grid(row=2, column=0, columnspan=3, sticky="ew")
        pf = ttk.LabelFrame(right, text="Palette (right-click = store current color)")
        pf.pack(fill="x")
        self.pal_btns = []
        for i, col in enumerate(self.palette):
            b = tk.Button(pf, bg=col, width=2, height=1, command=lambda i=i: self.pick_palette(i))
            b.grid(row=i // 8, column=i % 8)
            b.bind("<Button-3>", lambda e, i=i: self.store_palette(i))
            b.bind("<Button-2>", lambda e, i=i: self.store_palette(i))
            self.pal_btns.append(b)

        xf = ttk.LabelFrame(right, text="Canvas")
        xf.pack(fill="x", pady=4)
        for i, (t, c) in enumerate((
                ("Clear", self.op_clear), ("Fill color", self.op_fill), ("Rotate 90", self.op_rotate),
                ("Flip vertical", lambda: self.xform(lambda im: im.transpose(T.FLIP_TOP_BOTTOM))),
                ("Mirror horiz.", lambda: self.xform(lambda im: im.transpose(T.FLIP_LEFT_RIGHT))))):
            ttk.Button(xf, text=t, command=c).grid(row=i // 2, column=i % 2, sticky="ew")
        pf2 = ttk.LabelFrame(right, text="Text import / export (whole editor)")
        pf2.pack(fill="x")
        ttk.Button(pf2, text="Paste textures (bulk)...", command=self.paste_dialog).pack(fill="x")
        ttk.Button(pf2, text="Copy selected as text", command=self.copy_text).pack(fill="x")
        self.set_color(self.color)

    # ---- list
    def refresh(self, select=None):
        self.p = self.app.project
        want = select if select is not None else self.cur
        self.ids = sorted(self.p.tex[self.kind])
        self.lb.delete(0, "end")
        for i in self.ids:
            self.lb.insert("end", f"{i:5d}  {self.p.tex[self.kind][i]['name']}")
        if want in self.p.tex[self.kind]:
            self.lb.selection_set(self.ids.index(want))
            self.lb.see(self.ids.index(want))
            self.load(want)
        else:
            self.cur, self.img = None, None
            self.build_canvas()
        w, h = self.p.size(self.kind)
        self.info.config(text=f"{w}x{h} px | {len(self.ids)} textures | capacity {self.p.capacity(self.kind)}")

    def on_select(self, _=None):
        sel = self.lb.curselection()
        if sel:
            self.load(self.ids[sel[0]])

    def load(self, tid):
        t = self.p.tex[self.kind][tid]
        self.cur, self.img, self.undo, self.redo = tid, t["img"], [], []
        self.id_var.set(str(tid))
        self.name_var.set(t["name"])
        self.build_canvas()

    def new(self):
        name = simpledialog.askstring("New texture", "Name:", initialvalue=f"new_{self.kind}", parent=self)
        if not name:
            return
        try:
            tid = self.p.add_texture(self.kind, name)
        except ValueError as e:
            return messagebox.showerror("Error", str(e))
        self.app.dirty = True
        self.refresh(select=tid)

    def duplicate(self):
        if self.cur is None:
            return
        try:
            tid = self.p.add_texture(self.kind, self.p.tex[self.kind][self.cur]["name"] + "_copy",
                                     self.img.copy())
        except ValueError as e:
            return messagebox.showerror("Error", str(e))
        self.app.dirty = True
        self.refresh(select=tid)

    def delete(self):
        if self.cur is None:
            return
        msg = f"Delete texture {self.cur}?"
        if self.kind == "item":
            msg += "\nThe item with the same ID (and its definition) is deleted too."
        else:
            n = self.p.block_refs(self.cur)
            if n:
                msg += f"\n{n} block definition value(s) still reference this texture ID."
        if messagebox.askyesno("Delete", msg):
            self.p.remove_texture(self.kind, self.cur)
            self.cur = None
            self.app.dirty = True
            self.refresh()

    def apply_meta(self):
        if self.cur is None:
            return
        try:
            new = int(self.id_var.get())
            tid = self.cur
            if new != tid:
                self.p.move_tex_id(self.kind, tid, new)
                tid = new
            if sanitize_name(self.name_var.get()) != self.p.tex[self.kind][tid]["name"]:
                self.p.rename_texture(self.kind, tid, self.name_var.get())
        except ValueError as e:
            messagebox.showerror("Error", str(e))
            tid = self.cur if self.cur in self.p.tex[self.kind] else None
        self.app.dirty = True
        self.refresh(select=tid)

    # ---- canvas
    def disp(self, x, y, p):
        base = 204 if (x + y) % 2 == 0 else 153
        a = p[3] / 255
        return "#%02x%02x%02x" % tuple(int(p[i] * a + base * (1 - a)) for i in range(3))

    def build_canvas(self):
        self.canvas.delete("all")
        self.rects = {}
        w, h = self.img.size if self.img else self.p.size(self.kind)
        self.dims = (w, h)
        self.cell = max(4, min(32, 512 // max(w, h)))
        c = self.cell
        self.canvas.config(width=w * c, height=h * c)
        if self.img is None:
            return
        px = self.img.load()
        for y in range(h):
            for x in range(w):
                self.rects[(x, y)] = self.canvas.create_rectangle(
                    x * c, y * c, (x + 1) * c, (y + 1) * c, fill=self.disp(x, y, px[x, y]),
                    outline="#666" if c >= 8 else "")

    def redraw_all(self):
        px = self.img.load()
        for (x, y), r in self.rects.items():
            self.canvas.itemconfig(r, fill=self.disp(x, y, px[x, y]))

    def swap(self, img):
        self.img = img
        self.p.tex[self.kind][self.cur]["img"] = img
        if img.size != self.dims:
            self.build_canvas()
        else:
            self.redraw_all()
        self.app.dirty = True

    def cell_at(self, ev):
        if self.img is None:
            return None
        x, y = ev.x // self.cell, ev.y // self.cell
        w, h = self.img.size
        return (x, y) if 0 <= x < w and 0 <= y < h else None

    def push_undo(self):
        self.undo.append(self.img.copy())
        if len(self.undo) > 100:
            self.undo.pop(0)
        self.redo.clear()

    def do_undo(self):
        if self.undo and self.img is not None:
            self.redo.append(self.img.copy())
            self.swap(self.undo.pop())

    def do_redo(self):
        if self.redo and self.img is not None:
            self.undo.append(self.img.copy())
            self.swap(self.redo.pop())

    def put(self, c):
        val = (0, 0, 0, 0) if self.tool.get() == "erase" else self.color
        self.img.putpixel(c, val)
        self.canvas.itemconfig(self.rects[c], fill=self.disp(c[0], c[1], val))

    def press(self, ev):
        c = self.cell_at(ev)
        if not c:
            return
        tool = self.tool.get()
        if tool == "pick":
            return self.set_color(self.img.getpixel(c))
        self.push_undo()
        self.app.dirty = True
        if tool in ("pen", "erase"):
            self.last = c
            self.put(c)
        elif tool == "fill":
            self.flood(c)
        elif tool == "trans":
            px = self.img.load()
            target = px[c]
            w, h = self.img.size
            for y in range(h):
                for x in range(w):
                    if px[x, y] == target:
                        px[x, y] = (0, 0, 0, 0)
            self.redraw_all()

    def drag(self, ev):
        if self.tool.get() not in ("pen", "erase") or self.last is None:
            return
        c = self.cell_at(ev)
        if c and c != self.last:
            for pt in line_points(self.last, c):
                self.put(pt)
            self.last = c

    def flood(self, start):
        px = self.img.load()
        w, h = self.img.size
        target, new = px[start], tuple(self.color)
        if target == new:
            return
        stack = [start]
        while stack:
            x, y = stack.pop()
            if 0 <= x < w and 0 <= y < h and px[x, y] == target:
                px[x, y] = new
                stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
        self.redraw_all()

    # ---- canvas operations
    def xform(self, fn):
        if self.img is None:
            return
        self.push_undo()
        self.swap(fn(self.img))

    def op_clear(self): self.xform(lambda im: Image.new("RGBA", im.size, (0, 0, 0, 0)))
    def op_fill(self): self.xform(lambda im: Image.new("RGBA", im.size, tuple(self.color)))

    def op_rotate(self):
        if self.img is not None and self.img.size[0] != self.img.size[1]:
            return messagebox.showinfo("Rotate", "Only square textures can be rotated 90 degrees.")
        self.xform(lambda im: im.transpose(T.ROTATE_270))

    # ---- colors
    def set_color(self, c):
        self.color = tuple(c)
        self._lock = True
        self.hex_var.set(to_hex(c))
        self.alpha.set(c[3])
        self._lock = False
        self.swatch.config(bg="#%02x%02x%02x" % self.color[:3])

    def on_alpha(self, v):
        if not self._lock:
            self.set_color(self.color[:3] + (int(float(v)),))

    def apply_hex(self):
        c = parse_hex(self.hex_var.get())
        if c:
            self.set_color(c)
        else:
            messagebox.showerror("Color", "Use #RRGGBB or #RRGGBBAA")

    def choose_color(self):
        res = colorchooser.askcolor(color="#%02x%02x%02x" % self.color[:3], parent=self)
        if res and res[0]:
            self.set_color(tuple(int(v) for v in res[0]) + (self.color[3],))

    def pick_palette(self, i):
        c = parse_hex(self.palette[i])
        self.set_color(c)

    def store_palette(self, i):
        hx = "#%02X%02X%02X" % self.color[:3]
        self.palette[i] = hx
        self.pal_btns[i].config(bg=hx)

    # ---- text import / export
    def paste_dialog(self):
        w, h = self.p.size(self.kind)
        win = tk.Toplevel(self)
        win.title(f"Paste {self.kind} textures ({w}x{h})")
        ttk.Label(win, text=f"Format: name line, then {h} rows of {w} #RRGGBBAA values. "
                            "Several textures can follow each other.").pack(anchor="w", padx=6, pady=4)
        txt = tk.Text(win, width=110, height=30, font=("Courier", 9))
        txt.pack(fill="both", expand=True, padx=6)

        def go():
            res, errs = parse_paste(txt.get("1.0", "end"), w, h)
            added = 0
            for name, img in res:
                try:
                    self.p.add_texture(self.kind, name, img)
                    added += 1
                except ValueError as e:
                    errs.append(str(e))
                    break
            self.app.dirty = True
            self.refresh()
            win.destroy()
            msg = f"Imported {added} texture(s)."
            if errs:
                msg += "\n\nProblems:\n" + "\n".join(errs[:15])
            messagebox.showinfo("Import", msg)
        ttk.Button(win, text="Import", command=go).pack(pady=6)

    def copy_text(self):
        if self.img is None:
            return
        self.clipboard_clear()
        self.clipboard_append(texture_to_text(self.p.tex[self.kind][self.cur]["name"], self.img))
        messagebox.showinfo("Copied", "Texture copied to the clipboard in paste format.")


# ----------------------------------------------------------------------------- definition editor tab


class DefEditor(ttk.Frame):
    TITLES = {"item": "Item", "block": "Block", "ui": "UI"}

    def __init__(self, master, app, kind):
        super().__init__(master)
        self.app, self.kind, self.p = app, kind, app.project
        self.cur, self.ids, self.modified = None, [], False
        self._build()
        self.refresh()

    def _build(self):
        k = self.kind
        left = ttk.Frame(self)
        left.pack(side="left", fill="y", padx=4, pady=4)
        bf = ttk.Frame(left)
        bf.pack(side="bottom", fill="x")
        ttk.Button(bf, text="New", command=self.new).pack(side="left", expand=True, fill="x")
        ttk.Button(bf, text="Delete", command=self.delete).pack(side="left", expand=True, fill="x")
        lf = ttk.Frame(left)
        lf.pack(fill="both", expand=True)
        self.lb = tk.Listbox(lf, width=30, exportselection=False, font=("Courier", 10))
        sb = ttk.Scrollbar(lf, command=self.lb.yview)
        self.lb.config(yscrollcommand=sb.set)
        self.lb.pack(side="left", fill="y")
        sb.pack(side="left", fill="y")
        self.lb.bind("<<ListboxSelect>>", self.on_select)

        mid = ttk.Frame(self)
        mid.pack(side="left", fill="both", expand=True, padx=4, pady=4)
        form = ttk.Frame(mid)
        form.pack(fill="x")
        self.v_id, self.v_idname, self.v_name, self.v_ns = (tk.StringVar() for _ in range(4))
        self.v_stateless = tk.BooleanVar()
        ttk.Label(form, text="ID").grid(row=0, column=0)
        ttk.Entry(form, width=7, textvariable=self.v_id).grid(row=0, column=1)
        ttk.Button(form, text="Change ID", command=self.change_id).grid(row=0, column=2, padx=2)
        ttk.Label(form, text="id_name").grid(row=0, column=3)
        ttk.Entry(form, width=18, textvariable=self.v_idname).grid(row=0, column=4)
        ttk.Label(form, text="name").grid(row=0, column=5)
        ttk.Entry(form, width=18, textvariable=self.v_name).grid(row=0, column=6)
        ttk.Label(form, text="namespace").grid(row=0, column=7)
        ttk.Entry(form, width=10, textvariable=self.v_ns).grid(row=0, column=8)
        if k == "block":
            ttk.Checkbutton(form, text="stateless", variable=self.v_stateless).grid(row=0, column=9, padx=4)
        ttk.Button(form, text="Apply form -> JSON", command=self.apply_form).grid(row=0, column=10, padx=4)

        tf = ttk.Frame(mid)
        tf.pack(fill="both", expand=True, pady=4)
        self.txt = tk.Text(tf, font=("Courier", 10), undo=True, wrap="none")
        ys = ttk.Scrollbar(tf, command=self.txt.yview)
        xs = ttk.Scrollbar(mid, orient="horizontal", command=self.txt.xview)
        self.txt.config(yscrollcommand=ys.set, xscrollcommand=xs.set)
        ys.pack(side="right", fill="y")
        self.txt.pack(side="left", fill="both", expand=True)
        xs.pack(fill="x")
        self.txt.bind("<KeyRelease>", lambda e: setattr(self, "modified", True))
        bb = ttk.Frame(mid)
        bb.pack(fill="x", pady=4)
        ttk.Button(bb, text="Save definition", command=self.save_def).pack(side="left", padx=2)
        ttk.Button(bb, text="Validate / format", command=self.validate).pack(side="left", padx=2)
        ttk.Button(bb, text="Revert", command=self.revert).pack(side="left", padx=2)
        ttk.Label(bb, text="(// comments and trailing commas are accepted; output is clean JSON)").pack(
            side="left", padx=8)

        right = ttk.Frame(self)
        right.pack(side="left", fill="y", padx=4, pady=4)
        if k != "ui":
            gf = ttk.LabelFrame(right, text="Groups -> group_tags")
            gf.pack(fill="x")
            self.glb = tk.Listbox(gf, selectmode="multiple", height=7, width=26, exportselection=False)
            self.glb.pack(fill="x")
            gb = ttk.Frame(gf)
            gb.pack(fill="x")
            ttk.Button(gb, text="Add group", command=self.add_group).pack(side="left", expand=True, fill="x")
            ttk.Button(gb, text="Remove group", command=self.remove_group).pack(side="left", expand=True, fill="x")
            ttk.Button(gf, text="Apply selection to group_tags", command=self.apply_groups).pack(fill="x")

        pf = ttk.LabelFrame(right, text="Add property")
        pf.pack(fill="x", pady=4)
        self.c_target = ttk.Combobox(pf, state="readonly", width=24, values=(
            ["window", "(top level)"] if k == "ui" else
            ["properties.definition", "properties.instance_state", "(top level)"]))
        self.c_target.current(0)
        self.c_target.pack(fill="x")
        self.v_pkey, self.v_pval = tk.StringVar(), tk.StringVar()
        ttk.Label(pf, text="key (dots = nested)").pack(anchor="w")
        ttk.Entry(pf, textvariable=self.v_pkey).pack(fill="x")
        ttk.Label(pf, text="value (JSON, e.g. 5, \"text\", [1,2])").pack(anchor="w")
        ttk.Entry(pf, textvariable=self.v_pval).pack(fill="x")
        ttk.Button(pf, text="Add / set", command=self.add_prop).pack(fill="x")

        if k == "block":
            tf2 = ttk.LabelFrame(right, text="Texture slot")
            tf2.pack(fill="x")
            self.v_slot = tk.StringVar(value="default")
            ttk.Label(tf2, text="slot (e.g. default, lower.closed)").pack(anchor="w")
            ttk.Entry(tf2, textvariable=self.v_slot).pack(fill="x")
            self.c_tex = ttk.Combobox(tf2, state="readonly", width=24)
            self.c_tex.pack(fill="x")
            ttk.Button(tf2, text="Set texture slot", command=self.set_slot).pack(fill="x")
            gf2 = ttk.LabelFrame(right, text="Geometry preset")
            gf2.pack(fill="x", pady=4)
            self.c_geo = ttk.Combobox(gf2, state="readonly", width=24,
                                      values=list(block3d.GEOMETRY_PRESETS_3D) + ["2D: " + n for n in GEOMETRY_PRESETS])
            self.c_geo.current(0)
            self.c_geo.pack(fill="x")
            ttk.Button(gf2, text="Apply geometry", command=self.set_geo).pack(fill="x")
            uf = ttk.LabelFrame(right, text="Interaction UI (ui_id)")
            uf.pack(fill="x")
            self.c_ui = ttk.Combobox(uf, state="readonly", width=24)
            self.c_ui.pack(fill="x")
            ttk.Button(uf, text="Link UI", command=self.link_ui).pack(fill="x")
        if k == "ui":
            wf = ttk.LabelFrame(right, text="Add widget")
            wf.pack(fill="x")
            for wt in UI_WIDGETS:
                ttk.Button(wf, text=wt, command=lambda wt=wt: self.add_widget(wt)).pack(fill="x")

    # ---- list handling
    def refresh(self):
        self.p = self.app.project
        self.ids = sorted(self.p.defs[self.kind])
        self.lb.delete(0, "end")
        for i in self.ids:
            self.lb.insert("end", f"{i:5d}  {self.p.defs[self.kind][i]['id_name']}")
        if self.kind != "ui":
            self.glb.delete(0, "end")
            for g in self.p.s[self.kind + "_groups"]:
                self.glb.insert("end", g)
        if self.kind == "block":
            self.c_tex["values"] = [f"{i} {t['name']}" for i, t in sorted(self.p.tex["block"].items())]
            self.c_ui["values"] = [d["id_name"] for _, d in sorted(self.p.defs["ui"].items())]
        if self.cur in self.p.defs[self.kind]:
            self.lb.selection_set(self.ids.index(self.cur))
            if not self.modified:
                self.load_def(self.cur)
        else:
            self.cur = None
            self.txt.delete("1.0", "end")

    def select_id(self, i):
        self.lb.selection_clear(0, "end")
        if i in self.ids:
            self.lb.selection_set(self.ids.index(i))

    def on_select(self, _=None):
        sel = self.lb.curselection()
        if not sel or self.ids[sel[0]] == self.cur:
            return
        if self.modified and self.cur in self.p.defs[self.kind]:
            if not self.save_def(silent=True):
                if not messagebox.askyesno("Unsaved", "Current JSON is invalid. Discard your changes?"):
                    return self.select_id(self.cur)
        self.load_def(self.ids[sel[0]])

    def load_def(self, i):
        d = self.p.defs[self.kind][i]
        self.cur = i
        self.v_id.set(str(i))
        self.v_idname.set(d["id_name"])
        self.v_name.set(d.get("name", ""))
        self.v_ns.set(d.get("namespace", ""))
        self.v_stateless.set(bool(d.get("stateless", True)))
        self.show(d)
        self.modified = False
        if self.kind != "ui":
            self.glb.selection_clear(0, "end")
            for n, g in enumerate(self.p.s[self.kind + "_groups"]):
                if g in d.get("group_tags", []):
                    self.glb.selection_set(n)

    def new(self):
        name = simpledialog.askstring(f"New {self.kind}", "id_name (e.g. stone_sword):", parent=self)
        if not name:
            return
        try:
            if self.kind == "item":
                i = self.p.add_texture("item", name)
            elif self.kind == "block":
                tex = messagebox.askyesno("Block texture", "Also create a matching block texture\n"
                                          "and set textures.default to it?")
                i = self.p.add_block(name, tex)
            else:
                i = lowest_free(set(self.p.defs["ui"]), 1, MAX_ID + 1)
                nm = unique_name(sanitize_name(name) or "ui", {d["id_name"] for d in self.p.defs["ui"].values()})
                self.p.defs["ui"][i] = self.p.default_def("ui", i, nm)
        except ValueError as e:
            return messagebox.showerror("Error", str(e))
        self.app.dirty = True
        self.modified = False
        self.cur = i
        self.refresh()
        self.load_def(i)

    def delete(self):
        if self.cur is None:
            return
        msg = f"Delete {self.kind} {self.cur}?"
        if self.kind == "item":
            msg += "\nThe item texture with the same ID is deleted too."
        if self.kind == "block":
            msg += "\n(The matching item and any textures are kept.)"
        if messagebox.askyesno("Delete", msg):
            if self.kind == "item":
                self.p.remove_texture("item", self.cur)
            else:
                self.p.defs[self.kind].pop(self.cur)
            self.cur, self.modified = None, False
            self.app.dirty = True
            self.refresh()

    def change_id(self):
        if self.cur is None:
            return
        try:
            new = int(self.v_id.get())
            self.save_def(silent=True)
            self.p.move_def_id(self.kind, self.cur, new)
        except ValueError as e:
            return messagebox.showerror("Error", str(e))
        self.cur = new
        self.app.dirty = True
        self.modified = False
        self.refresh()
        self.load_def(new)

    # ---- JSON text
    def show(self, d):
        self.txt.delete("1.0", "end")
        self.txt.insert("1.0", json.dumps(d, indent=2, ensure_ascii=False))

    def parse(self, quiet=False):
        try:
            d = loads_tolerant(self.txt.get("1.0", "end"))
            if not isinstance(d, dict):
                raise ValueError("The definition must be a JSON object { ... }")
            return d
        except ValueError as e:
            if not quiet:
                messagebox.showerror("Invalid JSON", str(e))
            return None

    def edit_json(self, fn):
        if self.cur is None:
            return
        d = self.parse()
        if d is None:
            return
        fn(d)
        self.show(self.p.normalize(d, self.kind, self.cur))
        self.modified = True

    def validate(self):
        self.edit_json(lambda d: None)

    def revert(self):
        if self.cur in self.p.defs[self.kind]:
            self.load_def(self.cur)

    def save_def(self, silent=False):
        if self.cur is None:
            return False
        d = self.parse(quiet=silent)
        if d is None:
            return False
        d = self.p.normalize(d, self.kind, self.cur)
        for i, o in self.p.defs[self.kind].items():
            if i != self.cur and o["id_name"] == d["id_name"] and o.get("namespace") == d["namespace"]:
                if not silent:
                    messagebox.showerror("Duplicate", f"id_name '{d['id_name']}' is used by ID {i}.")
                return False
        old = self.p.defs[self.kind][self.cur]
        if self.kind == "item" and d["id_name"] != old["id_name"]:
            try:
                self.p.rename_texture("item", self.cur, d["id_name"])
            except ValueError as e:
                if not silent:
                    messagebox.showerror("Rename", str(e))
                return False
        self.p.defs[self.kind][self.cur] = d
        if self.kind != "ui":
            groups = self.p.s[self.kind + "_groups"]
            for g in d.get("group_tags", []):
                if g not in groups:
                    groups.append(g)
        self.modified = False
        self.app.dirty = True
        self.refresh()
        self.load_def(self.cur)
        return True

    def apply_form(self):
        def fn(d):
            d["id_name"] = self.v_idname.get()
            d["name"] = self.v_name.get()
            d["namespace"] = self.v_ns.get() or self.p.s["namespace"]
            if self.kind == "block":
                d["stateless"] = self.v_stateless.get()
        self.edit_json(fn)

    # ---- helpers that edit the JSON
    @staticmethod
    def set_path(root, path, value):
        parts = path.split(".")
        for part in parts[:-1]:
            if not isinstance(root.get(part), dict):
                root[part] = {}
            root = root[part]
        root[parts[-1]] = value

    def add_prop(self):
        key = self.v_pkey.get().strip()
        if not key:
            return
        raw = self.v_pval.get().strip()
        try:
            val = json.loads(raw) if raw else None
        except ValueError:
            val = raw
        target = self.c_target.get()
        prefix = "" if target == "(top level)" else target + "."
        self.edit_json(lambda d: self.set_path(d, prefix + key, val))

    def set_slot(self):
        sel = self.c_tex.get()
        if not sel or not self.v_slot.get().strip():
            return messagebox.showinfo("Texture slot", "Pick a block texture and a slot name.")
        self.edit_json(lambda d: self.set_path(d, "textures." + self.v_slot.get().strip(), int(sel.split()[0])))

    def set_geo(self):
        name = self.c_geo.get()
        if name in block3d.GEOMETRY_PRESETS_3D:
            return self.edit_json(lambda d: block3d.apply_preset(d, name))
        g = copy.deepcopy(GEOMETRY_PRESETS[name[4:] if name.startswith("2D: ") else name])
        self.edit_json(lambda d: self.set_path(d, "properties.definition.geometry", g))

    def link_ui(self):
        name = self.c_ui.get()
        if name:
            self.edit_json(lambda d: d.__setitem__("ui_id", {"namespace": self.p.s["namespace"], "id_name": name}))

    def add_widget(self, wt):
        def fn(d):
            w = copy.deepcopy(UI_WIDGETS[wt])
            ids = {x.get("id_name") for x in d.setdefault("widgets", []) if isinstance(x, dict)}
            w = {"type": w.pop("type"), "id_name": unique_name(f"{wt}_{len(ids) + 1}", ids), **w}
            d["widgets"].append(w)
        self.edit_json(fn)

    # ---- groups
    def add_group(self):
        g = simpledialog.askstring("New group", "Group name (e.g. smeltable):", parent=self)
        g = sanitize_name(g or "")
        if g and g not in self.p.s[self.kind + "_groups"]:
            self.p.s[self.kind + "_groups"].append(g)
            self.app.dirty = True
            self.refresh()

    def remove_group(self):
        sel = self.glb.curselection()
        if not sel:
            return
        names = [self.glb.get(i) for i in sel]
        if not messagebox.askyesno("Remove", f"Remove group(s) {', '.join(names)} from the project\n"
                                             "and from every definition's group_tags?"):
            return
        for n in names:
            self.p.s[self.kind + "_groups"].remove(n)
            for d in self.p.defs[self.kind].values():
                if n in d.get("group_tags", []):
                    d["group_tags"].remove(n)
        self.app.dirty = True
        self.refresh()
        if self.cur is not None:
            self.load_def(self.cur)

    def apply_groups(self):
        tags = [self.glb.get(i) for i in self.glb.curselection()]
        self.edit_json(lambda d: d.__setitem__("group_tags", tags))

    def flush(self):
        if self.modified:
            self.save_def(silent=True)


# ----------------------------------------------------------------------------- settings tab


class SettingsTab(ttk.Frame):
    def __init__(self, master, app):
        super().__init__(master)
        self.app = app
        self.v = {}
        s = app.project.s
        g = ttk.Frame(self)
        g.pack(anchor="nw", padx=12, pady=12)
        rows = [("namespace", "Default namespace", s["namespace"])]
        for k in ("item", "block"):
            rows += [(f"{k}_w", f"{k.title()} texture width", s[k + "_size"][0]),
                     (f"{k}_h", f"{k.title()} texture height", s[k + "_size"][1]),
                     (f"{k}_per_row", f"{k.title()} atlas: textures per row", s[k + "_per_row"]),
                     (f"{k}_rows", f"{k.title()} atlas: rows", s[k + "_rows"])]
        for r, (key, label, val) in enumerate(rows):
            ttk.Label(g, text=label).grid(row=r, column=0, sticky="w", pady=2)
            self.v[key] = tk.StringVar(value=str(val))
            ttk.Entry(g, width=14, textvariable=self.v[key]).grid(row=r, column=1, padx=8)
        ttk.Button(g, text="Apply settings", command=self.apply).grid(row=len(rows), column=0, pady=10, sticky="w")
        ttk.Button(g, text="Generate atlases now", command=app.generate_atlases).grid(
            row=len(rows), column=1, pady=10, sticky="w")
        self.info = ttk.Label(self, justify="left")
        self.info.pack(anchor="nw", padx=12)
        self.refresh()

    def refresh(self):
        p = self.app.project
        t = []
        for k in ("item", "block"):
            w, h = p.size(k)
            t.append(f"{k} atlas: {p.per_row(k) * w} x {p.rows(k) * h} px, capacity {p.capacity(k)} "
                     f"textures (IDs 1..{p.capacity(k) - 1}), {len(p.tex[k])} used")
        t.append("Block ID 0 / item ID 0 are reserved (air / none): the first atlas cell stays empty.")
        t.append("Project folder: " + (p.path or "(unsaved - Save will ask for a project name)"))
        self.info.config(text="\n".join(t))

    def apply(self):
        p = self.app.project
        try:
            val = {k: int(v.get()) for k, v in self.v.items() if k != "namespace"}
            ns = sanitize_name(self.v["namespace"].get()) or "base"
            for k in ("item", "block"):
                if min(val[k + "_w"], val[k + "_h"], val[k + "_per_row"], val[k + "_rows"]) < 1:
                    raise ValueError("All values must be >= 1.")
                if val[k + "_per_row"] * val[k + "_w"] > 32768 or val[k + "_rows"] * val[k + "_h"] > 32768:
                    raise ValueError(f"{k} atlas would exceed 32768 px on a side.")
                cap = min(MAX_ID + 1, val[k + "_per_row"] * val[k + "_rows"])
                if p.tex[k] and max(p.tex[k]) >= cap:
                    raise ValueError(f"{k} atlas too small: highest texture ID is {max(p.tex[k])}.")
        except ValueError as e:
            return messagebox.showerror("Settings", str(e))
        for k in ("item", "block"):
            new = [val[k + "_w"], val[k + "_h"]]
            if new != p.s[k + "_size"] and p.tex[k]:
                if not messagebox.askyesno("Resize", f"Resize all {len(p.tex[k])} {k} textures to "
                                                      f"{new[0]}x{new[1]} (nearest neighbour)?"):
                    return
                for t in p.tex[k].values():
                    t["img"] = t["img"].resize(tuple(new), Image.NEAREST)
            p.s[k + "_size"] = new
            p.s[k + "_per_row"], p.s[k + "_rows"] = val[k + "_per_row"], val[k + "_rows"]
        p.s["namespace"] = ns
        self.app.dirty = True
        self.refresh()
        self.app.build_tabs(select=self)


# ----------------------------------------------------------------------------- application


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.geometry("1360x860")
        self.project, self.dirty, self.nb = Project(), False, None
        os.makedirs(PROJECTS_DIR, exist_ok=True)
        self.status = tk.StringVar()
        ttk.Label(self, textvariable=self.status, relief="sunken", anchor="w").pack(side="bottom", fill="x")
        m = tk.Menu(self)
        fm = tk.Menu(m, tearoff=0)
        fm.add_command(label="New project...", command=self.new_project)
        fm.add_command(label="Open (select project files)...", command=self.open_files)
        fm.add_command(label="Open project folder...", command=self.open_folder)
        if os.path.isdir(GAME_ASSETS):
            fm.add_command(label="Open Cube Planet Assets folder", command=lambda: self.open_dir(GAME_ASSETS))
        fm.add_separator()
        fm.add_command(label="Save project (texture PNGs + definitions)", command=self.save, accelerator="Ctrl+S")
        fm.add_command(label="Generate atlases (save + build both atlas PNGs)", command=self.generate_atlases,
                       accelerator="Ctrl+G")
        fm.add_command(label="Export game files to folder (builds atlases)...", command=self.export)
        fm.add_separator()
        fm.add_command(label="Quit", command=self.quit_app)
        m.add_cascade(label="File", menu=fm)
        self.config(menu=m)
        self.bind("<Control-s>", lambda e: self.save())
        self.bind("<Control-g>", lambda e: self.generate_atlases())
        self.protocol("WM_DELETE_WINDOW", self.quit_app)
        self.build_tabs()

    def title_update(self):
        p = self.project
        self.title(f"Asset Editor - {os.path.basename(p.path) if p.path else '(unsaved project)'}")

    def build_tabs(self, select=None):
        idx = self.nb.index(self.nb.select()) if self.nb and select is not None else 0
        if self.nb:
            self.nb.destroy()
        self.nb = ttk.Notebook(self)
        self.nb.pack(fill="both", expand=True)
        self.tabs = [TextureEditor(self.nb, self, "item"), TextureEditor(self.nb, self, "block"),
                     DefEditor(self.nb, self, "item"), DefEditor(self.nb, self, "block"),
                     editor3d_tabs.BlockDesigner(self.nb, self), DefEditor(self.nb, self, "ui"),
                     editor3d_tabs.sounds_tab(self.nb, self), editor3d_tabs.recipes_tab(self.nb, self),
                     editor3d_tabs.EntitiesTab(self.nb, self), SettingsTab(self.nb, self)]
        for title, w in zip(("Item Textures", "Block Textures", "Item Definitions", "Block Definitions", "Block 3D",
                             "UI Definitions", "Sounds", "Recipes", "Entities", "Project Settings"), self.tabs):
            self.nb.add(w, text=title)
        self.nb.bind("<<NotebookTabChanged>>", self.on_tab)
        if select is not None:
            self.nb.select(idx)
        self.title_update()

    def on_tab(self, _=None):
        self.flush()  # JSON typed in one tab is applied before another tab shows the same definitions
        w = self.nametowidget(self.nb.select())
        if hasattr(w, "refresh"):
            w.refresh()

    def confirm_discard(self):
        return (not self.dirty) or messagebox.askyesno("Unsaved changes", "Discard unsaved changes?")

    def new_project(self, keep=False):
        if not keep and not self.confirm_discard():
            return False
        name = simpledialog.askstring("New project", "Project name (folder created in ./projects):", parent=self)
        if not name:
            return False
        name = re.sub(r"[^A-Za-z0-9_\-]+", "_", name.strip()).strip("_")
        path = os.path.join(PROJECTS_DIR, name)
        if not name or (os.path.exists(path) and os.listdir(path)):
            messagebox.showerror("New project", "Invalid name, or the folder already exists and is not empty.")
            return False
        os.makedirs(path, exist_ok=True)
        if keep:
            self.project.path = path
        else:
            self.project, self.dirty = Project(path), False
            self.build_tabs()
            self.save()
        self.title_update()
        return True

    def open_files(self):
        files = filedialog.askopenfilenames(
            title="Select the project files (any/all of the 7 files)", initialdir=PROJECTS_DIR,
            filetypes=[("Project files", "*.txt *.png *.json"), ("All files", "*.*")])
        if files:
            self.open_dir(os.path.dirname(files[0]))

    def open_folder(self):
        d = filedialog.askdirectory(initialdir=PROJECTS_DIR, title="Project folder")
        if d:
            self.open_dir(d)

    def open_dir(self, d):
        if not self.confirm_discard():
            return
        try:
            self.project, warnings = load_project(d)
        except Exception as e:
            return messagebox.showerror("Load failed", str(e))
        self.dirty = False
        self.build_tabs()
        self.status.set(f"Loaded {d}")
        if warnings:
            messagebox.showwarning("Loaded with notes", "\n".join(warnings[:20]))

    def flush(self):
        for t in self.tabs:
            if hasattr(t, "flush"):
                t.flush()

    def save(self, atlases=False):
        if not self.project.path and not self.new_project(keep=True):
            return
        self.flush()
        try:
            self.project.save(self.project.path, project=True, atlases=atlases)
        except Exception as e:
            return messagebox.showerror("Save failed", str(e))
        self.dirty = False
        self.status.set(f"Saved to {self.project.path}" + (" (atlases generated)" if atlases else ""))

    def generate_atlases(self):
        self.save(atlases=True)

    def export(self):
        d = filedialog.askdirectory(title="Export game files to...", initialdir=GAME_ASSETS if os.path.isdir(GAME_ASSETS) else PROJECTS_DIR)
        if not d:
            return
        self.flush()
        try:
            self.project.save(d, project=False, atlases=True)
        except Exception as e:
            return messagebox.showerror("Export failed", str(e))
        self.status.set(f"Exported the game files (7 + recipes, sounds, entities; fresh atlases) to {d}")

    def quit_app(self):
        if self.confirm_discard():
            self.destroy()


if __name__ == "__main__":
    App().mainloop()