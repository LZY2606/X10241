namespace IsotopeApp.Analysis;

/// <summary>
/// Uniform lattice over the proportion simplex. Proportions are always sampled
/// on the mathematically constrained simplex (sum one, non-negative); we never
/// clip an unconstrained answer and renormalise it.
/// </summary>
public sealed class SimplexLattice
{
    public int Components { get; }
    public int Subdivisions { get; }
    public double[][] Points { get; }
    private readonly int[] _composition;
    private readonly Dictionary<int, int> _index;

    public SimplexLattice(int components, int subdivisions)
    {
        Components = components;
        Subdivisions = subdivisions;
        var ints = new List<int[]>();
        void Rec(int start, int remaining, int[] cur)
        {
            if (start == components - 1)
            {
                cur[start] = remaining;
                ints.Add((int[])cur.Clone());
                return;
            }
            for (int v = 0; v <= remaining; v++)
            {
                cur[start] = v;
                Rec(start + 1, remaining - v, cur);
            }
        }
        Rec(0, subdivisions, new int[components]);
        Points = ints.Select(c => c.Select(v => (double)v / subdivisions).ToArray()).ToArray();
        _composition = ints.SelectMany(c => c).ToArray();
        _index = new Dictionary<int, int>();
        for (int i = 0; i < ints.Count; i++)
            _index[Code(ints[i])] = i;
    }

    private int Code(int[] c)
    {
        int b = Subdivisions + 1, h = 0;
        foreach (var v in c) h = h * b + v;
        return h;
    }

    /// <summary>Adjacent lattice indices reachable by moving one unit a->b.</summary>
    public IEnumerable<int> Neighbours(int index)
    {
        int k = Subdivisions, n = Components;
        int offset = index * n;
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                if (a == b || _composition[offset + a] >= k || _composition[offset + b] <= 0)
                    continue;
                var nc = new int[n];
                for (int t = 0; t < n; t++) nc[t] = _composition[offset + t];
                nc[a]++; nc[b]--;
                if (_index.TryGetValue(Code(nc), out var j)) yield return j;
            }
    }

    public List<List<int>> ConnectedComponents(bool[] feasible)
    {
        var seen = new bool[Points.Length];
        var result = new List<List<int>>();
        for (int i = 0; i < feasible.Length; i++)
        {
            if (!feasible[i] || seen[i]) continue;
            var stack = new Stack<int>();
            stack.Push(i); seen[i] = true;
            var members = new List<int>();
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                members.Add(u);
                foreach (var v in Neighbours(u))
                    if (feasible[v] && !seen[v]) { seen[v] = true; stack.Push(v); }
            }
            result.Add(members);
        }
        result.Sort((x, y) => y.Count.CompareTo(x.Count));
        return result;
    }
}
