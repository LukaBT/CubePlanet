namespace CubePlanet;

/// <summary>Breaking, placing and picking blocks with the selected hotbar block.</summary>
public static class BlockEditing
{
    public static int sel = Stone, slot = 2; // selected block id, selected hotbar slot (1..10)

    // ---------- editing ----------
    /// <summary>March along the view ray; returns the first solid block hit plus the empty cell just before it.</summary>
    public static RayHit Raycast(double reach)
    {
        double px = C.x, py = C.y, pz = C.z;
        for (double t = 0; t <= reach; t += .07)
        {
            double x = C.x + C.f.X * t, y = C.y + C.f.Y * t, z = C.z + C.f.Z * t;
            if (x * x + y * y + z * z < CORE * CORE)
            {
                int id = CoreAt(x, y, z);
                if (id != 0 && id != Water) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, core = true, id = id, ci = (int)Math.Floor(x), cj = (int)Math.Floor(y), ck = (int)Math.Floor(z) };
                px = x; py = y; pz = z; continue;
            }
            int bi = P2b(x, y, z);
            if (bi >= 0)
            {
                int f = PF, bx = PX, by = PY, bz = PZ, hb = Blk(bi, f, bx, by, bz);
                if (hb != 0 && hb != Water) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, bi = bi, f = f, bx = bx, by = by, bz = bz };
            }
            px = x; py = y; pz = z;
        }
        return null;
    }
    public static void Edit(bool brk, bool plc)
    {
        if (sel == BK) { BucketUse(brk, plc); return; }
        var T = Raycast(P.on ? 6 : 10); if (T == null) return;
        if (brk)
        {
            int old0 = GetAt(T.x, T.y, T.z);
            SetAt(T.x, T.y, T.z, 0);
            if (UWP[old0]) PlaceWater(T.x, T.y, T.z); // underwater plants leave water behind, not an air pocket
            V3 g = M.Nrm(new V3(T.x, T.y, T.z));
            for (int k = 1; k < 6; k++)
            {
                double X = T.x + g.X * k, Y = T.y + g.Y * k, Z = T.z + g.Z * k; int b = GetAt(X, Y, Z);
                if (SPC[b] != 0 && ATT[b] == 0) { SetAt(X, Y, Z, 0); if (UWP[b]) PlaceWater(X, Y, Z); } else break;
            }
        }
        else if (plc)
        {
            bool rp = T.core ? SPC[T.id] != 0 : SPC[Blk(T.bi, T.f, T.bx, T.by, T.bz)] != 0;
            double X = rp ? T.x : T.px, Y = rp ? T.y : T.py, Z = rp ? T.z : T.pz;
            if ((X - C.x) * (X - C.x) + (Y - C.y) * (Y - C.y) + (Z - C.z) * (Z - C.z) > 1)
            {
                int prev = GetAt(X, Y, Z); if (sel == Water) PlaceWater(X, Y, Z); else SetAt(X, Y, Z, sel);
                if (P.on && Pcol(P.p, HW)) SetAt(X, Y, Z, prev);
            }
        }
    }
    public static void PickBlock()
    {
        var T = Raycast(P.on ? 6 : 10); if (T == null) return;
        int id = T.core ? T.id : Blk(T.bi, T.f, T.bx, T.by, T.bz);
        if (id == 0 || id == Water) return;
        UI.SetSlotBlock(id);
    }
}
