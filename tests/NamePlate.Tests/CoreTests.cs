using Clipper2Lib;
using NamePlate.Core;

namespace NamePlate.Tests;

public static class CoreTests
{
    public static void Run()
    {
        T.Section("Geometry");
        var d = new PlateSettings { FontFamily = "Arial", FrontText = "Head of automation\ndepartment", TextSize = 9 };

        // blank plate (no text) must match the reference geometry: 175 x 40 x 2.975, 0.5 chamfer on long edges
        var blank = PlateBuilder.Build(new PlateSettings { FrontText = "" });
        double expected = 175 * 40 * 2.975 - 4 * (0.5 * 0.5 / 2) * 175;
        T.Closed("blank plate", blank.Plate);
        T.Check(Math.Abs(blank.Plate.Volume() - expected) < 1, $"blank volume {blank.Plate.Volume():0.00} ~ {expected:0.00}");
        var (mn, mx) = blank.Plate.Bounds();
        T.Check(Math.Abs(mx.X - mn.X - 175) < 1e-3 && Math.Abs(mx.Y - mn.Y - 40) < 1e-3 && Math.Abs(mx.Z - mn.Z - 2.975) < 1e-3, "blank bounds 175x40x2.975");

        var inlay = PlateBuilder.Build(d);
        T.Closed("inlay plate", inlay.Plate); T.Closed("inlay letters", inlay.LettersFront);
        double pocketVol = blank.Plate.Volume() - inlay.Plate.Volume();
        T.Check(pocketVol > 100, $"pocket removes {pocketVol:0.0} mm3");
        T.Check(inlay.LettersFront!.Volume() < pocketVol && inlay.LettersFront.Volume() > pocketVol * 0.6, "letters smaller than pocket by the clearance");
        T.Check(inlay.Warnings.Count == 0, "no warnings for a normal plate: " + string.Join(" | ", inlay.Warnings));

        // the default font of the reference model (installed with Office); its letters used to be non-manifold
        var def = PlateBuilder.Build(new PlateSettings());
        T.Closed("default settings plate", def.Plate); T.Closed("default settings letters", def.LettersFront);

        var both = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", FrontText = "Front", BackSameAsFront = false, BackText = "Back side", Faces = TextFaces.Both, TextSize = 14 });
        T.Closed("both plate", both.Plate); T.Closed("both letters front", both.LettersFront); T.Closed("both letters back", both.LettersBack);

        var eng = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", FrontText = "Hello", Faces = TextFaces.Back, Mode = TextMode.Engrave, TextSize = 14 });
        T.Closed("engrave back plate", eng.Plate);
        T.Check(eng.LettersBack == null && eng.LettersFront == null, "engrave has no loose letters");

        var emb = PlateBuilder.Build(new PlateSettings { FontFamily = "Times New Roman", FrontText = "Embossed @ 123", Faces = TextFaces.Front, Mode = TextMode.Emboss, TextSize = 14 });
        T.Closed("emboss plate", emb.Plate);
        T.Check(Math.Abs(emb.Plate.Bounds().Max.Z - (2.975 + 1.5)) < 1e-3, "emboss raises text by the text height");

        var misc = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", FrontText = "A\nBB\nCCC", ChamferEnds = true, Chamfer = 0.8, AutoFit = true, HAlign = HAlign.Right, VAlign = VAlign.Top, LineAlign = HAlign.Right, Bold = true, Italic = true });
        T.Closed("misc plate", misc.Plate); T.Closed("misc letters", misc.LettersFront);

        // per-character formatting: mixed fonts/sizes/styles in one line
        var mixed = new PlateSettings { FontFamily = "Arial", TextSize = 10 };
        mixed.FrontRuns = new()
        {
            new TextRun { Text = "Big", Size = 16, Bold = true }, new TextRun { Text = " and " },
            new TextRun { Text = "small\n", Size = 6, FontFamily = "Times New Roman", Italic = true },
            new TextRun { Text = "second line", FontFamily = "Courier New" },
        };
        var mx2 = PlateBuilder.Build(mixed);
        T.Closed("mixed-format plate", mx2.Plate); T.Closed("mixed-format letters", mx2.LettersFront);
        var plainSame = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", TextSize = 10, FrontText = "Big and small\nsecond line" });
        T.Check(Math.Abs(mx2.LettersFront!.Volume() - plainSame.LettersFront!.Volume()) > 5, "per-run formatting changes the letters");
        T.Check(PlateSettings.FromJson(mixed.ToJson()).FrontRuns.Count == 4, "runs survive JSON");

        // a blank line adds space (it used to have height 0)
        double H(string text) { var o = TextOutline.Create(new PlateSettings { FontFamily = "Arial" }, new List<TextRun> { new() { Text = text } }, 10); var b = Extrusion.Bounds(o); return b.MaxY - b.MinY; }
        T.Check(H("A\n\nB") > H("A\nB") * 1.3, $"a blank line adds a line of space ({H("A\nB"):0.#} -> {H("A\n\nB"):0.#} mm)");

        // a superseded build stops instead of running to the end
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            T.Check(PlateBuilder.TryBuild(d, cts.Token) == null, "a cancelled build stops early without throwing");
        }

        T.Section("Fonts");
        var fonts = TextOutline.InstalledFonts();
        T.Check(fonts.Contains("Arial") && fonts.Contains("Arial Black") && fonts.Contains("Segoe UI Light")
                && (!File.Exists(@"C:\Windows\Fonts\ARLRDBD.TTF") || fonts.Contains("Arial Rounded MT Bold")),
            $"font list has Word's names incl. weights ({fonts.Count} fonts)");
        T.Check(!fonts.Any(f => f.StartsWith("Global ")), "WPF's composite 'Global ...' entries are not listed");
        var dupes = fonts.GroupBy(n =>
            {
                TextOutline.Resolve(new System.Windows.Media.FontFamily(n), false, false).TryGetGlyphTypeface(out var g);
                return g?.FontUri.ToString() ?? "none:" + n;
            })
            .Where(g => g.Count() > 1).Select(g => string.Join("/", g)).ToList();
        T.Check(dupes.Count == 0, "every listed font draws its own font file (no look-alike entries): " + string.Join("; ", dupes.Take(5)));
        double V(string fam, bool bold = false, bool italic = false) =>
            PlateBuilder.Build(new PlateSettings { FontFamily = fam, Bold = bold, Italic = italic, FrontText = "Hamburg" }).LettersFront!.Volume();
        T.Check(V("Arial Black") > V("Arial", bold: true) * 1.05, $"'Arial Black' draws the Black face ({V("Arial Black"):0} vs bold Arial {V("Arial", bold: true):0} mm3)");
        T.Check(V("Segoe UI Light") < V("Segoe UI") * 0.95, "'Segoe UI Light' draws the Light face");
        if (fonts.Contains("Arial Rounded MT Bold"))
        {
            var up = TextOutline.Create(new PlateSettings { FontFamily = "Arial Rounded MT Bold" }, new List<TextRun> { new() { Text = "I" } }, 10);
            var it = TextOutline.Create(new PlateSettings { FontFamily = "Arial Rounded MT Bold", Italic = true }, new List<TextRun> { new() { Text = "I" } }, 10);
            double W(PathsD p) { var b = Extrusion.Bounds(p); return b.MaxX - b.MinX; }
            T.Check(W(it) > W(up) * 1.3, $"Italic slants the default font 'Arial Rounded MT Bold' (width of 'I' {W(up):0.##} -> {W(it):0.##} mm)");
        }

        T.Section("Files");
        var tmp = Path.Combine(Path.GetTempPath(), "nameplate_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var files = Exporters.ExportAll(both, Path.Combine(tmp, "t"), true, true, true);
            T.Check(files.Count == 6, "export writes 3mf + plate/letters/holder stl + json: " + string.Join(", ", files.Select(Path.GetFileName)));
            var fromJson = Exporters.LoadProject(Path.Combine(tmp, "t" + Exporters.ProjectExtension));
            var from3mf = Exporters.LoadProject(Path.Combine(tmp, "t.3mf"));
            T.Check(fromJson.ToJson() == both.Settings.ToJson() && from3mf.ToJson() == both.Settings.ToJson(), "settings round trip via JSON and 3MF");
            var stl = Exporters.ReadStl(Path.Combine(tmp, "t_plate.stl"));
            T.Check(stl.CountOpenEdges() == 0 && Math.Abs(stl.Volume() - both.Plate.Volume()) < 1, "STL re-read is closed with same volume");
            T.Check(Exporters.Check3Mf(Path.Combine(tmp, "t.3mf"), out var why), "3MF structure is valid " + why);
        }
        finally { Directory.Delete(tmp, true); }
    }
}
