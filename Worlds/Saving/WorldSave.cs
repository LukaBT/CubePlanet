using System.Text.Json;
using System.Text.Json.Nodes;

namespace CubePlanet;

/// <summary>
/// Worlds on disk: %APPDATA%/CubePlanet/worlds/&lt;name&gt;/world.json (name, seed, radius, version, player, camera, time, inventory, and the
/// block / item ID tables the save was written with) + regions/*.bin (<see cref="RegionFile"/>) + core.bin (Cartesian core edits).
/// Every touched chunk (player edits, water flow, state changes, block entities) is saved whole; untouched chunks are regenerated from the seed.
/// Loaded chunk records are used as they are instead of generating those chunks (so a later change to the generator does not alter built
/// things). IDs are remapped by name when the asset files changed since the save. Autosave every minute and when the game closes.
/// </summary>
public static class WorldSave
{
    public const int Version = 1;
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CubePlanet", "worlds");
    /// <summary>Folder of the world being played (null: nothing is saved, e.g. bench runs).</summary>
    public static string Current; public static string CurrentName;

    /// <summary>Saved chunk data by chunk key (guarded by EditLock: chunk workers read it while generating).</summary>
    static readonly Dictionary<long, ushort[]> saved = new();
    public static int SavedCount { get { lock (EditLock) return saved.Count; } }
    public static bool TryGetSaved(long key, out ushort[] d) { lock (EditLock) return saved.TryGetValue(key, out d); }

    public sealed class Info { public string Name, Folder; public double Radius; public int Seed; public DateTime LastPlayed; }

    // ---------- world list ----------
    public static List<Info> List()
    {
        var l = new List<Info>();
        if (!Directory.Exists(Root)) return l;
        foreach (var dir in Directory.GetDirectories(Root))
        {
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "world.json")));
                l.Add(new Info { Folder = dir, Name = (string)j["name"] ?? Path.GetFileName(dir), Radius = (double)j["radius"], Seed = (int)j["seed"], LastPlayed = File.GetLastWriteTime(Path.Combine(dir, "world.json")) });
            }
            catch { } // not a world folder / damaged: not listed
        }
        return l.OrderByDescending(i => i.LastPlayed).ToList();
    }
    public static string FolderFor(string name)
    {
        var safe = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "world";
        return Path.Combine(Root, safe);
    }
    public static void Delete(Info w) { if (w.Folder.StartsWith(Root) && Directory.Exists(w.Folder)) Directory.Delete(w.Folder, true); }
    public static void WriteAtomic(string path, byte[] data) { string tmp = path + ".tmp"; File.WriteAllBytes(tmp, data); File.Move(tmp, path, true); }

    // ---------- save ----------
    // A save is a snapshot taken on the main thread (copies of the touched chunks, entity bytes, core.bin, world.json: a few ms) and the slow
    // part (generating unloaded touched chunks, compressing, writing files) run by Write. Autosave runs Write on a background thread, so the game
    // does not freeze for ~70 ms per 100+ edited chunks every minute; SAVE NOW, closing the game and the self-test write right away.
    static double autosaveT;
    static Task pending;
    public static void Tick(double dt)
    {
        if (Current == null) return;
        if (pending is { IsCompleted: true })
        {
            if (pending.IsFaulted) UI.Note("Saving failed: " + pending.Exception?.GetBaseException().Message, 6); else UI.Note("World saved", 1.5);
            pending = null;
        }
        autosaveT += dt; if (autosaveT < 60 || pending != null) return; autosaveT = 0;
        try { var snap = Snapshot(Current, CurrentName); pending = Task.Run(() => Write(snap)); } catch (Exception e) { UI.Note("Saving failed: " + e.Message, 6); }
    }
    /// <summary>Waits for a running autosave (before saving again, loading or quitting).</summary>
    public static void WaitPending() { var t = pending; pending = null; try { t?.Wait(); } catch { } }
    public static void Save() { if (Current != null) SaveTo(Current, CurrentName); }

    public static void SaveTo(string folder, string name) { WaitPending(); Write(Snapshot(folder, name)); }

    sealed class SaveSnap { public string Folder; public List<RegionFile.Record> Recs = new(); public byte[] Core, Json; }

    static SaveSnap Snapshot(string folder, string name)
    {
        var snap = new SaveSnap { Folder = folder };
        // touched band chunks: edited (ecount > 0) or loaded from the save
        var keys = new HashSet<long>();
        lock (EditLock) { foreach (var kv in ecount) if (kv.Value > 0) keys.Add(kv.Key); foreach (var k in saved.Keys) keys.Add(k); }
        var ents = BlockStates.entities.Values.GroupBy(e => e.Pos.Core ? long.MinValue : ck(e.Pos.B, e.Pos.F, e.Pos.X >> 5, e.Pos.Y >> 5, e.Pos.Z >> 5)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (long k in keys)
        {
            var (bf, cx, cy, cz) = DecodeKey(k); int b = bf / 6, f = bf % 6;
            if (b >= bands.Count) continue;
            var rec = new RegionFile.Record { Band = b, Face = f, Cx = cx, Cy = cy, Cz = cz };
            // loaded chunks are copied now; the others are generated (edits and saved data applied) by Write
            if (chunks.TryGetValue(k, out var ch) && ch.d != null && !ch.core) { rec.D = (ushort[])ch.d.Clone(); var st = ch.st; rec.St = st != null && st.AsSpan().IndexOfAnyExcept((ushort)0) >= 0 ? (ushort[])st.Clone() : null; }
            if (ents.TryGetValue(k, out var el))
            {
                using var ms = new MemoryStream(); using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) foreach (var e in el) WriteEntity(w, e);
                rec.EntityBytes = ms.ToArray(); rec.EntityCount = el.Count;
            }
            snap.Recs.Add(rec);
        }
        // the Cartesian core: its edits are few, kept as a list
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(CoreGrid.coreEd.Count); foreach (var kv in CoreGrid.coreEd) { w.Write(kv.Key); w.Write(kv.Value); }
            List<KeyValuePair<long, ushort>> cst; lock (EditLock) cst = BlockStates.states.Where(kv => IsCoreKey(kv.Key)).ToList();
            w.Write(cst.Count); foreach (var kv in cst) { w.Write(kv.Key); w.Write(kv.Value); }
            var ce = ents.TryGetValue(long.MinValue, out var cel) ? cel : new List<BlockEntity>();
            w.Write(ce.Count); foreach (var e in ce) WriteEntity(w, e);
            w.Flush(); snap.Core = ms.ToArray();
        }
        snap.Json = System.Text.Encoding.UTF8.GetBytes(WorldJson(name).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return snap;
    }

    static void Write(SaveSnap snap)
    {
        Directory.CreateDirectory(Path.Combine(snap.Folder, "regions"));
        var regions = new Dictionary<string, List<RegionFile.Record>>();
        foreach (var rec in snap.Recs)
        {
            if (rec.D == null)
            {
                var tmp = new Chunk { key = ck(rec.Band, rec.Face, rec.Cx, rec.Cy, rec.Cz), band = rec.Band, face = rec.Face, cx = rec.Cx, cy = rec.Cy, cz = rec.Cz };
                ChunkGenerator.Ensure(tmp); rec.D = tmp.d; rec.St = tmp.st != null && tmp.st.Any(v => v != 0) ? tmp.st : null;
            }
            string rn = RegionFile.NameOf(rec.Band, rec.Face, rec.Cx, rec.Cy, rec.Cz);
            if (!regions.TryGetValue(rn, out var rl)) regions[rn] = rl = new();
            rl.Add(rec);
        }
        foreach (var (rn, recs) in regions) RegionFile.Write(Path.Combine(snap.Folder, "regions", rn), recs, WriteEntity);
        WriteAtomic(Path.Combine(snap.Folder, "core.bin"), snap.Core);
        WriteAtomic(Path.Combine(snap.Folder, "world.json"), snap.Json); // last: a world.json on disk means the rest is there
    }
    static bool IsCoreKey(long k) => k >= long.MinValue + 1 && k < long.MinValue + 1 + (1L << 24);

    static JsonObject WorldJson(string name)
    {
        JsonArray V(V3 v) => new(v.X, v.Y, v.Z);
        JsonArray Stacks(ItemStack[] a) { var r = new JsonArray(); foreach (var s in a) r.Add(s == null || s.IsEmpty ? null : new JsonObject { ["item"] = s.Def?.FullName ?? "?", ["count"] = s.Count, ["state"] = s.State }); return r; }
        var blocks = new JsonObject(); foreach (var b in GameAssets.BlockReg.All) blocks[b.FullName] = b.Id;
        var items = new JsonObject(); foreach (var i in GameAssets.ItemReg.All) items[i.FullName] = i.Id;
        return new JsonObject
        {
            ["name"] = name, ["version"] = Version, ["seed"] = SEEDV, ["radius"] = R, ["saved"] = DateTime.Now.ToString("s"),
            ["camera"] = new JsonObject { ["pos"] = new JsonArray(C.x, C.y, C.z), ["forward"] = V(C.f), ["up"] = V(C.u), ["orbit"] = C.orbit },
            ["player"] = new JsonObject { ["on"] = P.on, ["fly"] = P.fly, ["pos"] = V(P.p), ["selected"] = PlayerInventory.Selected },
            ["sun_angle"] = SUNA,
            ["water_reservoir"] = WaterSim.Reservoir, // water cycle: units in the sea / air (springs draw from it)
            ["wind_clock"] = Kinetics.Clock, // the wind: where it is in its slow changes
            ["wind_set"] = Kinetics.IsNatural ? null : new JsonObject { ["speed"] = Kinetics.WindSpeed, ["from"] = Kinetics.FromBearing }, // wind set in the settings (null = natural)
            ["inventory"] = new JsonObject { ["hotbar"] = Stacks(PlayerInventory.Hotbar), ["main"] = Stacks(PlayerInventory.Main) },
            ["block_ids"] = blocks, ["item_ids"] = items,
        };
    }

    // ---------- block entities ----------
    static void WriteEntity(BinaryWriter w, BlockEntity e)
    {
        var p = e.Pos; w.Write(p.Core); w.Write(p.B); w.Write(p.F); w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(e.BlockId);
        w.Write(e.Numbers.Count); foreach (var (k, v) in e.Numbers) { w.Write(k); w.Write(v); }
        w.Write(e.Inventories.Count);
        foreach (var (k, inv) in e.Inventories)
        {
            w.Write(k); w.Write(inv.Length);
            foreach (var s in inv) { bool has = s != null && !s.IsEmpty; w.Write(has ? s.Item : 0); if (has) { w.Write(s.Count); w.Write(s.State); } }
        }
    }
    static int[] blockMap, itemMap; // saved ID -> current ID while loading
    static BlockEntity ReadEntity(BinaryReader r, BlockPos _)
    {
        var p = new BlockPos(r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        int id = MapBlock(r.ReadInt32());
        var d = DEFS[id]; var e = d != null ? new BlockEntity(p, id, d.State) : null;
        int nn = r.ReadInt32(); for (int i = 0; i < nn; i++) { string k = r.ReadString(); double v = r.ReadDouble(); e?.SetNum(k, v); }
        int ni = r.ReadInt32();
        for (int i = 0; i < ni; i++)
        {
            string k = r.ReadString(); int n = r.ReadInt32(); var inv = e?.Inv(k);
            for (int s = 0; s < n; s++)
            {
                int item = r.ReadInt32(); if (item == 0) continue;
                int count = r.ReadInt32(); uint st = r.ReadUInt32(); item = MapItem(item);
                if (inv != null && s < inv.Length && item != 0) inv[s] = new ItemStack(item, count, st);
            }
        }
        return e;
    }
    static int MapBlock(int id) => blockMap == null ? id : id >= 0 && id < blockMap.Length ? blockMap[id] : 0;
    static int MapItem(int id) => itemMap == null ? id : id >= 0 && id < itemMap.Length ? itemMap[id] : 0;
    static int[] BuildMap(JsonObject saved, Func<string, int> current)
    {
        if (saved == null) return null;
        int max = saved.Select(kv => (int)kv.Value).DefaultIfEmpty(0).Max(); var m = new int[max + 1];
        foreach (var (name, v) in saved) m[(int)v] = current(name);
        return m;
    }

    // ---------- load (after World.InitWorld with the saved radius and seed) ----------
    public static JsonNode ReadInfo(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")));

    public static void LoadInto(string folder, bool restorePlayer = true)
    {
        WaitPending();
        var j = ReadInfo(folder);
        blockMap = BuildMap(j["block_ids"] as JsonObject, n => GameAssets.BlockReg.Has(n) ? GameAssets.BlockReg.Id(n) : 0);
        itemMap = BuildMap(j["item_ids"] as JsonObject, n => GameAssets.ItemReg.Has(n) ? GameAssets.ItemReg.Id(n) : 0);
        try
        {
            string rd = Path.Combine(folder, "regions");
            if (Directory.Exists(rd))
                foreach (var file in Directory.GetFiles(rd, "r.*.bin"))
                    foreach (var rec in RegionFile.Read(file, ReadEntity))
                    {
                        if (rec.Band >= bands.Count) continue;
                        for (int i = 0; i < rec.D.Length; i++) rec.D[i] = (ushort)MapBlock(rec.D[i]);
                        long key = ck(rec.Band, rec.Face, rec.Cx, rec.Cy, rec.Cz);
                        lock (EditLock)
                        {
                            saved[key] = rec.D; ecount.TryAdd(key, 0); // ecount: the loader creates the chunk even where it would skip deep stone
                            if (rec.St != null)
                                for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
                                            { ushort s = rec.St[x + 32 * (z + 32 * y)]; if (s != 0) BlockStates.states[ekOf(rec.Band, rec.Face, rec.Cx * 32 + x, rec.Cy * 32 + y, rec.Cz * 32 + z)] = s; }
                        }
                        foreach (var e in rec.Entities) BlockStates.entities[e.Pos.Key] = e;
                        if (chunks.TryGetValue(key, out var ch)) { ch.d = null; ch.st = null; ch.gst = 0; ch.dirty = true; } // regenerate from the save
                    }
            string core = Path.Combine(folder, "core.bin");
            if (File.Exists(core))
                using (var r = new BinaryReader(File.OpenRead(core)))
                {
                    int n = r.ReadInt32(); for (int i = 0; i < n; i++) { int k = r.ReadInt32(); CoreGrid.coreEd[k] = (ushort)MapBlock(r.ReadUInt16()); }
                    n = r.ReadInt32(); lock (EditLock) for (int i = 0; i < n; i++) { long k = r.ReadInt64(); BlockStates.states[k] = r.ReadUInt16(); }
                    n = r.ReadInt32(); for (int i = 0; i < n; i++) { var e = ReadEntity(r, default); if (e != null) BlockStates.entities[e.Pos.Key] = e; }
                }
        }
        finally { blockMap = itemMap = null; }
        // world state (not the player): the water cycle reservoir and the wind
        WaterSim.Reservoir = j["water_reservoir"] is JsonNode wr ? (double)wr : WaterSim.DefaultReservoir; Kinetics.Clock = j["wind_clock"] is JsonNode wc ? (double)wc : 0;
        if (j["wind_set"] is JsonObject ws) Kinetics.SetWind((double)ws["speed"], (double)ws["from"]); else Kinetics.Natural();
        if (!restorePlayer) return;
        // player, camera, time, inventory
        itemMap = BuildMap(j["item_ids"] as JsonObject, n => GameAssets.ItemReg.Has(n) ? GameAssets.ItemReg.Id(n) : 0);
        V3 Vec(JsonNode a) => new((double)a[0], (double)a[1], (double)a[2]);
        var cam = j["camera"]; var cp = Vec(cam["pos"]); C.x = cp.X; C.y = cp.Y; C.z = cp.Z; C.f = Vec(cam["forward"]); C.u = Vec(cam["up"]);
        var pl = j["player"]; P.on = (bool)pl["on"]; P.fly = (bool)pl["fly"]; P.p = Vec(pl["pos"]); PlayerInventory.Selected = Math.Clamp((int)pl["selected"], 0, 9);
        P.vh = new V3(); P.vu = 0; if (P.on) SyncCam();
        SUNA = (double)j["sun_angle"]; SunVec();
        void Stacks(JsonNode a, ItemStack[] into)
        {
            if (a is not JsonArray arr) return;
            for (int i = 0; i < into.Length && i < arr.Count; i++)
            {
                into[i] = null; var s = arr[i]; if (s == null) continue;
                string n = (string)s["item"]; if (!GameAssets.ItemReg.Has(n)) continue;
                into[i] = new ItemStack(GameAssets.ItemReg.Id(n), (int)s["count"], (uint)s["state"]);
            }
        }
        Stacks(j["inventory"]?["hotbar"], PlayerInventory.Hotbar); Stacks(j["inventory"]?["main"], PlayerInventory.Main);
        itemMap = null;
    }

    /// <summary>Forgets every edit, state, block entity and saved chunk (self-test: save, clear, load).</summary>
    public static void ResetWorldState()
    {
        WaitPending();
        lock (EditLock) { edits.Clear(); ecount.Clear(); BlockStates.states.Clear(); saved.Clear(); }
        BlockStates.entities.Clear(); CoreGrid.coreEd.Clear(); WaterFlow.Clear(); WaterSim.Reservoir = WaterSim.DefaultReservoir; Kinetics.Clock = 0; Kinetics.Natural();
        foreach (var ch in chunks.Values) { ch.d = null; ch.st = null; ch.gst = 0; }
    }

    public static (int bf, int cx, int cy, int cz) DecodeKey(long k)
    {
        long cz = k % 524288; k /= 524288; long cy = k % 262144; k /= 262144; long cx = k % 524288; k /= 524288;
        return ((int)k, (int)cx, (int)cy, (int)cz);
    }
}
