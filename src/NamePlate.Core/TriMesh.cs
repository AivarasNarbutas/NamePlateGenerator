namespace NamePlate.Core;

public readonly record struct V3(double X, double Y, double Z);

/// <summary>Indexed triangle mesh with vertex welding (1e-4 mm grid) so closedness can be verified.</summary>
public sealed class TriMesh
{
    private readonly List<V3> _v = new();
    private readonly Dictionary<(long, long, long), int> _map = new();
    private readonly List<int> _idx = new();

    public IReadOnlyList<V3> Vertices => _v;
    public IReadOnlyList<int> Indices => _idx;
    public int TriangleCount => _idx.Count / 3;
    public bool IsEmpty => _idx.Count == 0;

    public int AddVertex(double x, double y, double z)
    {
        var key = ((long)Math.Round(x * 1e4), (long)Math.Round(y * 1e4), (long)Math.Round(z * 1e4));
        if (_map.TryGetValue(key, out int id)) return id;
        id = _v.Count;
        _v.Add(new V3(x, y, z));
        _map[key] = id;
        return id;
    }

    public void AddTriangle(int a, int b, int c)
    {
        if (a == b || b == c || a == c) return;
        _idx.Add(a); _idx.Add(b); _idx.Add(c);
    }

    public TriMesh Transformed(Func<V3, V3> f)
    {
        var m = new TriMesh();
        var map = new int[_v.Count];
        for (int i = 0; i < _v.Count; i++) { var p = f(_v[i]); map[i] = m.AddVertex(p.X, p.Y, p.Z); }
        for (int i = 0; i < _idx.Count; i += 3) m.AddTriangle(map[_idx[i]], map[_idx[i + 1]], map[_idx[i + 2]]);
        return m;
    }

    public (V3 Min, V3 Max) Bounds()
    {
        if (_v.Count == 0) return (default, default);
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        foreach (var p in _v)
        {
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
        }
        return (new V3(x0, y0, z0), new V3(x1, y1, z1));
    }

    /// <summary>Signed volume in mm³ (positive when normals point outwards).</summary>
    public double Volume()
    {
        double v = 0;
        for (int i = 0; i < _idx.Count; i += 3)
        {
            var a = _v[_idx[i]]; var b = _v[_idx[i + 1]]; var c = _v[_idx[i + 2]];
            v += a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
        }
        return v / 6.0;
    }

    /// <summary>Counts directed edges that are not matched by exactly one opposite edge (0 = watertight, consistently oriented).</summary>
    public int CountOpenEdges() => OpenEdges().Count;

    public List<(V3 A, V3 B)> OpenEdges()
    {
        var edges = new Dictionary<(int, int), int>();
        for (int i = 0; i < _idx.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                var e = (_idx[i + k], _idx[i + (k + 1) % 3]);
                edges[e] = edges.GetValueOrDefault(e) + 1;
            }
        var bad = new List<(V3, V3)>();
        foreach (var (e, n) in edges)
            if (n != 1 || !edges.TryGetValue((e.Item2, e.Item1), out int m) || m != 1) bad.Add((_v[e.Item1], _v[e.Item2]));
        return bad;
    }
}
