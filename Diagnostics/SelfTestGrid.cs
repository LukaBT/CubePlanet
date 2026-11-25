namespace CubePlanet;

public static partial class SelfTest
{
    // the grid: a 60-block line at LV vs at MV (transformers), a machine on MV, wires between connectors, pylons
    static void Grid()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n);
        var (_, _, g) = SpotInFront(16); int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX, y0 = PY + 24, z0 = PZ;
        var made = new List<Chunk>();
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 3; dx++)
                {
                    long k = ck(bi, f, (x0 >> 5) + dx, (y0 >> 5) + dy, (z0 >> 5) + dz);
                    if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz }; AddChunk(c); made.Add(c); }
                    ChunkGenerator.Ensure(c); c.built = true;
                }
        BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
        void Put(int x, int y, int z, string block, string facing = null)
        {
            int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { Wires.Reset(); for (int y = 4; y <= 14; y++) for (int z = -2; z <= 6; z++) for (int x = -2; x <= 70; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } }
        int coal = GameAssets.ItemReg.Id("coal");
        void Source() { for (int x = 0; x <= 3; x++) { Put(x, 8, 0, "coal_generator", "north"); BlockStates.entities[At(x, 8, 0).Key].Inv("fuel")[0] = new ItemStack(coal, 8); } }
        // delivered E/s into the two batteries at bx, bx + 1 (after 1 s to settle, over 5 s)
        double Delivered(int bx)
        {
            var b1 = BlockStates.entities[At(bx, 8, 0).Key]; var b2 = BlockStates.entities[At(bx + 1, 8, 0).Key];
            b1.SetNum("energy", 0); b2.SetNum("energy", 0);
            for (int i = 0; i < 10; i++) BlockActions.Tick(.1);
            double e0 = b1.Num("energy") + b2.Num("energy");
            for (int i = 0; i < 50; i++) BlockActions.Tick(.1);
            return (b1.Num("energy") + b2.Num("energy") - e0) / 5 / BatteryBehavior.Efficiency;
        }
        // 80 E/s of coal over 60 blocks of copper at LV: limited to 64 E/s and losing a lot on the way
        Clear(); Source(); for (int x = 4; x <= 63; x++) Put(x, 8, 0, "copper_cable"); Put(64, 8, 0, "battery"); Put(65, 8, 0, "battery");
        double lv = Delivered(64); string hudLv = EnergyNet.DescribeCable(At(30, 8, 0));
        // the same line at MV: transformer up (back LV to the generators, front MV to the line), transformer down at the far end
        Put(4, 8, 0, "transformer_lv_mv", "east"); Put(63, 8, 0, "transformer_lv_mv", "west");
        double mv = Delivered(64); string hudMv = EnergyNet.DescribeCable(At(30, 8, 0)); var tr = BlockStates.entities[At(4, 8, 0).Key];
        Check(lv < 57 && lv > 45 && mv > 75 && hudLv.StartsWith("LV") && hudMv.StartsWith("MV") && tr.Num("ok") > 0 && Math.Abs(tr.Num("flow") - 80) < 1,
              $"60 blocks of copper: at LV {lv:0.0} of 80 E/s arrive ({hudLv}); stepped up to MV {mv:0.0} E/s ({hudMv}; transformer {tr.Num("flow"):0.0} E/s)");
        // a machine on the MV line does nothing (machines run on LV)
        Put(30, 9, 0, "battery"); var wrong = BlockStates.entities[At(30, 9, 0).Key];
        for (int i = 0; i < 20; i++) BlockActions.Tick(.1);
        Check(wrong.Num("energy") == 0 && EnergyNet.WrongVoltage.TryGetValue(At(30, 9, 0).Key, out var wv) && wv == 8, "a battery on the MV line gets nothing (WRONG VOLTAGE)");
        // a transformer turned the wrong way (MV side toward the generators) fights the other one: it passes nothing
        Put(4, 8, 0, "transformer_lv_mv", "west");
        for (int i = 0; i < 5; i++) BlockActions.Tick(.1);
        Check(BlockStates.entities[At(4, 8, 0).Key].Num("ok") < 0, "a transformer with a side on the wrong voltage passes nothing");

        // wires: generators -> gold cable -> connector ==40 blocks of wire== connector -> gold cable -> batteries
        Clear(); Source(); Put(4, 8, 0, "gold_cable"); Put(5, 8, 0, "wire_connector"); Put(45, 8, 0, "wire_connector"); Put(46, 8, 0, "gold_cable");
        Put(47, 8, 0, "battery"); Put(48, 8, 0, "battery");
        bool linked = Wires.Connect(At(5, 8, 0), At(45, 8, 0)); var link = Wires.Links.FirstOrDefault();
        double wired = Delivered(47);
        Check(linked && link != null && Math.Abs(link.Length - 40) < .5 && wired > 77 && wired < 80 && Math.Abs(link.Flow - 80) < 1 && Wires.Describe(At(5, 8, 0)).StartsWith("1 WIRE"),
              $"a 40-block wire carries {link?.Flow:0.0} E/s between connectors, {wired:0.0} E/s arrive ({Wires.Describe(At(5, 8, 0))})");
        Put(5, 8, 60, "wire_connector");
        Check(!Wires.Connect(At(5, 8, 0), At(5, 8, 60)) && Wires.Links.Count == 1, "a wire longer than 48 blocks is refused");
        var saved = Wires.Save(); Wires.Load(System.Text.Json.Nodes.JsonNode.Parse(saved.ToJsonString()));
        Check(Wires.Links.Count == 1 && Wires.Same(Wires.Links[0].A, At(5, 8, 0)) && Math.Abs(Wires.Links[0].Length - link.Length) < 1e-9, "wires save and load");
        SetCell(bi, f, x0 + 45, y0 + 8, z0, 0); BlockActions.Tick(.1);
        Check(Wires.Links.Count == 0, "breaking a connector takes its wire down");
        // a pylon: 5 blocks tall, its top holds the wire
        BlockActions.Place(At(10, 6, 4), Id("pylon"), new V3(0, 0, 1), out _);
        Check(At(10, 10, 4).Id == Id("pylon") && Wires.IsAnchor(At(10, 10, 4)) && !Wires.IsAnchor(At(10, 6, 4)), "a pylon stands 5 blocks tall and holds wires at its top");
        Console.WriteLine($"GRID 80 E/s over 60 blocks of copper: LV {lv:0.0} E/s arrive, MV {mv:0.0} E/s; 40-block wire {wired:0.0} E/s");
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
