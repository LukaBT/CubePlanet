namespace CubePlanet;

/// <summary>
/// Routing energy through cables with limits: a maximum flow (Dinic) from what machines make to what machines take, through the network's
/// blocks. A cable block carries at most its capacity (definition key "cable": {"capacity": E/s}) per second; machines pass any amount
/// (machines touching each other connect directly). So a thin cable only limits the branch it is on, and parallel cables add up.
/// </summary>
public static class CableFlow
{
    public const double Inf = 1e18;

    /// <summary>A network's blocks (cables and machines), their capacities per step (Inf for machines) and the joins between them.</summary>
    public sealed class Graph
    {
        public readonly List<long> Keys = new(); public readonly List<double> Cap = new(); public readonly List<(int a, int b)> Edges = new();
        public readonly Dictionary<long, int> Index = new();
        public int Add(long key, double cap) { int i = Keys.Count; Keys.Add(key); Cap.Add(cap); Index[key] = i; return i; }
    }

    /// <summary>Actual energy each machine makes / takes this step and the flow through every block (index = graph node), given what the
    /// balance planned (made / got per machine, at graph nodes machineNode).</summary>
    public static (double[] made, double[] got, double[] through) Route(Graph g, int[] machineNode, double[] made, double[] got)
    {
        int n = g.Keys.Count, S = 2 * n, T = 2 * n + 1; var d = new Dinic(2 * n + 2);
        var thru = new int[n];
        for (int v = 0; v < n; v++) thru[v] = d.Edge(2 * v, 2 * v + 1, g.Cap[v]);
        foreach (var (a, b) in g.Edges) { d.Edge(2 * a + 1, 2 * b, Inf); d.Edge(2 * b + 1, 2 * a, Inf); }
        int m = machineNode.Length; var src = new int[m]; var snk = new int[m];
        for (int i = 0; i < m; i++)
        {
            src[i] = made[i] > 0 ? d.Edge(S, 2 * machineNode[i], made[i]) : -1;
            snk[i] = got[i] > 0 ? d.Edge(2 * machineNode[i] + 1, T, got[i]) : -1;
        }
        d.Run(S, T);
        var mo = new double[m]; var go = new double[m]; var th = new double[n];
        for (int i = 0; i < m; i++) { mo[i] = src[i] < 0 ? 0 : d.Flow(src[i]); go[i] = snk[i] < 0 ? 0 : d.Flow(snk[i]); }
        for (int v = 0; v < n; v++) th[v] = d.Flow(thru[v]);
        return (mo, go, th);
    }

    sealed class Dinic
    {
        readonly List<int> to = new(), next = new(); readonly List<double> cap = new(), orig = new();
        readonly int[] head, level, it; const double Eps = 1e-9;
        public Dinic(int n) { head = new int[n]; Array.Fill(head, -1); level = new int[n]; it = new int[n]; }
        public int Edge(int a, int b, double c)
        {
            int e = to.Count;
            to.Add(b); cap.Add(c); orig.Add(c); next.Add(head[a]); head[a] = e;
            to.Add(a); cap.Add(0); orig.Add(0); next.Add(head[b]); head[b] = e + 1;
            return e;
        }
        public double Flow(int e) => orig[e] - cap[e];
        bool Bfs(int s, int t)
        {
            Array.Fill(level, -1); var q = new Queue<int>(); q.Enqueue(s); level[s] = 0;
            while (q.Count > 0)
            {
                int v = q.Dequeue();
                for (int e = head[v]; e >= 0; e = next[e]) if (cap[e] > Eps && level[to[e]] < 0) { level[to[e]] = level[v] + 1; q.Enqueue(to[e]); }
            }
            return level[t] >= 0;
        }
        readonly Stack<int> path = new();
        // one augmenting path in the level graph (iterative: a cable line can be thousands of blocks long)
        double Augment(int s, int t)
        {
            path.Clear(); int v = s;
            while (true)
            {
                if (v == t)
                {
                    double f = Inf; foreach (int e in path) f = Math.Min(f, cap[e]);
                    foreach (int e in path) { cap[e] -= f; cap[e ^ 1] += f; }
                    return f;
                }
                bool moved = false;
                for (; it[v] >= 0; it[v] = next[it[v]])
                {
                    int e = it[v];
                    if (cap[e] > Eps && level[to[e]] == level[v] + 1) { path.Push(e); v = to[e]; moved = true; break; }
                }
                if (moved) continue;
                if (path.Count == 0) return 0;
                level[v] = -1; int back = path.Pop(); v = to[back ^ 1]; it[v] = next[it[v]]; // dead end: step back and skip it
            }
        }
        public void Run(int s, int t)
        {
            while (Bfs(s, t))
            {
                Array.Copy(head, it, head.Length);
                while (Augment(s, t) > Eps) { }
            }
        }
    }
}
