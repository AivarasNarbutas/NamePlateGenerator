using Clipper2Lib;
using LibTessDotNet;

namespace NamePlate.Core;

/// <summary>2.5D mesh building from Clipper regions (outer paths CCW, holes CW).</summary>
public static class Extrusion
{
    public const int Precision = 4;

    public static PathsD Rect(double x0, double y0, double x1, double y1) =>
        new() { new PathD { new PointD(x0, y0), new PointD(x1, y0), new PointD(x1, y1), new PointD(x0, y1) } };

    public static double Area(PathsD p) => p.Sum(Clipper.Area);

    /// <summary>Prepares a region for meshing: drops sub-micron detail and separates outlines that touch in a single
    /// point (the same vertex used twice gives non-manifold edges). Changes the shape by at most a few microns.</summary>
    public static PathsD Clean(PathsD region)
    {
        if (region.Count == 0) return region;
        var r = Clipper.SimplifyPaths(region, 0.0005, true);
        r.RemoveAll(p => p.Count < 3 || Math.Abs(Clipper.Area(p)) < 1e-6);

        static (long, long) Key(PointD p) => ((long)Math.Round(p.x * 1e4), (long)Math.Round(p.y * 1e4));
        var uses = new Dictionary<(long, long), int>();
        foreach (var path in r) foreach (var p in path) uses[Key(p)] = uses.GetValueOrDefault(Key(p)) + 1;
        if (uses.Values.All(n => n == 1)) return r;

        // Move each shared vertex 2 µm into its own material (left of the travel direction: outers CCW, holes CW).
        const double nudge = 0.002;
        var res = new PathsD(r.Count);
        foreach (var path in r)
        {
            var np = new PathD(path);
            int n = path.Count;
            for (int i = 0; i < n; i++)
            {
                if (uses[Key(path[i])] < 2) continue;
                var a = path[(i - 1 + n) % n]; var p = path[i]; var b = path[(i + 1) % n];
                double nx = 0, ny = 0;
                foreach (var (dx, dy) in new[] { (p.x - a.x, p.y - a.y), (b.x - p.x, b.y - p.y) })
                {
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0) { nx += -dy / len; ny += dx / len; }
                }
                double nl = Math.Sqrt(nx * nx + ny * ny);
                if (nl > 1e-9) np[i] = new PointD(p.x + nx / nl * nudge, p.y + ny / nl * nudge);
            }
            res.Add(np);
        }
        return res;
    }

    public static PathsD Concat(PathsD a, PathsD b) { var r = new PathsD(a); r.AddRange(b); return r; }

    /// <summary>Horizontal cap of <paramref name="region"/> at height z, normal +Z (up) or −Z.</summary>
    public static void Cap(this TriMesh mesh, PathsD region, double z, bool up)
    {
        if (region.Count == 0) return;
        double cx = 0, cy = 0; int n = 0;
        foreach (var path in region) foreach (var p in path) { cx += p.x; cy += p.y; n++; }
        cx /= n; cy /= n;

        var tess = new Tess();
        foreach (var path in region)
        {
            if (path.Count < 3) continue;
            var cv = new ContourVertex[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                cv[i].Position = new Vec3 { X = (float)(path[i].x - cx), Y = (float)(path[i].y - cy), Z = 0 };
                cv[i].Data = mesh.AddVertex(path[i].x, path[i].y, z);
            }
            tess.AddContour(cv, ContourOrientation.Original);
        }
        tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

        int Id(int k)
        {
            var v = tess.Vertices[tess.Elements[k]];
            return v.Data is int id ? id : mesh.AddVertex(v.Position.X + cx, v.Position.Y + cy, z);
        }

        // LibTess emits every triangle with the same winding, but may emit zero-area slivers along collinear
        // vertices. Slivers must stay (dropping them opens a crack), so the winding is decided for the whole batch.
        var tris = new List<(int A, int B, int C)>();
        int pos = 0, neg = 0;
        for (int t = 0; t < tess.ElementCount; t++)
        {
            if (tess.Elements[t * 3] < 0 || tess.Elements[t * 3 + 1] < 0 || tess.Elements[t * 3 + 2] < 0) continue;
            int a = Id(t * 3), b = Id(t * 3 + 1), c = Id(t * 3 + 2);
            var va = mesh.Vertices[a]; var vb = mesh.Vertices[b]; var vc = mesh.Vertices[c];
            double cross = (vb.X - va.X) * (vc.Y - va.Y) - (vb.Y - va.Y) * (vc.X - va.X);
            if (cross > 0) pos++; else if (cross < 0) neg++;
            tris.Add((a, b, c));
        }
        bool tessCcw = pos >= neg;
        foreach (var (a, b, c) in tris)
            if (tessCcw == up) mesh.AddTriangle(a, b, c); else mesh.AddTriangle(a, c, b);
    }

    /// <summary>Vertical walls along every contour between zBottom and zTop.
    /// <paramref name="normalsOutOfRegion"/>: true when the solid lies inside the region (letters, embossing),
    /// false when the region is a void in the solid (pockets).</summary>
    public static void Walls(this TriMesh mesh, PathsD region, double zBottom, double zTop, bool normalsOutOfRegion)
    {
        foreach (var path in region)
        {
            int n = path.Count;
            for (int i = 0; i < n; i++)
            {
                var p = path[i]; var q = path[(i + 1) % n];
                int a0 = mesh.AddVertex(p.x, p.y, zBottom), b0 = mesh.AddVertex(q.x, q.y, zBottom);
                int b1 = mesh.AddVertex(q.x, q.y, zTop), a1 = mesh.AddVertex(p.x, p.y, zTop);
                if (normalsOutOfRegion) { mesh.AddTriangle(a0, b0, b1); mesh.AddTriangle(a0, b1, a1); }
                else { mesh.AddTriangle(a0, b1, b0); mesh.AddTriangle(a0, a1, b1); }
            }
        }
    }

    /// <summary>Wall band between two closed rings (CCW), normals pointing outwards when ringB is above ringA.</summary>
    public static void Band(this TriMesh mesh, PathD ringA, double zA, PathD ringB, double zB)
    {
        int n = ringA.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = mesh.AddVertex(ringA[i].x, ringA[i].y, zA), b = mesh.AddVertex(ringA[j].x, ringA[j].y, zA);
            int c = mesh.AddVertex(ringB[j].x, ringB[j].y, zB), d = mesh.AddVertex(ringB[i].x, ringB[i].y, zB);
            mesh.AddTriangle(a, b, c); mesh.AddTriangle(a, c, d);
        }
    }

    public static PathsD MirrorX(PathsD p, double length)
    {
        var r = new PathsD();
        foreach (var path in p)
        {
            var m = new PathD(path.Count);
            for (int i = path.Count - 1; i >= 0; i--) m.Add(new PointD(length - path[i].x, path[i].y));
            r.Add(m);
        }
        return r;
    }

    public static PathsD Translate(PathsD p, double dx, double dy)
    {
        var r = new PathsD();
        foreach (var path in p) r.Add(new PathD(path.Select(pt => new PointD(pt.x + dx, pt.y + dy))));
        return r;
    }

    public static PathsD Scale(PathsD p, double k)
    {
        var r = new PathsD();
        foreach (var path in p) r.Add(new PathD(path.Select(pt => new PointD(pt.x * k, pt.y * k))));
        return r;
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(PathsD p)
    {
        double x0 = double.MaxValue, y0 = x0, x1 = double.MinValue, y1 = x1;
        foreach (var path in p) foreach (var pt in path)
        {
            x0 = Math.Min(x0, pt.x); y0 = Math.Min(y0, pt.y); x1 = Math.Max(x1, pt.x); y1 = Math.Max(y1, pt.y);
        }
        return (x0, y0, x1, y1);
    }
}
