"""
Cube Planet tabs of the Asset Editor: Block 3D (model, face textures with thumbnails, render flags, geometry presets, rotation, behaviour,
UI, sounds, instance-state fields with the 16-bit check, multiblock parts, a 3D preview per state, item icon from the block) and simple
list editors for sounds, recipes and entities. They edit the project's definition dicts directly; the JSON tabs show the result.
"""
import copy
import json
import tkinter as tk
from tkinter import ttk, messagebox, simpledialog

from PIL import Image, ImageTk

import block3d as b3


def _set(root, path, value):
    parts = path.split(".")
    for part in parts[:-1]:
        if not isinstance(root.get(part), dict): root[part] = {}
        root = root[part]
    if value is None: root.pop(parts[-1], None)
    else: root[parts[-1]] = value


def _get(root, path, default=None):
    for part in path.split("."):
        if not isinstance(root, dict) or part not in root: return default
        root = root[part]
    return root


class BlockDesigner(ttk.Frame):
    """Block 3D tab."""
    TEX_MODES = {"all": ["all"], "top / side / bottom": ["top", "side", "bottom"], "six faces": b3.FACE_NAMES}

    def __init__(self, master, app):
        super().__init__(master)
        self.app, self.cur, self.ids, self.thumbs, self.loading = app, None, [], {}, False
        self._build()
        self.refresh()

    @property
    def p(self): return self.app.project
    @property
    def d(self): return self.p.defs["block"].get(self.cur)

    # ---------------------------------------------------------------- layout
    def _build(self):
        left = ttk.Frame(self); left.pack(side="left", fill="y", padx=4, pady=4)
        self.v_filter = tk.StringVar(); self.v_filter.trace_add("write", lambda *_: self.refresh())
        ttk.Entry(left, textvariable=self.v_filter).pack(fill="x")
        self.lb = tk.Listbox(left, width=28, exportselection=False, font=("Courier", 10)); self.lb.pack(fill="y", expand=True)
        self.lb.bind("<<ListboxSelect>>", lambda e: self.on_select())

        mid = ttk.Frame(self); mid.pack(side="left", fill="both", expand=True, padx=4, pady=4)
        f = ttk.LabelFrame(mid, text="Model, rotation, behaviour"); f.pack(fill="x")
        self.v = {}
        def combo(parent, key, label, values, r, c, width=14, readonly=True):
            ttk.Label(parent, text=label).grid(row=r, column=c, sticky="e", padx=2)
            cb = ttk.Combobox(parent, values=values, width=width, state="readonly" if readonly else "normal")
            cb.grid(row=r, column=c + 1, sticky="w", pady=1); cb.bind("<<ComboboxSelected>>", lambda e, k=key: self.changed(k))
            if not readonly: cb.bind("<Return>", lambda e, k=key: self.changed(k))
            self.v[key] = cb; return cb
        combo(f, "model", "model", b3.MODELS, 0, 0); combo(f, "attach", "attach", ["", "vine", "pod"], 0, 2)
        combo(f, "rotation", "rotation", b3.ROTATIONS, 0, 4); combo(f, "placement_facing", "placement", ["toward_player", "away"], 1, 0)
        combo(f, "behavior", "behavior", b3.BEHAVIORS, 1, 2); combo(f, "ui_id", "UI (ui_id)", [], 1, 4)

        tf = ttk.LabelFrame(mid, text="Face textures (thumbnails show the default state)"); tf.pack(fill="x", pady=4)
        mf = ttk.Frame(tf); mf.pack(fill="x")
        self.v_texmode = tk.StringVar(value="all")
        for m in self.TEX_MODES:
            ttk.Radiobutton(mf, text=m, value=m, variable=self.v_texmode, command=self.tex_mode).pack(side="left", padx=4)
        self.slot_frame = ttk.Frame(tf); self.slot_frame.pack(fill="x")

        rf = ttk.LabelFrame(mid, text="Render"); rf.pack(fill="x")
        combo(rf, "render.layer", "layer", b3.RENDER_LAYERS, 0, 0); combo(rf, "render.sway", "sway", b3.SWAYS, 0, 2)
        combo(rf, "render.connect_group", "connect group", ["", "leaves", "glass", "ice"], 0, 4, readonly=False)
        combo(rf, "render.emissive", "emissive", ["0", "0.5", "1"], 1, 0, 6, readonly=False)
        self.chk = {}
        for i, (key, label, dflt) in enumerate([("render.casts_shadow", "casts shadow", True), ("waterlogged", "waterlogged", False), ("random_offset", "random offset", True)]):
            var = tk.BooleanVar(value=dflt); self.chk[key] = (var, dflt)
            ttk.Checkbutton(rf, text=label, variable=var, command=lambda k=key: self.changed(k)).grid(row=1, column=2 + i, padx=4, sticky="w")

        gf = ttk.LabelFrame(mid, text="Geometry preset (writes model, boxes, collision / selection; doors and beds also parts and state)")
        gf.pack(fill="x", pady=4)
        self.c_preset = ttk.Combobox(gf, values=list(b3.GEOMETRY_PRESETS_3D), state="readonly", width=24); self.c_preset.current(0)
        self.c_preset.pack(side="left", padx=4); ttk.Button(gf, text="Apply preset", command=self.apply_preset).pack(side="left")

        sf = ttk.LabelFrame(mid, text="Sounds (sound id_names from the Sounds tab; nothing plays in the game yet)"); sf.pack(fill="x")
        for i, slot in enumerate(b3.SOUND_SLOTS): combo(sf, "sounds." + slot, slot, [], 0, i * 2, 12)

        st = ttk.LabelFrame(mid, text="Instance state fields"); st.pack(fill="both", expand=True, pady=4)
        self.flb = tk.Listbox(st, height=6, exportselection=False, font=("Courier", 9)); self.flb.pack(side="left", fill="both", expand=True)
        self.flb.bind("<<ListboxSelect>>", lambda e: self.field_selected())
        ff = ttk.Frame(st); ff.pack(side="left", fill="y", padx=4)
        self.fv = {k: tk.StringVar() for k in ("name", "type", "default", "values", "min", "max", "slots", "storage")}
        for r, (k, label) in enumerate([("name", "name"), ("type", "type"), ("default", "default"), ("values", "values (a,b,c)"),
                                        ("min", "min"), ("max", "max"), ("slots", "slots"), ("storage", "storage")]):
            ttk.Label(ff, text=label).grid(row=r, column=0, sticky="e")
            if k == "type": ttk.Combobox(ff, textvariable=self.fv[k], values=b3.FIELD_TYPES, state="readonly", width=12).grid(row=r, column=1)
            elif k == "storage": ttk.Combobox(ff, textvariable=self.fv[k], values=["packed", "entity"], state="readonly", width=12).grid(row=r, column=1)
            else: ttk.Entry(ff, textvariable=self.fv[k], width=15).grid(row=r, column=1)
        bf = ttk.Frame(ff); bf.grid(row=8, column=0, columnspan=2)
        ttk.Button(bf, text="Add / update", command=self.field_set).pack(side="left"); ttk.Button(bf, text="Remove", command=self.field_remove).pack(side="left")
        self.l_bits = ttk.Label(ff, text=""); self.l_bits.grid(row=9, column=0, columnspan=2)

        right = ttk.Frame(self); right.pack(side="left", fill="y", padx=4, pady=4)
        self.preview = ttk.Label(right); self.preview.pack()
        self.state_frame = ttk.LabelFrame(right, text="Preview state"); self.state_frame.pack(fill="x", pady=4)
        ttk.Button(right, text="Generate item icon from block", command=self.make_icon).pack(fill="x")
        mp = ttk.LabelFrame(right, text="Multiblock parts (offset relative to the facing; north = -z)"); mp.pack(fill="x", pady=6)
        self.plb = tk.Listbox(mp, height=4, exportselection=False, font=("Courier", 9)); self.plb.pack(fill="x")
        pf = ttk.Frame(mp); pf.pack(fill="x")
        self.pv = {k: tk.StringVar(value="0") for k in "xyz"}; self.pv["state"] = tk.StringVar(value='{"half": "upper"}')
        for i, k in enumerate("xyz"): ttk.Label(pf, text=k).grid(row=0, column=i * 2); ttk.Entry(pf, textvariable=self.pv[k], width=4).grid(row=0, column=i * 2 + 1)
        ttk.Entry(mp, textvariable=self.pv["state"]).pack(fill="x")
        pb = ttk.Frame(mp); pb.pack(fill="x")
        ttk.Button(pb, text="Add part", command=self.part_add).pack(side="left", expand=True, fill="x")
        ttk.Button(pb, text="Remove part", command=self.part_remove).pack(side="left", expand=True, fill="x")
        self.status = ttk.Label(right, text="", wraplength=260, foreground="#a33"); self.status.pack(fill="x", pady=4)

    # ---------------------------------------------------------------- list / load
    def refresh(self):
        flt = self.v_filter.get().strip().lower()
        self.ids = [i for i in sorted(self.p.defs["block"]) if flt in self.p.defs["block"][i]["id_name"]]
        self.lb.delete(0, "end")
        for i in self.ids: self.lb.insert("end", f"{i:5d}  {self.p.defs['block'][i]['id_name']}")
        self.v["ui_id"]["values"] = [""] + [d["id_name"] for _, d in sorted(self.p.defs["ui"].items())]
        sounds = [""] + [s.get("id_name", "") for s in self.p.extra["sound"]]
        for slot in b3.SOUND_SLOTS: self.v["sounds." + slot]["values"] = sounds
        if self.cur in self.ids: self.lb.selection_set(self.ids.index(self.cur)); self.load()
        elif self.ids: self.cur = self.ids[0]; self.lb.selection_set(0); self.load()

    def on_select(self):
        sel = self.lb.curselection()
        if sel: self.cur = self.ids[sel[0]]; self.load()

    def load(self):
        d = self.d
        if d is None: return
        self.loading = True
        pd = b3.defn(d)
        for key, cb in self.v.items():
            if key == "ui_id": cb.set(_get(d, "ui_id.id_name", ""))
            elif key.startswith("sounds."): cb.set(_get(pd, key, ""))
            else: cb.set(str(_get(pd, key, {"model": "cube", "rotation": "none", "placement_facing": "toward_player", "render.layer": "opaque" if pd.get("model", "cube") == "cube" else "cutout", "render.sway": "none", "render.emissive": "0"}.get(key, ""))))
        for key, (var, dflt) in self.chk.items(): var.set(bool(_get(pd, key, dflt)))
        tex = d.get("textures", {}) or {}
        self.v_texmode.set("six faces" if any(n in tex for n in ("north", "south", "east", "west")) else "top / side / bottom" if any(n in tex for n in ("top", "side", "bottom")) else "all")
        self.loading = False
        self.tex_mode(rewrite=False); self.fields_refresh(); self.parts_refresh(); self.state_controls(); self.update_preview()

    # ---------------------------------------------------------------- edits
    def dirty(self):
        self.app.dirty = True; self.fields_refresh(); self.update_preview()

    def changed(self, key):
        if self.loading or self.d is None: return
        d = self.d; pd = d.setdefault("properties", {}).setdefault("definition", {})
        if key in self.chk:
            var, dflt = self.chk[key]; _set(pd, key, None if var.get() == dflt else var.get())
        elif key == "ui_id":
            n = self.v[key].get(); d.pop("ui_id", None)
            if n: d["ui_id"] = {"namespace": self.p.s["namespace"], "id_name": n}
        else:
            val = self.v[key].get().strip()
            if key == "render.emissive":
                try: val = float(val or 0)
                except ValueError: val = 0.0
                _set(pd, key, val or None)
            elif key == "attach": _set(pd, key, val or None)
            elif key.startswith("sounds.") or key in ("behavior", "render.connect_group"): _set(pd, key, val or None)
            else: _set(pd, key, val)
            if key == "model" and val == "boxes" and not pd.get("boxes"): pd["boxes"] = [b3.box(0, 0, 0, 1, 1, 1)]
        if key == "rotation": self.state_controls()
        self.dirty()

    def tex_mode(self, rewrite=True):
        for w in self.slot_frame.winfo_children(): w.destroy()
        d = self.d
        if d is None: return
        slots = self.TEX_MODES[self.v_texmode.get()]
        tex = d.setdefault("textures", {}); flat = b3.flatten(tex)
        if rewrite and not self.loading:   # switching mode: fill the new slots from the current look, drop the others
            faces = b3.faces_of(flat); new = {}
            for s in slots: new[s] = faces[b3.FACE_NAMES.index(s)] if s in b3.FACE_NAMES else (faces[0] if s == "side" else faces[2] if s == "all" else faces[b3.FACE_NAMES.index(s)])
            for k in [k for k in tex if not isinstance(tex[k], dict)]: tex.pop(k)
            tex.update(new); flat = b3.flatten(tex); self.app.dirty = True
        names = [f"{i} {t['name']}" for i, t in sorted(self.p.tex["block"].items())]
        for r, s in enumerate(slots):
            ttk.Label(self.slot_frame, text=s, width=7).grid(row=r // 3, column=(r % 3) * 3, sticky="e")
            lab = ttk.Label(self.slot_frame); lab.grid(row=r // 3, column=(r % 3) * 3 + 1)
            cb = ttk.Combobox(self.slot_frame, values=names, state="readonly", width=20); cb.grid(row=r // 3, column=(r % 3) * 3 + 2, padx=(0, 8))
            tid = flat.get(s, b3.texture_of(flat, s) if flat else None)
            if tid in self.p.tex["block"]: cb.set(f"{tid} {self.p.tex['block'][tid]['name']}")
            self.thumb(lab, tid)
            cb.bind("<<ComboboxSelected>>", lambda e, s=s, cb=cb, lab=lab: self.set_slot(s, cb, lab))
        self.update_preview()

    def thumb(self, label, tid):
        im = self.p.tex["block"].get(tid, {}).get("img")
        im = (im or Image.new("RGBA", (16, 16), (255, 0, 255, 255))).resize((32, 32), Image.NEAREST)
        ph = ImageTk.PhotoImage(im); self.thumbs[id(label)] = ph; label.configure(image=ph)

    def set_slot(self, slot, cb, lab):
        tid = int(cb.get().split()[0]); self.d.setdefault("textures", {})[slot] = tid; self.thumb(lab, tid); self.dirty()

    def apply_preset(self):
        if self.d is None: return
        b3.apply_preset(self.d, self.c_preset.get()); self.load(); self.app.dirty = True

    # ---------------------------------------------------------------- instance state
    def fields_refresh(self):
        if self.d is None: return
        ist = self.d.get("properties", {}).get("instance_state", {}) or {}
        self.flb.delete(0, "end")
        for k, f in ist.items():
            bits = b3.field_bits(f) if isinstance(f, dict) else 0
            self.flb.insert("end", f"{k:14s} {f.get('type', '?') if isinstance(f, dict) else '?':9s} {'entity' if bits == 0 else str(bits) + ' bit'}")
        fields, total = b3.schema(self.d)
        auto = " (incl. automatic facing)" if any(n == "facing" for n, _, _ in fields) and "facing" not in ist else ""
        self.l_bits.configure(text=f"packed bits: {total} / 16{auto}", foreground="#a00" if total > 16 else "#070")
        self.status.configure(text="Too many packed state bits: mark big fields storage = entity" if total > 16 else "")

    def field_selected(self):
        sel = self.flb.curselection()
        if not sel: return
        ist = self.d["properties"]["instance_state"]; name = list(ist)[sel[0]]; f = ist[name]
        self.fv["name"].set(name); self.fv["type"].set(f.get("type", "string")); self.fv["default"].set(json.dumps(f.get("default", "")))
        self.fv["values"].set(",".join(map(str, f.get("values", [])))); self.fv["min"].set(str(f.get("min", ""))); self.fv["max"].set(str(f.get("max", "")))
        self.fv["slots"].set(str(f.get("slots", ""))); self.fv["storage"].set(f.get("storage", "packed"))

    def field_set(self):
        if self.d is None: return
        name = self.fv["name"].get().strip()
        if not name: return messagebox.showinfo("State field", "Give the field a name.")
        t = self.fv["type"].get() or "string"; f = {"type": t}
        raw = self.fv["default"].get().strip()
        if raw:
            try: f["default"] = json.loads(raw)
            except ValueError: f["default"] = raw
        if t == "string": f["values"] = [v.strip() for v in self.fv["values"].get().split(",") if v.strip()]
        if t == "integer":
            for k in ("min", "max"):
                if self.fv[k].get().strip(): f[k] = int(self.fv[k].get())
        if t == "inventory": f["slots"] = int(self.fv["slots"].get() or 1)
        if self.fv["storage"].get() == "entity" and t in ("boolean", "string", "integer"): f["storage"] = "entity"
        self.d.setdefault("properties", {}).setdefault("instance_state", {})[name] = f
        self.d["stateless"] = False; self.state_controls(); self.dirty()

    def field_remove(self):
        sel = self.flb.curselection()
        if not sel: return
        ist = self.d["properties"]["instance_state"]; ist.pop(list(ist)[sel[0]])
        if not ist: self.d["stateless"] = True
        self.state_controls(); self.dirty()

    # ---------------------------------------------------------------- multiblock
    def parts_refresh(self):
        self.plb.delete(0, "end")
        for prt in _get(b3.defn(self.d) if self.d else {}, "multiblock.parts", []) or []:
            self.plb.insert("end", f"{prt.get('offset')}  {json.dumps(prt.get('state', {}))}")

    def part_add(self):
        if self.d is None: return
        try: off = [int(self.pv[k].get()) for k in "xyz"]; st = json.loads(self.pv["state"].get() or "{}")
        except ValueError as e: return messagebox.showerror("Part", f"Offset must be integers and state JSON: {e}")
        pd = self.d.setdefault("properties", {}).setdefault("definition", {})
        pd.setdefault("multiblock", {}).setdefault("parts", []).append({"offset": off, "state": st})
        self.parts_refresh(); self.dirty()

    def part_remove(self):
        sel = self.plb.curselection()
        if not sel: return
        parts = b3.defn(self.d)["multiblock"]["parts"]; parts.pop(sel[0])
        if not parts: b3.defn(self.d).pop("multiblock")
        self.parts_refresh(); self.dirty()

    # ---------------------------------------------------------------- preview
    def state_controls(self):
        for w in self.state_frame.winfo_children(): w.destroy()
        self.sv = {}
        if self.d is None: return
        fields, _ = b3.schema(self.d); dv = b3.default_values(self.d)
        for r, (name, f, _) in enumerate(fields):
            vals = [json.dumps(v) if not isinstance(v, str) else v for v in b3.field_values(f)]
            ttk.Label(self.state_frame, text=name).grid(row=r, column=0, sticky="e")
            cb = ttk.Combobox(self.state_frame, values=vals, state="readonly", width=12); cb.grid(row=r, column=1)
            d0 = dv.get(name); cb.set(d0 if isinstance(d0, str) else json.dumps(d0))
            if name == "facing": cb.set("south")
            cb.bind("<<ComboboxSelected>>", lambda e: self.update_preview()); self.sv[name] = (cb, f)

    def preview_state(self):
        st = {}
        for name, (cb, f) in getattr(self, "sv", {}).items():
            v = cb.get(); st[name] = v if f.get("type", "string") == "string" or name == "facing" else json.loads(v)
        return st

    def update_preview(self):
        if self.d is None or self.loading: return
        try:
            tex = lambda tid: self.p.tex["block"].get(tid, {}).get("img")
            im = b3.block_picture(self.d, tex, 192, self.preview_state() if getattr(self, "sv", None) else None)
        except Exception as e:  # half-written definitions must not break the tab
            im = Image.new("RGBA", (192, 192)); self.status.configure(text=f"preview: {e}")
        bg = Image.new("RGBA", im.size, (60, 60, 70, 255)); bg.alpha_composite(im)
        self._ph = ImageTk.PhotoImage(bg); self.preview.configure(image=self._ph)

    def make_icon(self):
        d = self.d
        if d is None: return
        ref = (d.get("namespace", self.p.s["namespace"]), d["id_name"])
        iid = next((i for i, it in self.p.defs["item"].items() if (_get(it, "properties.definition.places_block.namespace"), _get(it, "properties.definition.places_block.id_name")) == ref), None)
        if iid is None: iid = next((i for i, it in self.p.defs["item"].items() if it["id_name"] == d["id_name"]), None)
        if iid is None: return messagebox.showinfo("Item icon", f"No item places '{d['id_name']}' (create it in the Item Definitions tab).")
        w, h = self.p.size("item")
        self.p.tex["item"][iid]["img"] = b3.block_picture(d, lambda tid: self.p.tex["block"].get(tid, {}).get("img"), w).resize((w, h), Image.NEAREST)
        self.app.dirty = True; self.status.configure(text=f"item texture {iid} '{self.p.tex['item'][iid]['name']}' rendered from the block", foreground="#070")

    def flush(self): pass


class ListTab(ttk.Frame):
    """A list of definition dicts (project.extra[kind]) edited with a small form. fields: (path, label, choices or None)."""
    def __init__(self, master, app, kind, fields, new_def, title):
        super().__init__(master)
        self.app, self.kind, self.fields, self.new_def, self.cur = app, kind, fields, new_def, None
        left = ttk.Frame(self); left.pack(side="left", fill="y", padx=4, pady=4)
        self.lb = tk.Listbox(left, width=34, exportselection=False, font=("Courier", 10)); self.lb.pack(fill="y", expand=True)
        self.lb.bind("<<ListboxSelect>>", lambda e: self.on_select())
        bf = ttk.Frame(left); bf.pack(fill="x")
        ttk.Button(bf, text="New", command=self.new).pack(side="left", expand=True, fill="x"); ttk.Button(bf, text="Delete", command=self.delete).pack(side="left", expand=True, fill="x")
        form = ttk.LabelFrame(self, text=title); form.pack(side="left", fill="both", expand=True, padx=4, pady=4)
        self.vars, self.combos = {}, {}
        for r, (path, label, choices) in enumerate(fields):
            ttk.Label(form, text=label).grid(row=r, column=0, sticky="e", padx=4, pady=2)
            var = tk.StringVar(); self.vars[path] = var
            if choices is not None: cb = ttk.Combobox(form, textvariable=var, width=40); cb.grid(row=r, column=1, sticky="w"); self.combos[path] = (cb, choices)
            else: ttk.Entry(form, textvariable=var, width=42).grid(row=r, column=1, sticky="w")
        ttk.Button(form, text="Apply", command=self.apply).grid(row=len(fields), column=1, sticky="w", pady=6)
        self.refresh()

    @property
    def items(self): return self.app.project.extra[self.kind]
    def label(self, d): return f"{d.get('id', '')!s:>4} {d.get('id_name', '?')}"
    def refresh(self):
        self.lb.delete(0, "end")
        for d in self.items: self.lb.insert("end", self.label(d))
        for path, (cb, choices) in self.combos.items(): cb["values"] = choices(self.app.project) if callable(choices) else choices
    def on_select(self):
        sel = self.lb.curselection()
        if not sel: return
        self.cur = sel[0]; d = self.items[self.cur]
        for path, var in self.vars.items(): v = _get(d, path, ""); var.set("" if v is None else str(v))
    def new(self):
        name = simpledialog.askstring("New", "id_name:", parent=self)
        if not name: return
        self.items.append(self.new_def(self.app.project, name)); self.app.dirty = True; self.refresh()
        self.lb.selection_set(len(self.items) - 1); self.on_select()
    def delete(self):
        if self.cur is None or self.cur >= len(self.items): return
        self.items.pop(self.cur); self.cur = None; self.app.dirty = True; self.refresh()
    def apply(self):
        if self.cur is None: return
        d = self.items[self.cur]
        for path, var in self.vars.items():
            raw = var.get().strip()
            try: val = json.loads(raw) if raw and (raw[0].isdigit() or raw[0] in "-[{") else raw
            except ValueError: val = raw
            _set(d, path, val if raw != "" else None)
        self.app.dirty = True; self.refresh(); self.lb.selection_set(self.cur)
    def flush(self): pass


def sounds_tab(master, app):
    def new(p, name): return {"id": max([s.get("id", 0) for s in p.extra["sound"]] + [0]) + 1, "id_name": name, "file": f"sounds/{name}.ogg"}
    return ListTab(master, app, "sound", [("id", "id", None), ("id_name", "id_name", None), ("file", "file (ogg / wav, relative to Assets)", None)], new,
                   "Sound (blocks reference id_name in properties.definition.sounds)")


def recipes_tab(master, app):
    items = lambda p: sorted(d["id_name"] for d in p.defs["item"].values())
    def new(p, name): return {"type": "smelting", "id_name": name, "input": {"namespace": p.s["namespace"], "id_name": ""}, "output": {"namespace": p.s["namespace"], "id_name": "", "count": 1}, "time": 10}
    t = ListTab(master, app, "recipe", [("type", "type", ["smelting"]), ("id_name", "id_name", None), ("input.id_name", "input item", items),
                                         ("output.id_name", "output item", items), ("output.count", "output count", None), ("time", "time (seconds)", None)], new,
                "Recipe (smelting; fuel = an item's fuel_time)")
    t.label = lambda d: f"{d.get('type', '?')[:5]} {d.get('id_name', '?')}"
    t.refresh(); return t


class EntitiesTab(ttk.Frame):
    """entity_definitions.txt: the format is reserved by the game; this tab keeps the list as raw JSON."""
    def __init__(self, master, app):
        super().__init__(master); self.app = app
        ttk.Label(self, text="Entity definitions (reserved for players, dropped items, animals... - edit as a JSON list)").pack(anchor="w", padx=6, pady=4)
        self.txt = tk.Text(self, font=("Courier", 10), wrap="none"); self.txt.pack(fill="both", expand=True, padx=6)
        ttk.Button(self, text="Apply JSON", command=self.apply).pack(anchor="w", padx=6, pady=4)
        self.refresh()
    def refresh(self):
        self.txt.delete("1.0", "end"); self.txt.insert("1.0", json.dumps(self.app.project.extra["entity"], indent=2, ensure_ascii=False))
    def apply(self):
        try:
            data = json.loads(self.txt.get("1.0", "end") or "[]")
            if not isinstance(data, list): raise ValueError("expected a JSON list [...]")
        except ValueError as e: return messagebox.showerror("Entities", str(e))
        self.app.project.extra["entity"] = data; self.app.dirty = True
    def flush(self): pass
