namespace CubePlanet;

/// <summary>
/// Automated checks of game rules that need no window (--selftest; exit code 1 on failure), and the --showcase bench helper that places
/// example blocks in front of the bench camera so screenshots show them. Later phases add their tests here (multiblocks, containers, saving).
/// </summary>
public static class SelfTest
{
    static int fails, checks;
    static void Check(bool ok, string what) { checks++; if (!ok) { fails++; Console.WriteLine("FAIL " + what); } }

    public static int Run()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        var (up, fwd, ground) = SpotInFront(6);

        // ---- slab: collision only in the lower half, selection too; placing on top goes into the cell above, not into the slab
        V3 slabAt = ground + up * .5;
        SetAt(slabAt.X, slabAt.Y, slabAt.Z, OakSlab);
        int id = LocalAt(slabAt.X, slabAt.Y, slabAt.Z, out _, out double ly, out _);
        Check(id == OakSlab, $"slab placed (got {BN[id]})");
        V3 lower = ground + up * .25, upper = ground + up * .75;
        Check(Collision.SolidAt(lower.X, lower.Y, lower.Z), "slab: lower half is solid");
        Check(!Collision.SolidAt(upper.X, upper.Y, upper.Z), "slab: upper half is free");
        // look straight down at the slab from 3 blocks above
        V3 eye = ground + up * 3.2; C.x = eye.X; C.y = eye.Y; C.z = eye.Z; C.f = -up; C.u = fwd;
        var hit = BlockEditing.Raycast(6);
        Check(hit != null && hit.id == OakSlab, $"raycast hits the slab (got {(hit == null ? "nothing" : BN[hit.id])})");
        if (hit != null)
        {
            Check(Vec(hit.x, hit.y, hit.z).Sub(ground).Dot(up) < .55, "raycast stops at the slab's top (0.5), not the cell top");
            V3 pc = Vec(hit.px, hit.py, hit.pz); int pid = LocalAt(pc.X, pc.Y, pc.Z, out _, out _, out _);
            Check(pid == 0 && pc.Sub(ground).Dot(up) > 1, "place position is the empty cell above the slab");
        }
        // ---- player dropped onto the slab lands on its top (0.5), not on the cell top (1.0)
        int pb = BlockAccess.P2b(slabAt.X, slabAt.Y, slabAt.Z); int pf = PF, pbx = PX, pby = PY, pbz = PZ; Band PB = bands[pb];
        for (int a = -1; a <= 1; a++) for (int c = -1; c <= 1; c++) { SetCell(pb, pf, pbx + a, pby, pbz + c, OakSlab); for (int k = 1; k <= 3; k++) SetCell(pb, pf, pbx + a, pby + k, pbz + c, 0); }
        V3 centre = Gp(PB, pf, pbz + .5, pbx + .5, PB.inner + pby); // bottom centre of the slab's cell
        P.on = true; P.ready = true; P.fly = false; P.core = false; P.vu = 0; P.vh = new V3(); P.p = centre + up * 2.5; ground = centre;
        for (int i = 0; i < 80; i++) PlayerPhysics.PlayerUpdate(.05, 0, 0, false, false, false);
        double feet = M.Dot(P.p - ground, up);
        Check(Math.Abs(feet - .5) < .06 && P.gnd, $"player stands on the slab top (feet at {feet:0.000}, on ground {P.gnd})");
        P.on = false;
        // ---- flower: no collision, selectable
        V3 fl = ground + up * .5 + fwd * 1; SetAt(fl.X, fl.Y, fl.Z, Poppy);
        Check(!Collision.SolidAt(fl.X, fl.Y, fl.Z), "flower has no collision");
        // ---- water: neither solid nor selectable
        V3 wa = ground + up * .5 + fwd * 2; SetAt(wa.X, wa.Y, wa.Z, Water);
        Check(!Collision.SolidAt(wa.X, wa.Y, wa.Z) && !Selects(Water, .5, .5, .5), "water: no collision, not selectable");
        // ---- stairs: back upper quarter solid, front upper quarter free
        V3 st = ground + up * .5 + fwd * 3; SetAt(st.X, st.Y, st.Z, OakStairs);
        // stairs boxes: the upper half covers z 0..0.5 (turning with the block's facing comes with block state in phase 4)
        Check(Collides(OakStairs, .5, .75, .25) && !Collides(OakStairs, .5, .75, .75), "stairs: upper box only at the back");
        Check(Collides(OakStairs, .5, .25, .75), "stairs: lower half solid");
        // ---- per-face textures and box meshing
        Check(TEXF[OakLog * 6 + 2] != TEXF[OakLog * 6 + 0], "oak log top texture differs from its side");
        Check(TEXF[OakLog * 6 + 3] == TEXF[OakLog * 6 + 2], "oak log bottom = top");
        int sb = BlockAccess.P2b(st.X, st.Y, st.Z);
        var chunk = new Chunk { key = ck(sb, PF, PX >> 5, PY >> 5, PZ >> 5), band = sb, face = PF, cx = PX >> 5, cy = PY >> 5, cz = PZ >> 5 };
        ChunkGenerator.Ensure(chunk);
        var r = Mesher.Build(chunk, out _);
        bool frac = false;
        if (r?.o != null)
            for (int k = 0; k < r.o.vb.Length / 16; k++) { int u = BitConverter.ToUInt16(r.o.vb, k * 16 + 10); int uu = u & 31, vv = (u >> 5) & 31; if (uu % 16 != 0 || vv % 16 != 0) { frac = true; break; } }
        Check(frac, "box faces get fractional texture coordinates");
        EditDirtying();
        Icons();
        CaveWater();
        Phase4(up, fwd);
        SaveReload();
        Console.WriteLine(fails == 0 ? $"SELFTEST OK ({checks} checks)" : $"SELFTEST FAILED: {fails} of {checks} checks");
        return fails == 0 ? 0 : 1;
    }

    // cave water (aquifers): generated level and at rest, the same at unloaded cells (Gen), and the simulation re-meshes in the background
    static void CaveWater()
    {
        V3 c = new(C.x, C.y, C.z); int bi = BlockAccess.P2b(c.X, c.Y, c.Z); int f = PF, ccx = PX >> 5, ccz = PZ >> 5; Band B = bands[bi];
        var (water, loose) = WaterBench.Unsettled(B, bi, f, ccx, ccz, 2);
        Check(water > 1000 && loose == 0, $"generated water is at rest: none beside / above air ({loose} of {water} cells)");
        // a flooded cave chunk: Gen (used for cells of unloaded chunks) gives water exactly where the chunk generator put it
        int cy = (int)Math.Floor((R - 30 - B.inner) / CS); int same = 0, cells = 0;
        for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
            {
                var ch = new Chunk { key = ck(bi, f, ccx + dx, cy, ccz + dz), band = bi, face = f, cx = ccx + dx, cy = cy, cz = ccz + dz }; ChunkGenerator.Ensure(ch);
                for (int i = 0; i < ch.d.Length; i += 7)
                {
                    if (ch.d[i] != Water) continue; cells++;
                    if (Gen(B, f, ch.cx * 32 + (i & 31), cy * 32 + (i >> 10), ch.cz * 32 + ((i >> 5) & 31)) == Water) same++;
                }
            }
        Check(cells > 0 && same == cells, $"cave water at unloaded cells matches the generator ({same} of {cells})");
        // simulation changes: meshed by the worker threads (relight + version), never on the main thread this frame
        var (up, fwd, g) = SpotInFront(11); int b2 = BlockAccess.P2b(g.X, g.Y + 0, g.Z); int x = PX, y = PY, z = PZ;
        var chn = new Chunk { key = ck(b2, PF, x >> 5, y >> 5, z >> 5), band = b2, face = PF, cx = x >> 5, cy = y >> 5, cz = z >> 5 };
        bool made = !chunks.ContainsKey(chn.key); if (made) AddChunk(chn); else chn = chunks[chn.key];
        ChunkGenerator.Ensure(chn); chn.built = true; chn.dirty = chn.relight = false; int v0 = chn.ver;
        SetCell(b2, PF, (x & ~31) + 10, y, (z & ~31) + 10, Water, 5, 0, true);
        Check(!chn.dirty && chn.relight && chn.ver > v0, "water simulation edits re-mesh in the background");
        SetCell(b2, PF, (x & ~31) + 10, y, (z & ~31) + 10, 0); WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        if (made) FreeChunk(chn);
    }

    // item icons: 3D models for cube / box blocks, the block texture for plants, the item atlas picture for the door and plain items
    static void Icons()
    {
        ItemDefinition It(string n) => GameAssets.ItemReg.All.First(i => i.IdName == n);
        Check(ItemIcons.KindOf(It("stone")) == "model" && ItemIcons.KindOf(It("oak_stairs")) == "model" && ItemIcons.KindOf(It("red_bed")) == "model", "cube / box blocks get 3D model icons");
        Check(ItemIcons.KindOf(It("poppy")) == "sprite" && ItemIcons.KindOf(It("vine")) == "sprite", "plants / vines get flat sprite icons");
        Check(ItemIcons.KindOf(It("oak_door")) == "texture" && ItemIcons.KindOf(It("coal")) == "texture", "door and plain items keep their atlas picture");
        Check(ItemIcons.BoxesOf(It("red_bed")).Count == 6, $"bed icon shows both parts ({ItemIcons.BoxesOf(It("red_bed")).Count} boxes)");
        Check(ItemIcons.BoxesOf(It("oak_fence")).Count == 5, "fence icon shows the post with east / west bars (icon_state)");
        var top = GameAssets.IconMips[0]; int S = ItemIcons.Size;
        double Cover(int id) { int n = 0; for (int i = 0; i < S * S; i++) if (top[(id * S * S + i) * 4 + 3] > 127) n++; return n / (double)(S * S); }
        double cs = Cover(It("stone").Id), cl = Cover(It("oak_slab").Id);
        Check(cs > .6 && cs < .85, $"stone icon covers a cube's share of the icon ({cs:0.00})");
        Check(cl > .3 && cl < cs, $"slab icon is smaller than a full block ({cl:0.00})");
        Check(ItemIcons.Smooth[It("stone").Id] == 1 && ItemIcons.Smooth[It("poppy").Id] == 0, "3D icons sampled smoothly, flat ones sharply");
        // the poppy sprite is the block texture scaled up by whole pixels: the texel at (8, 8) of 16 covers an 8x8 square of the icon
        int pid = It("poppy").Id, o0 = (pid * S * S + 64 * S + 64) * 4, o1 = (pid * S * S + 71 * S + 71) * 4;
        Check(top[o0] == top[o1] && top[o0 + 1] == top[o1 + 1] && top[o0 + 3] == top[o1 + 3], "sprite icons keep square pixels");
        try { Gpu.CheckCompiles(Glsl.UI_FS, true, "ui.frag"); Check(true, ""); } catch (Exception e) { Check(false, "ui.frag compiles: " + e.Message); }
    }

    // a block edit re-meshes on the main thread only the chunks whose faces change (its own, a neighbour it touches); chunks merely within
    // skylight reach are re-lit in the background (meshing them all at once froze the game when building fast)
    static void EditDirtying()
    {
        var (_, _, g) = SpotInFront(9); int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x = PX, y = PY, z = PZ;
        var made = new List<Chunk>();
        Chunk Get(int cx, int cy, int cz) { long k = ck(bi, f, cx, cy, cz); if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(c); made.Add(c); } ChunkGenerator.Ensure(c); c.built = true; c.dirty = c.relight = false; return c; }
        int cx = x >> 5, cy = y >> 5, cz = z >> 5;
        var self = Get(cx, cy, cz); var west = Get(cx - 1, cy, cz); var east = Get(cx + 1, cy, cz);
        // 4 blocks from the west border: own chunk meshed now, west chunk only re-lit, east chunk (28 away) untouched
        SetCell(bi, f, cx * 32 + 4, y, z, Stone);
        Check(self.dirty && !west.dirty && west.relight && !east.dirty && !east.relight, $"edit inside a chunk: own chunk re-meshed, near neighbour re-lit only (west dirty {west.dirty} relight {west.relight}, east {east.dirty}/{east.relight})");
        foreach (var c in new[] { self, west, east }) c.dirty = c.relight = false;
        // on the west border: the west chunk shows a new face, so it is re-meshed at once too
        SetCell(bi, f, cx * 32, y, z, Stone);
        Check(self.dirty && west.dirty, "edit on a chunk border re-meshes the touching neighbour at once");
        SetCell(bi, f, cx * 32 + 4, y, z, 0); SetCell(bi, f, cx * 32, y, z, 0);
        foreach (var c in made) FreeChunk(c);
    }

    // saving (PLAN phase 5): save -> forget everything -> load gives identical chunks, states and block entities
    static void SaveReload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cubeplanet_selftest_world");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        List<long> keys; lock (EditLock) keys = ecount.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        (ushort[] d, ushort[] st) Gen(long k)
        {
            var (bf, cx, cy, cz) = WorldSave.DecodeKey(k);
            var c = new Chunk { key = k, band = bf / 6, face = bf % 6, cx = cx, cy = cy, cz = cz }; ChunkGenerator.Ensure(c); return (c.d, c.st);
        }
        var before = keys.ToDictionary(k => k, Gen);
        string Ents() => string.Join("|", BlockStates.entities.Values.OrderBy(e => e.Pos.Key).Select(e =>
            $"{e.BlockId}:{string.Join(",", e.Numbers.OrderBy(n => n.Key).Select(n => $"{n.Key}={n.Value:0.###}"))}:{string.Join(",", e.Inventories.OrderBy(i => i.Key).SelectMany(i => i.Value.Select(s => s == null ? "-" : $"{s.Item}x{s.Count}/{s.State}")))}"));
        string entsBefore = Ents(); int nEnts = BlockStates.entities.Count;
        WorldSave.SaveTo(dir, "selftest");
        Check(File.Exists(Path.Combine(dir, "world.json")) && Directory.GetFiles(Path.Combine(dir, "regions")).Length > 0, $"world saved ({keys.Count} touched chunks, {nEnts} block entities)");
        WorldSave.ResetWorldState();
        Check(BlockStates.entities.Count == 0 && keys.Any(k => !Gen(k).d.SequenceEqual(before[k].d)), "after clearing, the touched chunks generate differently (test is meaningful)");
        WorldSave.LoadInto(dir, restorePlayer: false);
        int same = 0, sameSt = 0;
        foreach (var k in keys)
        {
            var (d, st) = Gen(k);
            if (d.SequenceEqual(before[k].d)) same++;
            if ((st ?? new ushort[32768]).SequenceEqual(before[k].st ?? new ushort[32768])) sameSt++;
        }
        Check(same == keys.Count, $"reloaded block data identical in {same} / {keys.Count} touched chunks");
        Check(sameSt == keys.Count, $"reloaded state words identical in {sameSt} / {keys.Count} touched chunks");
        Check(Ents() == entsBefore, "block entities (chest, furnace, generator contents and numbers) identical after reloading");
        // block lookups in a saved chunk that is not loaded read the saved data (here: the chest, with no player edit left in memory)
        var chestE = BlockStates.entities.Values.FirstOrDefault(e => e.BlockId == Chest);
        int editsLeft; lock (EditLock) editsLeft = edits.Count;
        Check(chestE != null && editsLeft == 0 && chestE.Pos.Id == Chest, "an unloaded saved chunk answers block lookups from the save");
        Directory.Delete(dir, true);
    }

    // block state, multiblocks, behaviours, containers, machines (PLAN phase 4)
    static void Phase4(V3 up, V3 fwd)
    {
        V3 side = M.Nrm(M.Cross(fwd, up));
        BlockPos Spot(double along, double across)
        {   // a free cell on the ground, away from the earlier tests, with 3 free cells above it
            V3 g = Ground(new V3(C.x, C.y, C.z) * 0 + (new V3(C.x, C.y, C.z) - up * 3.2) + fwd * along + side * across + up * 4, up) + up * .5;
            BlockPos.TryAt(g.X, g.Y, g.Z, out var p);
            for (int a = -2; a <= 2; a++) for (int c = -2; c <= 2; c++)
                {   // a clear 5x5 area, 3 high, on a flat stone floor (neighbouring terrain would join fences or block stairs)
                    for (int k = 0; k < 3; k++) if (p.TryOffset(a, k, c, out var q)) q.Set(0);
                    if (p.TryOffset(a, -1, c, out var fl)) fl.Set(Stone);
                }
            return p;
        }
        int Item(string n) => GameAssets.ItemReg.Id(n);
        string StateText(BlockPos p) => DEFS[p.Id]?.State.Describe((uint)p.State) ?? "";

        // ---- door: two halves, opens / closes together, collision follows, breaking one half removes both
        var d0 = Spot(14, 0); var (ax, _, az) = d0.Axes();
        Check(BlockActions.Place(d0, OakDoor, -az, out string why), $"door placed ({why})"); // looking north
        d0.TryOffset(0, 1, 0, out var d1);
        Check(d0.Id == OakDoor && d1.Id == OakDoor, "door: both halves exist");
        Check(StateText(d0).Contains("half=lower") && StateText(d1).Contains("half=upper") && StateText(d0).Contains("facing=north"), $"door states ({StateText(d0)} / {StateText(d1)})");
        Check(Collides(OakDoor, d0.State, .5, .5, .1) && !Collides(OakDoor, d0.State, .5, .5, .6), "closed door: thin box at the north edge");
        BlockActions.Use(d1);
        Check(StateText(d0).Contains("open=true") && StateText(d1).Contains("open=true"), "using the upper half opens both");
        Check(Collides(OakDoor, d0.State, .1, .5, .6) && !Collides(OakDoor, d0.State, .5, .5, .1), "open door: box turned to the side");
        // state survives the chunk being regenerated (states table)
        var regen = new Chunk { key = ck(d0.B, d0.F, d0.X >> 5, d0.Y >> 5, d0.Z >> 5), band = d0.B, face = d0.F, cx = d0.X >> 5, cy = d0.Y >> 5, cz = d0.Z >> 5 };
        ChunkGenerator.Ensure(regen);
        Check(regen.st != null && regen.st[(d0.X & 31) + 32 * ((d0.Z & 31) + 32 * (d0.Y & 31))] == d0.State, "door state is restored when its chunk is generated again");
        BlockActions.Break(d1);
        Check(d0.Id == 0 && d1.Id == 0, "breaking the upper half removes the whole door");
        // a door does not fit under a ceiling
        var d2 = Spot(14, 3); d2.TryOffset(0, 1, 0, out var above); above.Set(Stone);
        Check(!BlockActions.Place(d2, OakDoor, -az, out _) && d2.Id == 0, "door needs two free cells");

        // ---- stairs and bed turn with the placement direction (both face away from the player)
        var s0 = Spot(17, 0);
        BlockActions.Place(s0, OakStairs, ax, out _); // looking east
        Check(StateText(s0).Contains("facing=east"), $"stairs placed looking east face east ({StateText(s0)})");
        Check(Collides(OakStairs, s0.State, .75, .75, .5) && !Collides(OakStairs, s0.State, .25, .75, .5), "turned stairs: the high side is east");
        var b0 = Spot(17, 4);
        Check(BlockActions.Place(b0, RedBed, -az, out _), "bed placed");
        b0.TryOffset(0, 0, -1, out var bh);
        Check(bh.Id == RedBed && StateText(bh).Contains("part=head"), "bed head is the cell beyond the foot");

        // ---- stairs join like Minecraft's and can be placed upside down
        var st1 = Spot(32, 0);
        BlockActions.Place(st1, OakStairs, -az, out _); // high side north
        Check(StateText(st1).Contains("shape=straight") && StateText(st1).Contains("half=bottom"), $"single stairs: straight, bottom ({StateText(st1)})");
        st1.TryOffset(0, 0, -1, out var behind);
        BlockActions.Place(behind, OakStairs, -ax, out _); // behind the high side, facing west
        Check(StateText(st1).Contains("shape=outer_left"), $"stairs facing west behind -> outer corner on the left ({StateText(st1)})");
        var st2 = Spot(32, 4); st2.TryOffset(0, 0, 1, out var front);
        BlockActions.Place(st2, OakStairs, -az, out _); BlockActions.Place(front, OakStairs, ax, out _); // in front, facing east
        Check(StateText(st2).Contains("shape=inner_right"), $"stairs facing east in front -> inner corner on the right ({StateText(st2)})");
        var st3 = Spot(32, 8);
        BlockActions.Place(st3, OakStairs, -az, out _, upperHalf: true);
        Check(StateText(st3).Contains("half=top") && Collides(OakStairs, st3.State, .5, .75, .75) && !Collides(OakStairs, st3.State, .5, .25, .75), "upside-down stairs: full on top, the step hangs below at the back");
        // ---- slabs: bottom or top half from the click; vertical slabs stand against the side you look at
        var sl1 = Spot(38, 0); BlockActions.Place(sl1, OakSlab, -az, out _);
        var sl2 = Spot(38, 3); BlockActions.Place(sl2, OakSlab, -az, out _, upperHalf: true);
        Check(StateText(sl1).Contains("half=bottom") && Collides(OakSlab, sl1.State, .5, .25, .5) && !Collides(OakSlab, sl1.State, .5, .75, .5), "bottom slab fills the lower half");
        Check(StateText(sl2).Contains("half=top") && Collides(OakSlab, sl2.State, .5, .75, .5) && !Collides(OakSlab, sl2.State, .5, .25, .5), "slab placed from an upper-half click fills the upper half");
        var vs = Spot(38, 6); BlockActions.Place(vs, OakVerticalSlab, -az, out _); // looking north
        Check(Collides(OakVerticalSlab, vs.State, .5, .5, .25) && !Collides(OakVerticalSlab, vs.State, .5, .5, .75), $"vertical slab stands in the north half when placed looking north ({StateText(vs)})");
        var vs2 = Spot(38, 9); BlockActions.Place(vs2, OakVerticalSlab, ax, out _); // looking east
        Check(Collides(OakVerticalSlab, vs2.State, .75, .5, .5) && !Collides(OakVerticalSlab, vs2.State, .25, .5, .5), "vertical slab turns with the look direction (east half)");
        // ---- walking up stairs and slabs (step-up), but not up a full block
        double maxLag = 0;
        (double h, double dist) Walk(BlockPos at, double secs, bool jump = false)
        {
            Band WB = bands[at.B]; V3 c0 = Gp(WB, at.F, at.Z + .5, at.X + .5, WB.inner + at.Y), up0 = M.Nrm(c0), fw0 = M.Nrm(-at.Axes().z);
            var cam = (C.x, C.y, C.z, C.f, C.u); // the camera follows the player: put it back so later test spots stay where they were
            P.on = true; P.ready = true; P.fly = false; P.core = false; P.vu = 0; P.vh = new V3(); P.gnd = true; P.p = c0 + up0 * .01;
            C.f = fw0; C.u = up0;
            double top = -9; // highest the feet got (the walk may carry on past the obstacle and drop down behind it)
            maxLag = 0;
            for (double t = 0; t < secs; t += .05) { PlayerPhysics.PlayerUpdate(.05, 1, 0, jump, false, false); top = Math.Max(top, M.Dot(P.p - c0, up0)); maxLag = Math.Max(maxLag, PlayerPhysics.stepLag); }
            V3 d = P.p - c0; P.on = false; (C.x, C.y, C.z, C.f, C.u) = cam; return (top, M.Dot(d, fw0));
        }
        var w1 = Spot(41, 0); w1.TryOffset(0, 0, -1, out var s1a); w1.TryOffset(0, 0, -2, out var s1b);
        BlockActions.Place(s1a, OakSlab, -az, out _); BlockActions.Place(s1b, OakSlab, -az, out _);
        var (h1, d1w) = Walk(w1, .6);
        Check(Math.Abs(h1 - .5) < .06 && d1w > .9, $"walks up onto slabs without jumping (highest {h1:0.00}, moved {d1w:0.00})");
        Check(maxLag > .2, $"the camera glides after a step-up instead of jumping (camera lag up to {maxLag:0.00})");
        for (int i = 0; i < 20; i++) PlayerPhysics.PlayerUpdate(.05, 0, 0, false, false, false);
        Check(PlayerPhysics.stepLag < .01, "the camera catches up with the body");
        var w2 = Spot(41, 4); w2.TryOffset(0, 0, -1, out var s2a); w2.TryOffset(0, 0, -2, out var s2b);
        BlockActions.Place(s2a, OakStairs, -az, out _); s2b.Set(Stone); // stairs rising away from the player, then a block at the top height
        var (h2, d2w) = Walk(w2, 1.0);
        Check(Math.Abs(h2 - 1) < .06 && d2w > 1.5, $"walks up the stairs onto the block behind them (highest {h2:0.00}, moved {d2w:0.00})");
        var w3 = Spot(41, 8); w3.TryOffset(0, 0, -1, out var s3a); s3a.Set(Stone);
        var (h3, d3w) = Walk(w3, .6);
        Check(h3 < .06 && d3w < .5, $"a full block still blocks (highest {h3:0.00}, moved {d3w:0.00})");

        // ---- fences are 1.5 high for collision: jumping does not get over them
        var wf = Spot(41, 12);
        for (int a = -1; a <= 1; a++) if (wf.TryOffset(a, 0, -1, out var fq)) BlockActions.Place(fq, OakFence, -az, out _);
        var (hf, dfw) = Walk(wf, 1.5, jump: true);
        Check(dfw < .8, $"jumping at a fence row does not get over it (moved {dfw:0.00}, highest {hf:0.00})");
        // a lone post: landing on it from above, and walking into it head-on (the hitbox must not slip around a 0.25 wide post)
        var wp = Spot(41, 16); wp.TryOffset(0, 0, -1, out var post); BlockActions.Place(post, OakFence, -az, out _);
        {
            Band PBd = bands[post.B]; V3 pc = Gp(PBd, post.F, post.Z + .5, post.X + .5, PBd.inner + post.Y), pu = M.Nrm(pc);
            for (int a = -1; a <= 1; a++) for (int c = -1; c <= 1; c++) for (int k = 1; k <= 5; k++) if (post.TryOffset(a, k, c, out var air)) air.Set(0); // room for the 1.8 high player
            var cam = (C.x, C.y, C.z, C.f, C.u);
            P.on = true; P.ready = true; P.fly = false; P.core = false; P.vu = 0; P.vh = new V3(); P.p = pc + pu * 2.2; // inside the cleared space above the post
            for (int i = 0; i < 60; i++) PlayerPhysics.PlayerUpdate(.05, 0, 0, false, false, false);
            double feetP = M.Dot(P.p - pc, pu); P.on = false; (C.x, C.y, C.z, C.f, C.u) = cam;
            Check(Math.Abs(feetP - 1.5) < .06 && P.gnd, $"dropped onto a fence post, the player stands on it (feet {feetP:0.00})");
        }
        var (hp, dpw) = Walk(wp, .8);
        Check(dpw < .75, $"walking into a lone fence post is blocked (moved {dpw:0.00})");
        // ---- climbing out of deep water onto the bank (forward + jump), like Minecraft
        var wpool = Spot(44, 0);
        for (int k = 1; k <= 3; k++)
            for (int a = -1; a <= 1; a++) for (int c = -1; c <= 1; c++)
                    if (wpool.TryOffset(a, -k, c, out var q)) { if (a == 0 && c == 0) SetCell(q.B, q.F, q.X, q.Y, q.Z, Water); else if (k > 1) q.Set(Stone); }
        if (wpool.TryOffset(0, -4, 0, out var pfloor)) pfloor.Set(Stone);
        wpool.TryOffset(0, -3, 0, out var deep);
        var (hw2, dw2) = Walk(deep, 4.0, jump: true);
        Check(hw2 > 2.95 && dw2 > .5, $"swims up and climbs out of 3 deep water onto the bank (highest {hw2:0.00} of 3, moved {dw2:0.00})");

        // ---- fences connect to fences and solid blocks
        var fa = Spot(35, 0); BlockActions.Place(fa, OakFence, -az, out _);
        Check(!StateText(fa).Contains("=true"), $"lone fence has no arms ({StateText(fa)})");
        fa.TryOffset(1, 0, 0, out var fb); BlockActions.Place(fb, OakFence, -az, out _);
        Check(StateText(fa).Contains("east=true") && StateText(fb).Contains("west=true"), $"two fences join ({StateText(fa)} / {StateText(fb)})");
        fa.TryOffset(0, 0, -1, out var fwall); BlockActions.Place(fwall, Stone, -az, out _);
        Check(StateText(fa).Contains("north=true"), "fence joins a solid block");
        Check(Collides(OakFence, fa.State, .5, .5, .1) && !Collides(OakFence, fa.State, .1, .5, .9), "fence arm collides, the open side does not");
        BlockActions.Break(fb);
        Check(StateText(fa).Contains("east=false"), "breaking the neighbour removes the arm");
        // ---- bed: solid sides (no see-through holes), mattress + legs
        int sideTex = DEFS[RedBed].Texture("side"); bool holes = false;
        for (int k = 0; k < 16 * 16; k++) if (GameAssets.BlockLayers[(sideTex * 256 + k) * 4 + 3] < 255) holes = true;
        Check(!holes && Variant(RedBed, 0).Boxes.Length == 3, "bed: opaque side texture, mattress and two legs per part");

        // ---- chest: block entity with 27 slots; move items with the real slot clicks
        var c0 = Spot(20, 0); BlockActions.Place(c0, Chest, -az, out _);
        var ce = BlockStates.Entity(c0);
        Check(ce != null && ce.Inv("slots")?.Length == 27, "chest has a 27-slot inventory");
        PlayerInventory.Init(); PlayerInventory.Hotbar[0] = new ItemStack(Item("stone"), 40);
        Check(BlockActions.Use(c0) && UI.invOpen, "right click opens the chest window");
        UI.TestClick("hotbar", 0, true); UI.TestClick("slots", 4, true);
        Check(ce.Inv("slots")[4]?.Count == 40 && PlayerInventory.Hotbar[0] == null, "picked up 40 stone and put them into chest slot 5");
        UI.TestClick("slots", 4, false); UI.TestClick("slots", 5, false);
        Check(ce.Inv("slots")[4].Count == 20 && ce.Inv("slots")[5].Count == 1 && UI.cursor?.Count == 19, "right click takes half, right click puts one");
        UI.TestClick("slots", 4, true);
        Check(ce.Inv("slots")[4].Count == 39 && UI.cursor == null, "left click merges the stacks");
        UI.TestClick("slots", 4, true, shiftKey: true);
        Check(ce.Inv("slots")[4] == null && PlayerInventory.Hotbar.Concat(PlayerInventory.Main).Any(s => s != null && s.Item == Item("stone") && s.Count == 39), "shift click moves the stack to the player");
        UI.CloseScreen();

        // ---- furnace: smelts 2 iron ore with one coal, lit while burning
        var f0 = Spot(23, 0); BlockActions.Place(f0, Furnace, -az, out _);
        var fe = BlockStates.Entity(f0);
        int frontTex = DEFS[Furnace].Texture("north");
        Check(StateText(f0).Contains("facing=south") && Variant(Furnace, f0.State).Faces[4] == frontTex, $"furnace placed looking north shows its front to the south, towards the player ({StateText(f0)})");
        fe.Inv("input")[0] = new ItemStack(Item("iron_ore"), 2); fe.Inv("fuel")[0] = new ItemStack(Item("coal"), 1);
        for (int i = 0; i < 20; i++) BlockActions.Tick(.1);
        Check(StateText(f0).Contains("lit=true"), "furnace is lit while it burns");
        for (int i = 0; i < 200; i++) BlockActions.Tick(.1);
        Check(fe.Inv("output")[0]?.Item == Item("iron_ingot") && fe.Inv("output")[0].Count == 2 && fe.Inv("input")[0] == null, $"furnace smelted 2 iron ingots ({fe.Inv("output")[0]})");

        // ---- oxygen generator: coal + a full bucket -> an oxygen canister
        var o0 = Spot(26, 0); BlockActions.Place(o0, OxygenGenerator, -az, out _);
        var oe = BlockStates.Entity(o0); var bucketDef = GameAssets.ItemReg.Get(Item("bucket"));
        oe.Inv("fuel")[0] = new ItemStack(Item("coal"), 1); oe.Inv("water_in")[0] = new ItemStack(bucketDef.Id, 1, bucketDef.State.Find("fill").With(0, 8));
        for (int i = 0; i < 150; i++) BlockActions.Tick(.1);
        Check(oe.Inv("output")[0]?.Item == Item("oxygen_canister"), $"oxygen generator filled a canister (energy {oe.Num("energy"):0}, water {oe.Num("water"):0.0}, oxygen {oe.Num("oxygen"):0})");
        Check(bucketDef.State.Find("fill").Value(oe.Inv("water_in")[0].State) == 0, "the bucket was emptied into the tank");

        // ---- water level is the water block's state
        var w0 = Spot(29, 0); SetCell(w0.B, w0.F, w0.X, w0.Y, w0.Z, Water, 3);
        Check(WaterSim.LevelOf(w0.State) == 3, $"water level 3 is stored as state ({StateText(w0)})");
        SetCell(w0.B, w0.F, w0.X, w0.Y, w0.Z, Water, 8);
        Check(w0.State == 0, "a full water cell has no state word");
    }

    /// <summary>Bench: a row of example blocks on the ground in front of the camera (slabs, stairs, fence posts, logs).</summary>
    public static void Showcase()
    {
        var (up, fwd, ground) = SpotInFront(7);
        V3 side = M.Nrm(M.Cross(fwd, up));
        int[] row = { OakSlab, StoneSlab, OakStairs, StoneBrickStairs, OakFence, OakDoor, OakDoor, RedBed, Chest, Furnace, OxygenGenerator, OakLog, BirchLog, CherryLog };
        for (int i = 0; i < row.Length; i++)
        {
            V3 p = ground + side * ((i - row.Length / 2) * 1.25) + up * .5;
            p = Ground(p + up * 4, up) + up * .5;
            if (!BlockPos.TryAt(p.X, p.Y, p.Z, out var at)) continue;
            BlockActions.Place(at, row[i], i == 2 ? side : fwd, out _); // fronts face the camera; the first stairs turned sideways
            if (row[i] == OakLog || row[i] == BirchLog) { V3 q = p + up; SetAt(q.X, q.Y, q.Z, row[i]); }
            if (row[i] == OakDoor && i == 6) BlockActions.Use(at); // the second door open
            showcase[row[i]] = at;
        }
        // something to look at in the machines
        if (showcase.TryGetValue(Furnace, out var fp) && BlockStates.Entity(fp) is { } fe)
        { fe.Inv("input")[0] = new ItemStack(GameAssets.ItemReg.Id("iron_ore"), 12); fe.Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 8); }
        if (showcase.TryGetValue(Chest, out var cp) && BlockStates.Entity(cp) is { } ce)
            for (int i = 0; i < 9; i++) ce.Inv("slots")[i * 2] = new ItemStack(GameAssets.ItemReg.All[(i * 13) % GameAssets.ItemReg.All.Count].Id, 1 + i * 7);
        if (showcase.TryGetValue(OxygenGenerator, out var op) && BlockStates.Entity(op) is { } oe)
        { oe.Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 3); var bd = GameAssets.ItemReg.Get(GameAssets.ItemReg.Id("bucket")); oe.Inv("water_in")[0] = new ItemStack(bd.Id, 1, bd.State.Find("fill").With(0, 8)); }
    }
    static readonly Dictionary<int, BlockPos> showcase = new();
    /// <summary>Bench --showui: opens a window over the showcase (inventory, chest, furnace, oxygen_generator).</summary>
    public static void ShowUi(string name)
    {
        if (name == "inventory") { UI.OpenInventory(); return; }
        int id = name switch { "chest" => Chest, "furnace" => Furnace, _ => OxygenGenerator };
        if (showcase.TryGetValue(id, out var p)) BlockActions.Use(p);
    }

    static (V3 up, V3 fwd, V3 ground) SpotInFront(double dist)
    {
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c);
        V3 fwd = C.f - up * M.Dot(C.f, up); if (fwd.Len < 1e-6) fwd = C.u - up * M.Dot(C.u, up); fwd = M.Nrm(fwd);
        return (up, fwd, Ground(c + fwd * dist, up));
    }
    /// <summary>Walks down from p until the cell below is solid; returns the bottom of the first free cell (on the ground).</summary>
    static V3 Ground(V3 p, V3 up)
    {
        for (int i = 0; i < 400; i++)
        {
            V3 below = p - up * .5; int b = GetAt(below.X, below.Y, below.Z);
            if (b != 0 && b != Water && SPC[b] == 0) { BlockAccess.P2b(p.X, p.Y, p.Z); return p - up * PFY; }
            p = p - up * .5;
        }
        return p;
    }
    static V3 Vec(double x, double y, double z) => new(x, y, z);
    static V3 Sub(this V3 a, V3 b) => a - b;
    static double Dot(this V3 a, V3 b) => M.Dot(a, b);
}
