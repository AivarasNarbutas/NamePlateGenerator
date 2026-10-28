using NamePlate.Core;

namespace NamePlate.Tests;

/// <summary>Inputs a user can type; none may crash or produce a broken mesh, and odd ones must explain themselves.</summary>
public static class EdgeCaseTests
{
    private static PlateSettings P(Action<PlateSettings> f) { var s = new PlateSettings { FontFamily = "Arial" }; f(s); return s; }

    public static void Run()
    {
        T.Section("Edge cases");
        var cases = new (string Name, PlateSettings S, string? Warn)[]
        {
            ("missing font name", P(s => s.FontFamily = "No Such Font 123"), "not installed"),
            ("missing font file", P(s => s.FontFile = @"C:\nope\x.ttf"), "Font file not found"),
            ("run with missing font", P(s => s.FrontRuns = new() { new TextRun { Text = "ab", FontFamily = "Nope Font" } }), "not installed"),
            ("margin 0, no chamfer, clipped", P(s => { s.Margin = 0; s.Chamfer = 0; s.TextSize = 40; s.FrontText = "WIDE TEXT HERE"; }), "clipped"),
            ("margin 0, clipped", P(s => { s.Margin = 0; s.TextSize = 40; s.FrontText = "WIDE TEXT HERE"; }), "clipped"),
            ("emboss margin 0, clipped", P(s => { s.Margin = 0; s.Chamfer = 0; s.Mode = TextMode.Emboss; s.TextSize = 40; s.FrontText = "WIDE TEXT HERE"; }), "clipped"),
            ("huge margin", P(s => { s.Margin = 30; s.AutoFit = true; }), "no room"),
            ("auto-fit single dash", P(s => { s.AutoFit = true; s.FrontText = "-"; }), null),
            ("line spacing 0", P(s => s.LineSpacing = 0), "Line spacing"),
            ("line spacing -1", P(s => s.LineSpacing = -1), "Line spacing"),
            ("clearance 3", P(s => s.Clearance = 3), "removes all"),
            ("clearance 0.6", P(s => s.Clearance = 0.6), "thin strokes"),
            ("negative clearance", P(s => s.Clearance = -0.2), "negative"),
            ("pocket depth 0", P(s => s.PocketDepth = 0), "Pocket depth raised"),
            ("pocket depth 10", P(s => s.PocketDepth = 10), "Pocket depth limited"),
            ("both faces", P(s => s.Faces = TextFaces.Both), "Pocket depth limited"),
            ("thickness 0", P(s => s.Thickness = 0), "Thickness raised"),
            ("length -5", P(s => s.Length = -5), "Length raised"),
            ("chamfer 5", P(s => s.Chamfer = 5), "Chamfer reduced"),
            ("text size 0", P(s => s.TextSize = 0), "greater than 0"),
            ("text size 500", P(s => s.TextSize = 500), "outside"),
            ("offset far away", P(s => s.OffsetX = 500), "completely outside"),
            ("tabs and spaces", P(s => s.FrontText = "\tA  B\t"), null),
            ("Lithuanian letters", P(s => s.FrontText = "Ąžuolas Čiurlionis ĘĖĮŠŲŪŽ"), null),
            ("emoji", P(s => s.FrontText = "Hi 😀"), null),
            ("only newlines", P(s => s.FrontText = "\n\n"), null),
            ("CRLF text", P(s => s.FrontText = "Line1\r\nLine2"), null),
            ("back only emboss", P(s => { s.Faces = TextFaces.Back; s.Mode = TextMode.Emboss; }), "cannot lie flat"),
            ("script font", P(s => { s.FontFamily = "Segoe Script"; s.FrontText = "Signature style"; }), null),
            ("run size 0 / negative", P(s => s.FrontRuns = new() { new TextRun { Text = "ab", Size = 0 }, new TextRun { Text = "cd", Size = -3 } }), null),
            ("null runs", P(s => s.FrontRuns = null!), null),
            ("run with null text", P(s => s.FrontRuns = new() { new TextRun { Text = null! } }), null),
            ("NaN length", P(s => s.Length = double.NaN), null),
            ("infinite text size", P(s => s.TextSize = double.PositiveInfinity), null),
            ("tiny plate", P(s => { s.Length = 5; s.Height = 5; s.Thickness = 0.8; }), null),
        };

        foreach (var (name, s, w) in cases)
        {
            try
            {
                var m = PlateBuilder.Build(s);
                bool closed = new[] { m.Plate, m.LettersFront, m.LettersBack }.All(x => x == null || x.IsEmpty || (x.CountOpenEdges() == 0 && x.Volume() > 0));
                bool warned = w == null || m.Warnings.Any(x => x.Contains(w, StringComparison.OrdinalIgnoreCase));
                T.Check(closed && warned && m.EffectiveTextSize >= 0,
                    $"{name}: closed={closed} warned={warned} size={m.EffectiveTextSize:0.##}" + (warned ? "" : " | got: " + string.Join(" | ", m.Warnings)));
            }
            catch (Exception ex) { T.Check(false, $"{name}: {ex.GetType().Name} {ex.Message}"); }
        }

        var legacy = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial Rounded MT Bold" });
        T.Check(TextOutline.IsInstalled("Arial Rounded MT Bold") == File.Exists(@"C:\Windows\Fonts\ARLRDBD.TTF")
                && (!TextOutline.IsInstalled("Arial Rounded MT Bold") || legacy.Warnings.All(w => !w.Contains("not installed"))),
            "legacy font names that resolve (\"Arial Rounded MT Bold\") are not reported as missing");

        var dash = PlateBuilder.Build(P(s => { s.AutoFit = true; s.FrontText = "-"; }));
        T.Check(dash.EffectiveTextSize <= 40, $"auto-fit never makes the font taller than the plate ({dash.EffectiveTextSize:0.#} mm)");

        // project files with damaged content
        T.Check(Throws(() => PlateSettings.FromJson("{\"Mode\":\"Bogus\"}")), "unknown enum value is rejected with a message");
        T.Check(Throws(() => PlateSettings.FromJson("null")), "'null' project is rejected");
        var nulls = PlateSettings.FromJson("{\"FrontRuns\":null,\"FontFamily\":null,\"Length\":\"NaN\"}");
        T.Check(nulls.FrontRuns != null && nulls.FontFamily != null && double.IsFinite(nulls.Length), "nulls and NaN in a project are repaired");
        T.Check(Throws(() => Exporters.LoadProject(@"C:\Windows\win.ini")), "a non-project file is rejected");
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }
}
