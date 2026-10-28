using NamePlate.Core;

namespace NamePlate.Tests;

/// <summary>Minimal assertion helpers; the runner prints PASS/FAIL lines and exits non-zero on failure.</summary>
public static class T
{
    private static int _failures, _passes;

    public static void Section(string name) => Console.WriteLine($"\n== {name}");

    public static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (ok) _passes++; else _failures++;
    }

    public static void Closed(string name, TriMesh? m)
    {
        if (m == null) { Check(false, name + " missing"); return; }
        int open = m.CountOpenEdges(); double v = m.Volume();
        Check(open == 0 && v > 0, $"{name}: {m.TriangleCount} tris, volume {v:0.0} mm3, open edges {open}");
    }

    public static int Summary()
    {
        Console.WriteLine(_failures == 0 ? $"\nALL {_passes} PASSED" : $"\n{_failures} FAILED, {_passes} passed");
        return _failures == 0 ? 0 : 1;
    }
}
