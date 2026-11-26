namespace CubePlanet;

/// <summary>A 32^3 block chunk of one band face (or of the Cartesian core when core = true) with its meshes and thread claims.</summary>
public class Chunk
{
    public long key; public int band, face, cx, cy, cz;
    public ushort[] d; public bool dirty = true, built, emp, solid, core, empty; // d = block IDs (2 bytes), index x + 32 * (z + 32 * y)
    public int gst; public bool gfail; // generation claim: 0 free, 1 claimed by a worker or the main thread; gfail = a worker threw, the main thread retries
    public Mesh mesh, wmesh;
    public double mx, my, mz, cr;
    public List<long> wait;
    public int[] cc;
    public ushort[] st;        // state words (BlockStates), null while every block of the chunk is in its default state
    public long waitStamp = -1;
    public int mst;            // mesh claim: 0 idle, 1 a worker is meshing it
    public bool relight;       // re-mesh in the background (a neighbour it was waiting for appeared, light near an edit, flowing water)
    public bool urgent;        // a player edit: re-meshed by the workers before anything else (the old mesh stays until the new one is ready)
    public int ver;            // bumped by every block change: a background mesh started before a change is redone
    public int readyWait;      // frames spent waiting for neighbour data before meshing
    public bool mfail;         // background mesh threw: retry on the main thread
}
