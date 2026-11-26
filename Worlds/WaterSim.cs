namespace CubePlanet;

// Every liquid block (placed or natural: water, lava, oil, liquid oxygen; model "liquid" with a "level" state) is 8 units, never created or
// destroyed by the simulation (except where liquids react: lava meeting water turns to stone / obsidian and boils a little water off into the
// water cycle; liquid oxygen open to the air boils away). The level is the block's "level" state (1..8, default 8): only disturbed cells have
// a state word, so oceans & lakes cost nothing until disturbed, then flow like placed liquid. Rules per tick: fall; spread sideways to equalise
// (plus slope relaxation: a 1-unit step that continues downhill is smoothed); pressure: for liquid under a ceiling or a column, a bounded
// flood-fill finds the highest and lowest surface of the connected body and moves one unit between them (U-tubes, tunnels, communicating
// pools). Different liquids do not mix: a heavier one sinks under a lighter one (water under oil); a thick one (lava) moves only every few
// ticks. The mesher blends neighbouring heights so levels look smooth.
/// <summary>Finite liquid simulation and the bucket.</summary>
public static class WaterSim
{
    static StateSchema.Field levelField;
    static StateSchema.Field LevelField => levelField ??= DEFS[Water].State.Find("level") ?? throw new InvalidDataException("the water block needs an instance_state field \"level\" (integer 1..8, default 8)");
    /// <summary>State word of a liquid cell holding lvl units (1..8). Every liquid block has the same "level" field as water.</summary>
    public static int LevelState(int lvl) => (int)LevelField.With(0, Math.Clamp(lvl, 1, 8) - LevelField.Min);
    /// <summary>Liquid level stored in a state word.</summary>
    public static int LevelOf(int state) => LevelField.Value((uint)state);
    internal static readonly Dictionary<long, (int b, int f, int x, int y, int z)> WAct = new();
    internal static readonly Queue<long> wactQ = new();
    internal static readonly int[][] N4 = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
    internal static readonly Random wrng = new();
    internal static double wAcc;
    static long tickNo;
    /// <summary>Cells to look at again next tick (thick liquids waiting, liquid oxygen boiling).</summary>
    static readonly List<(int b, int f, int x, int y, int z)> nextTick = new();

    internal static bool Wfree(int id) => id == 0 || (SPC[id] != 0 && ATT[id] == 0 && !UWP[id]);
    internal static int Wl(WC c) => LIQ[c.id] ? (BlockStates.TryGet(c.k, out ushort s) ? LevelOf(s) : 8) : 0;
    // ---------- water cycle: total water = water in the world + Reservoir, never created or destroyed ----------
    /// <summary>Units "in the sea and the air": springs draw from it, the sea surface takes water arriving on top of it back into it.
    /// It starts at roughly the volume of a planet's oceans, so the sea never visibly changes level.</summary>
    public static double Reservoir = DefaultReservoir;
    public const double DefaultReservoir = 1e9;
    /// <summary>The row just above sea level (water resting there on the sea flows into it), outside lakes (a lake near sea level is not
    /// the sea).</summary>
    public static bool AboveSea(int b, int f, int x, int y, int z)
    {
        double rel = bands[b].inner + y + .5 - R; if (!(rel > 0 && rel < 1)) return false;
        return float.IsNaN(ColsOf(bands[b], f, x >> 5, z >> 5).rs[(x & 31) + 32 * (z & 31)]);
    }
    /// <summary>Adds up to 'units' of a liquid to a cell (air or the same liquid; levels above 8 rise into the cells above). Returns units placed.</summary>
    public static int AddWater(WC c, int units) => AddLiquid(c, units, LIQ[c.id] ? c.id : Water);
    public static int AddLiquid(WC c, int units, int liquid)
    {
        int placed = 0;
        for (int i = 0; i < 16 && units > 0 && c.ok; i++)
        {
            if (c.id != liquid && !Wfree(c.id)) break;
            int l = c.id == liquid ? Wl(c) : 0, t = Math.Min(8, l + units);
            if (t > l) { Wput(c, t, liquid); units -= t - l; placed += t - l; }
            c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z);
        }
        return placed;
    }
    /// <summary>Takes up to 'units' from a liquid cell (and the same liquid above it). Returns units taken.</summary>
    public static int TakeWater(WC c, int units)
    {
        int taken = 0, liq = c.id; if (!LIQ[liq]) return 0; var top = c;
        for (int i = 0; i < 64; i++) { var u = Wcell(top.b, top.F, top.X, top.Y + 1, top.Z); if (!u.ok || u.id != liq) break; top = u; }
        // take from the top of the column down (the surface sinks)
        for (int i = 0; i < 64 && units > 0 && top.ok && top.id == liq; i++)
        {
            int l = Wl(top), m = Math.Min(l, units);
            Wput(top, l - m, liq); units -= m; taken += m;
            if (top.k == c.k) break;
            top = Wcell(top.b, top.F, top.X, top.Y - 1, top.Z);
        }
        return taken;
    }
    /// <summary>Surface height (in blocks along the band's rows) of the liquid column at c, or the cell's floor for air.</summary>
    public static double Surface(WC c)
    {
        if (!c.ok) return double.NaN;
        if (!LIQ[c.id]) return c.Y;
        var top = c;
        for (int i = 0; i < 64; i++) { var u = Wcell(top.b, top.F, top.X, top.Y + 1, top.Z); if (!u.ok || u.id != c.id) break; top = u; }
        return top.Y + Wl(top) / 8.0;
    }

    /// <summary>Cell changes made by the simulation (benchmark counter).</summary>
    public static long Moves;
    // the simulation's changes are meshed by the worker threads (background), not on the main thread in the frame they happen
    internal static void Wput(WC c, int l) => Wput(c, l, LIQ[c.id] ? c.id : Water);
    internal static void Wput(WC c, int l, int liquid) { Moves++; SetCell(c.b, c.F, c.X, c.Y, c.Z, l > 0 ? liquid : 0, l, 0, true); }

    internal static WC Wcell(int bi, int f, int x, int y, int z)
    {
        if (!Norm(bi, f, x, y, z)) return default;
        int b = NB, F = NF, X = NX, Y = NY, Z = NZ; long k = ek(); int id;
        // liquid only moves in loaded chunks (like Minecraft): an unloaded cell acts as a wall. Generating terrain for single cells there
        // made one pressure search take ~20 ms when a body of water reached past the loaded area.
        if (chunks.TryGetValue(ck(b, F, X >> 5, Y >> 5, Z >> 5), out var ch) && ch.d != null) id = ch.d[(X & 31) + 32 * ((Z & 31) + 32 * (Y & 31))];
        else return default;
        return new WC { ok = true, k = k, b = b, F = F, X = X, Y = Y, Z = Z, id = id };
    }
    internal static void Wake(int b, int f, int x, int y, int z)
    {
        for (int d = -1; d < 6; d++)
        {
            var c = d < 0 ? Wcell(b, f, x, y, z) : Wcell(b, f, x + DX[d], y + DY[d], z + DZ[d]);
            if (c.ok && LIQ[c.id] && !WAct.ContainsKey(c.k)) { WAct[c.k] = (c.b, c.F, c.X, c.Y, c.Z); wactQ.Enqueue(c.k); }
        }
    }

    /// <summary>A placed liquid block adds 8 units: it tops up this cell and whatever doesn't fit rises into the cells above.</summary>
    public static void PlaceWater(double x, double y, double z, int amt = 8) => PlaceLiquid(x, y, z, amt, Water);
    public static void PlaceLiquid(double x, double y, double z, int amt, int liquid)
    {
        if (x * x + y * y + z * z < CORE * CORE) { CoreSet(x, y, z, liquid); return; }
        int bi = P2b(x, y, z); if (bi < 0) return;
        var c = Wcell(bi, PF, PX, PY, PZ); if (!c.ok) return;
        int add = amt > 0 ? amt : 8;
        for (int i = 0; i < 200 && add > 0 && c.ok; i++)
        {
            if (c.id != liquid && !Wfree(c.id)) break;
            int l = c.id == liquid ? Wl(c) : 0, t = Math.Min(8, l + add);
            if (t > l) { SetCell(c.b, c.F, c.X, c.Y, c.Z, liquid, t); add -= t - l; }
            c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z);
        }
    }

    // pressure: find the highest and lowest surface of the connected body of one liquid (bounded) and move one unit from high to low
    static readonly HashSet<long> pSeen = new(); static readonly List<WC> pQ = new();
    internal static void Wpress(WC st)
    {
        var seen = pSeen; var q = pQ; seen.Clear(); q.Clear(); seen.Add(st.k); q.Add(st); int L = st.id; // reused: no garbage per search (GC pauses)
        WC hi = default, lo = default, loUp = default; int hiS = 0, hiLv = 0, loS = 0, loLv = 0; bool hasHi = false, hasLo = false;
        for (int qi = 0; qi < q.Count && q.Count < 256; qi++)
        {
            var c = q[qi]; int lv = Wl(c); var up = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z); bool upW = up.ok && up.id == L;
            if (!upW && c.b == st.b)
            {
                int S = c.Y * 8 + lv;
                if (!hasHi || S > hiS) { hi = c; hiLv = lv; hiS = S; hasHi = true; }
                if ((lv < 8 || (up.ok && Wfree(up.id))) && (!hasLo || S < loS)) { lo = c; loLv = lv; loS = S; loUp = up; hasLo = true; }
            }
            for (int d = 0; d < 6; d++)
            {
                if (d == 2 && !upW) continue;
                var n = d == 2 ? up : Wcell(c.b, c.F, c.X + DX[d], c.Y + DY[d], c.Z + DZ[d]);
                if (n.ok && n.id == L && !seen.Contains(n.k)) { seen.Add(n.k); q.Add(n); }
            }
        }
        if (hasHi && hasLo && hiS - loS >= 2)
        {
            if (loLv < 8) Wput(lo, loLv + 1, L); else if (loUp.ok && Wfree(loUp.id)) Wput(loUp, 1, L); else return;
            Wput(hi, hiLv - 1, L);
        }
    }

    // nearest drop within 4 cells (a cell at this height whose cell below can take this liquid): returns the first sideways step toward it.
    // Breadth-first over a 9x9 grid of offsets around the cell; buffers are reused (this runs for every moving liquid cell).
    static readonly int[] dQ = new int[81], dFirst = new int[81]; static readonly byte[] dSeen = new byte[81]; static readonly int[] dOut = new int[2];
    internal static int[] Wdrop(WC c, int ro)
    {
        Array.Clear(dSeen); int qh = 0, qt = 0; dQ[qt++] = 40; dSeen[40] = 1; dFirst[40] = -1; int L = c.id;
        while (qh < qt)
        {
            int o = dQ[qh++], ox = o % 9 - 4, oz = o / 9 - 4;
            if (Math.Abs(ox) + Math.Abs(oz) >= 4) continue;
            for (int i = 0; i < 4; i++)
            {
                int dx = N4[(i + ro) & 3][0], dz = N4[(i + ro) & 3][1], nx = ox + dx, nz = oz + dz, ni = (nz + 4) * 9 + nx + 4;
                if (dSeen[ni] != 0) continue; dSeen[ni] = 1;
                var n = Wcell(c.b, c.F, c.X + nx, c.Y, c.Z + nz);
                if (!n.ok || !(Wfree(n.id) || (n.id == L && Wl(n) < 8))) continue;
                int first = dFirst[o] < 0 ? (i + ro) & 3 : dFirst[o]; dFirst[ni] = first;
                var bl = Wcell(n.b, n.F, n.X, n.Y - 1, n.Z);
                if (bl.ok && (Wfree(bl.id) || (bl.id == L && Wl(bl) < 8))) { dOut[0] = N4[first][0]; dOut[1] = N4[first][1]; return dOut; }
                dQ[qt++] = ni;
            }
        }
        return null;
    }

    /// <summary>Lava touching water: the lava freezes (a full cell to obsidian, else cobblestone) and a unit of the water boils off into the
    /// water cycle. True if cur itself changed.</summary>
    static bool React(WC cur)
    {
        int L = cur.id; if (L != Water && L != Lava) return false; int other = L == Water ? Lava : Water;
        for (int d = 0; d < 6; d++)
        {
            var n = Wcell(cur.b, cur.F, cur.X + DX[d], cur.Y + DY[d], cur.Z + DZ[d]); if (!n.ok || n.id != other) continue;
            WC lava = L == Lava ? cur : n, water = L == Lava ? n : cur;
            SetCell(lava.b, lava.F, lava.X, lava.Y, lava.Z, Wl(lava) >= 8 ? Obsidian : Cobblestone, 0, 0, true); Moves++;
            int lw = Wl(water); Wput(water, lw - 1, Water); Reservoir += 1;
            Wake(lava.b, lava.F, lava.X, lava.Y, lava.Z);
            return true;
        }
        return false;
    }

    // work per tick: the cells that were active when the tick started (at most 400; a cell moved during the tick is queued again but waits
    // for the next tick, so liquid moves one block per tick: falling and spreading at TicksPerSecond blocks a second) and 6 pressure searches.
    // WaterTick spreads a tick's work over the frames within a time budget.
    public const double TicksPerSecond = 5;
    const int CellsPerTick = 400, PressPerTick = 6;
    static int allowance, pressLeft;
    static void NewTick()
    {
        tickNo++;
        foreach (var (b, f, x, y, z) in nextTick) { var c = Wcell(b, f, x, y, z); if (c.ok && LIQ[c.id] && !WAct.ContainsKey(c.k)) { WAct[c.k] = (b, f, x, y, z); wactQ.Enqueue(c.k); } }
        nextTick.Clear();
    }
    /// <summary>One whole tick at once (benchmark, tests).</summary>
    public static void WaterStep() { NewTick(); allowance = Math.Min(wactQ.Count, CellsPerTick); pressLeft = PressPerTick; WaterWork(double.MaxValue); }

    static void WaterWork(double budgetMs)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); double perMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        while (allowance > 0 && wactQ.Count > 0)
        {
            if ((allowance & 15) == 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / perMs > budgetMs) return;
            allowance--;
            long k = wactQ.Dequeue(); if (!WAct.Remove(k, out var co)) continue;
            var cur = Wcell(co.b, co.f, co.x, co.y, co.z); if (!cur.ok || !LIQ[cur.id]) continue;
            int L = cur.id;
            if (React(cur)) continue;
            int visc = LiquidVisc[L];
            if (visc > 1 && (tickNo + co.x + co.z) % visc != 0) { nextTick.Add(co); continue; } // a thick liquid (lava) moves only every few ticks
            int l = Wl(cur), l0 = l;
            var up0 = Wcell(cur.b, cur.F, cur.X, cur.Y + 1, cur.Z);
            if (LiquidBoil[L] > 0 && up0.ok && Wfree(up0.id))
            {   // boils away where it is open to the air (liquid oxygen at -183 C)
                if (wrng.NextDouble() < LiquidBoil[L] / TicksPerSecond) { l--; Wput(cur, l, L); if (l == 0) continue; }
                nextTick.Add(co);
            }
            var dn = Wcell(cur.b, cur.F, cur.X, cur.Y - 1, cur.Z);
            if (dn.ok)
            {
                // the sea takes in water arriving on top of it (rivers, waterfalls): it goes back into the reservoir springs draw from
                if (L == Water && dn.id == Water && Wl(dn) == 8 && AboveSea(cur.b, cur.F, cur.X, cur.Y, cur.Z)) { Reservoir += l; WaterFlow.Move(cur, 0, -1, 0, l); Wput(cur, 0, L); continue; }
                if (Wfree(dn.id)) { WaterFlow.Move(cur, 0, -1, 0, l); Wput(dn, l, L); Wput(cur, 0, L); continue; }
                if (dn.id == L) { int ld = Wl(dn); if (ld < 8) { int m = Math.Min(l, 8 - ld); WaterFlow.Move(cur, 0, -1, 0, m); Wput(dn, ld + m, L); l -= m; if (l == 0) { Wput(cur, 0, L); continue; } } }
                else if (LIQ[dn.id] && LiquidDensity[L] > LiquidDensity[dn.id])
                {   // heavier above lighter: they change places (water sinks under oil)
                    int ld = Wl(dn), od = dn.id; Wput(dn, l, L); Wput(cur, ld, od); Wake(cur.b, cur.F, cur.X, cur.Y, cur.Z); continue;
                }
            }
            // a full cell whose four sides are full liquid or walls cannot move sideways: only pressure may apply (most cells of a big body)
            if (l == 8)
            {
                bool shut = true;
                for (int ni = 0; ni < 4 && shut; ni++)
                {
                    var nc = Wcell(cur.b, cur.F, cur.X + N4[ni][0], cur.Y, cur.Z + N4[ni][1]);
                    shut = nc.ok && (nc.id == L ? Wl(nc) == 8 : !Wfree(nc.id));
                }
                if (shut)
                {
                    var up = Wcell(cur.b, cur.F, cur.X, cur.Y + 1, cur.Z);
                    if (pressLeft > 0 && up.ok && !Wfree(up.id)) { pressLeft--; Wpress(cur); }
                    continue;
                }
            }
            int ro = wrng.Next(4); var dd = Wdrop(cur, ro);
            if (dd != null)
            {
                var nc = Wcell(cur.b, cur.F, cur.X + dd[0], cur.Y, cur.Z + dd[1]);
                if (nc.ok && (nc.id == L || Wfree(nc.id)))
                {
                    int ln = Wl(nc), m = Math.Min(l, 8 - ln);
                    if (m > 0) { WaterFlow.Move(cur, dd[0], 0, dd[1], m); Wput(nc, ln + m, L); l -= m; Wput(cur, l, L); continue; }
                }
            }
            for (int ni = 0; ni < 4; ni++)
            {
                int dx = N4[(ni + ro) & 3][0], dz = N4[(ni + ro) & 3][1];
                var nc = Wcell(cur.b, cur.F, cur.X + dx, cur.Y, cur.Z + dz);
                if (!nc.ok || (nc.id != L && !Wfree(nc.id))) continue;
                int lo = nc.id == L ? Wl(nc) : 0;
                if (l - lo >= 2) { int m = (l - lo) >> 1; l -= m; WaterFlow.Move(cur, dx, 0, dz, m); Wput(nc, lo + m, L); }
                else if (l - lo == 1 && lo > 0)
                {
                    var n2 = Wcell(cur.b, cur.F, cur.X + 2 * dx, cur.Y, cur.Z + 2 * dz);
                    if (n2.ok && (n2.id == L || Wfree(n2.id)) && (n2.id == L ? Wl(n2) : 0) < lo) { l -= 1; WaterFlow.Move(cur, dx, 0, dz, 1); Wput(nc, lo + 1, L); }
                }
            }
            if (l != l0) { Wput(cur, l, L); continue; }
            if (l == 8 && pressLeft > 0)
            {
                var up = Wcell(cur.b, cur.F, cur.X, cur.Y + 1, cur.Z);
                if (up.ok && !Wfree(up.id)) { pressLeft--; Wpress(cur); }
            }
        }
    }
    /// <summary><see cref="TicksPerSecond"/> ticks, each tick's work spread over the frames with at most <see cref="FrameBudgetMs"/> per frame (no frame spikes).</summary>
    public const double FrameBudgetMs = 1.5;
    public static void WaterTick(double dt)
    {
        WaterFlow.Tick(dt);
        wAcc += dt;
        if (wAcc > 1 / TicksPerSecond)
        {
            wAcc = Math.Min(wAcc - 1 / TicksPerSecond, 1 / TicksPerSecond); NewTick();
            // this tick: the cells queued now (those of an unfinished last tick are at the front of the queue)
            allowance = Math.Min(Math.Max(allowance, Math.Min(wactQ.Count, CellsPerTick)), 2 * CellsPerTick); pressLeft = Math.Min(pressLeft + PressPerTick, 2 * PressPerTick);
        }
        if (WAct.Count > 0) WaterWork(FrameBudgetMs);
    }

    // ---------- bucket: break = scoop up to its capacity out of the targeted liquid cell (drains it), place = pour everything held ----------
    internal static WC Wray(double reach)
    {
        for (double t = 0; t <= reach; t += .07)
        {
            double x = C.x + C.f.X * t, y = C.y + C.f.Y * t, z = C.z + C.f.Z * t;
            if (x * x + y * y + z * z < CORE * CORE) return default;
            int bi = P2b(x, y, z); if (bi < 0) continue;
            int f = PF, bx = PX, by = PY, bz = PZ, b = Blk(bi, f, bx, by, bz);
            if (LIQ[b]) return Wcell(bi, f, bx, by, bz);
            if (b != 0 && SPC[b] == 0) return default;
        }
        return default;
    }
    /// <summary>The liquid a bucket holds (its "liquid" state: a liquid block's id_name; water if the item has no such field).</summary>
    public static int BucketLiquid(ItemStack bucket)
    {
        var f = bucket.Def.State.Find("liquid"); if (f == null) return Water;
        string n = f.Values[f.Index(bucket.State)]; return GameAssets.BlockReg.Has("base:" + n) ? GameAssets.BlockReg.Id("base:" + n) : Water;
    }
    static bool SetBucketLiquid(ItemStack bucket, int liquid)
    {
        var f = bucket.Def.State.Find("liquid"); if (f == null) return liquid == Water;
        int i = Array.IndexOf(f.Values, DEFS[liquid].IdName); if (i < 0) return false;
        bucket.State = f.With(bucket.State, i); return true;
    }
    /// <summary>Bucket item (behavior "bucket"): break = scoop up to its capacity, place = pour everything. The fill level is the stack's "fill"
    /// state, the liquid its "liquid" state (one liquid at a time).</summary>
    internal static void BucketUse(ItemStack bucket, bool brk, bool plc)
    {
        double reach = P.on ? 6 : 10; var fillF = bucket.Def.State.Find("fill"); if (fillF == null) return;
        int cap = Math.Max(1, bucket.Def.Capacity), bk = fillF.Value(bucket.State);
        if (brk)
        {
            if (bk >= cap) { UI.Note("Bucket is full", 1.2); return; }
            var c = Wray(reach); if (!c.ok) return;
            if (bk > 0 && BucketLiquid(bucket) != c.id) { UI.Note($"The bucket holds {DEFS[BucketLiquid(bucket)].Name.ToLowerInvariant()}: pour it out first", 1.5); return; }
            if (!SetBucketLiquid(bucket, c.id)) { UI.Note($"A bucket can't hold {DEFS[c.id].Name.ToLowerInvariant()}", 1.5); return; }
            int l = Wl(c), t = Math.Min(cap - bk, l); bucket.State = fillF.With(bucket.State, bk + t); SetCell(c.b, c.F, c.X, c.Y, c.Z, l - t > 0 ? c.id : 0, l - t);
            return;
        }
        if (plc)
        {
            if (bk == 0) { UI.Note("Bucket is empty", 1.2); return; }
            var T = Raycast(reach); if (T == null) return;
            bool rp = T.core ? SPC[T.id] != 0 : SPC[Blk(T.bi, T.f, T.bx, T.by, T.bz)] != 0;
            double X = rp ? T.x : T.px, Y = rp ? T.y : T.py, Z = rp ? T.z : T.pz;
            if ((X - C.x) * (X - C.x) + (Y - C.y) * (Y - C.y) + (Z - C.z) * (Z - C.z) > 1) { PlaceLiquid(X, Y, Z, bk, BucketLiquid(bucket)); bucket.State = fillF.With(bucket.State, 0); }
        }
    }
}
