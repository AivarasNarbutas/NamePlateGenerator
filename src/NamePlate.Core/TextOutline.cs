using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Clipper2Lib;

namespace NamePlate.Core;

/// <summary>Turns text + font into 2D polygons (mm, Y up) using WPF's glyph geometry.</summary>
public static class TextOutline
{
    private const double Em = 100; // working em size in WPF units; scaled to mm afterwards
    public const double MinLineSpacing = 0.3;

    /// <summary>Curve flattening tolerance in mm (well below what a printer resolves).</summary>
    public const double FlattenToleranceMm = 0.01;

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>WPF family names ("Arial", "Arial Rounded MT"); one family can hold several weights.</summary>
    private static readonly Lazy<HashSet<string>> InstalledSet = new(() =>
        new(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<IReadOnlyList<string>> FontList = new(BuildFontList);

    /// <summary>
    /// The font names as Word lists them, one per Windows font name: "Arial", "Arial Black", "Arial Narrow",
    /// "Arial Rounded MT Bold", "Segoe UI Light" ... (WPF's own list merges these into families and so hides
    /// most weights). Only names that WPF draws with their own face are listed; WPF's composite "Global ..."
    /// fonts and the named instances of variable fonts (WPF can only draw their default) are left out.
    /// Takes a few seconds the first time: load it off the UI thread.
    /// </summary>
    public static IReadOnlyList<string> InstalledFonts() => FontList.Value;

    private static IReadOnlyList<string> BuildFontList() =>
        Index.Value.Keys.Where(n => Face(n) is { Faithful: true })
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string Name(IDictionary<CultureInfo, string> names) => names.TryGetValue(En, out var n) ? n : names.Values.First();

    private sealed record FaceInfo(FontFamily Family, FontWeight Weight, FontStretch Stretch, bool Faithful);

    /// <summary>Every installed face by its Windows font name (built from WPF's own face list, ~3 s once).</summary>
    private static readonly Lazy<Dictionary<string, FaceInfo>> Index = new(BuildIndex);

    private static Dictionary<string, FaceInfo> BuildIndex()
    {
        var faces = new List<(string Name, Typeface Tf, GlyphTypeface Gt)>();
        foreach (var tf in Fonts.SystemTypefaces)
        {
            if (tf.IsBoldSimulated || tf.IsObliqueSimulated) continue;
            if (!tf.TryGetGlyphTypeface(out var gt) || gt.Win32FamilyNames.Count == 0) continue;
            faces.Add((Name(gt.Win32FamilyNames), tf, gt));
        }
        // A file that answers to several names is a variable font: WPF draws only its default instance, so
        // "Bahnschrift Light" would look exactly like "Bahnschrift". Keep one name per such file (the shortest).
        var keep = faces.GroupBy(f => f.Gt.FontUri)
            .SelectMany(g => g.Select(f => f.Name).Distinct().Count() == 1 ? g.Select(f => f.Name)
                                 : new[] { g.Select(f => f.Name).OrderBy(n => n.Length).ThenBy(n => n).First() })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = new Dictionary<string, FaceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in faces.Where(f => keep.Contains(f.Name)).GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            // the upright face closest to regular is what the name stands for
            var (_, tf, _) = g.OrderBy(f => f.Tf.Style == FontStyles.Normal ? 0 : 1)
                              .ThenBy(f => Math.Abs(f.Tf.Weight.ToOpenTypeWeight() - 400)).First();
            index[g.Key] = new FaceInfo(tf.FontFamily, tf.Weight, tf.Stretch, true);
        }
        return index;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FaceInfo?> Faces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>For a Windows font name such as "Arial Black": the WPF family ("Arial") and the weight/stretch of
    /// that face (Black, Normal). Faithful = drawing family+weight+stretch gives exactly that font file.</summary>
    private static FaceInfo? Face(string name) => Faces.GetOrAdd(name, n =>
    {
        // quick path (no index needed): WPF finds the face by its Windows name ...
        if (new Typeface(new FontFamily(n), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var direct)
            && direct.Style == FontStyles.Normal && string.Equals(Name(direct.Win32FamilyNames), n, StringComparison.OrdinalIgnoreCase))
        {
            bool Holds(FontFamily f) =>
                new Typeface(f, FontStyles.Normal, direct.Weight, direct.Stretch).TryGetGlyphTypeface(out var g) && g.FontUri == direct.FontUri;
            // usually the font's own family name is the WPF family ("Arial Black" -> "Arial") ...
            string own = Name(direct.FamilyNames);
            if (InstalledSet.Value.Contains(own) && Holds(new FontFamily(own)))
                return new FaceInfo(new FontFamily(own), direct.Weight, direct.Stretch, true);
            // ... but some fonts name their family like the face ("Arial Rounded MT Bold") while WPF files them under
            // a shorter family ("Arial Rounded MT")
            foreach (var f in Fonts.SystemFontFamilies
                         .Where(f => n.StartsWith(f.Source, StringComparison.OrdinalIgnoreCase) || own.StartsWith(f.Source, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(f => f.Source.Length))
                if (Holds(f)) return new FaceInfo(f, direct.Weight, direct.Stretch, true);
        }
        // ... otherwise ("Berlin Sans FB Demi" resolves to the regular file) look it up in the index
        if (Index.Value.TryGetValue(n, out var info)) return info;
        return direct != null ? new FaceInfo(new FontFamily(n), direct.Weight, direct.Stretch, false) : null;
    });

    /// <summary>
    /// The typeface to draw <paramref name="family"/> with, bold/italic as asked. A Windows name like
    /// "Arial Rounded MT Bold" or "Segoe UI Light" is turned into its WPF family plus that face's weight, so WPF
    /// picks the real bold/italic face when there is one and simulates it otherwise. (Drawn under the Windows
    /// name directly, WPF ignores Bold/Italic for such fonts.) Used for the plate and for the editors.
    /// </summary>
    public static Typeface Resolve(FontFamily family, bool bold, bool italic)
    {
        var style = italic ? FontStyles.Italic : FontStyles.Normal;
        string src = family.Source;
        if (!src.Contains('#') && !InstalledSet.Value.Contains(src) && Face(src) is { Faithful: true } f)
        {
            var weight = bold && f.Weight.ToOpenTypeWeight() < FontWeights.Bold.ToOpenTypeWeight() ? FontWeights.Bold : f.Weight;
            return new Typeface(f.Family, style, weight, f.Stretch);
        }
        return new Typeface(family, style, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
    }

    /// <summary>
    /// The family the on-screen editor should use so it shows the same face as <see cref="Resolve"/> while bold/italic
    /// stay plain flags: a single-weight font named by its Windows name ("Arial Rounded MT Bold") is shown under its
    /// WPF family ("Arial Rounded MT"), where WPF simulates italic; anything else is shown as named.
    /// </summary>
    public static FontFamily DisplayFamily(FontFamily family)
    {
        string src = family.Source;
        if (src.Contains('#') || InstalledSet.Value.Contains(src) || Face(src) is not { Faithful: true } f) return family;
        return f.Family.FamilyTypefaces.Select(t => t.Weight).Distinct().Count() == 1 ? f.Family : family;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Resolvable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the name maps to a real font file. Legacy names such as "Arial Rounded MT Bold"
    /// are not in the family list (WPF calls it "Arial Rounded MT") but still resolve, so they count.</summary>
    public static bool IsInstalled(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;
        if (InstalledSet.Value.Contains(family)) return true;
        return Resolvable.GetOrAdd(family, f =>
            new Typeface(new FontFamily(f), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out _));
    }

    public static bool FontFileUsable(string? file) => !string.IsNullOrWhiteSpace(file) && File.Exists(file);

    /// <summary>Family from a font file when it exists, otherwise the named family (WPF falls back to its default for unknown names).</summary>
    public static FontFamily CreateFontFamily(string? family, string? fontFile)
    {
        if (FontFileUsable(fontFile))
        {
            var full = Path.GetFullPath(fontFile!);
            var gt = new GlyphTypeface(new Uri(full));
            var names = gt.Win32FamilyNames.Count > 0 ? gt.Win32FamilyNames : gt.FamilyNames;
            string name = names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var en) ? en : names.Values.First();
            string folder = Path.GetDirectoryName(full)!.TrimEnd('\\') + "\\";
            return new FontFamily(new Uri(folder), "./#" + name);
        }
        return new FontFamily(string.IsNullOrWhiteSpace(family) ? "Arial" : family);
    }

    public static Typeface CreateTypeface(PlateSettings s) => Resolve(CreateFontFamily(s.FontFamily, s.FontFile), s.Bold, s.Italic);

    /// <summary>Lines aligned among themselves; origin is arbitrary (caller positions the block). Unioned, outer CCW.</summary>
    public static PathsD Create(PlateSettings s, IReadOnlyList<TextRun> runs, double sizeMm)
    {
        var result = new PathsD();
        if (sizeMm <= 0 || runs == null || runs.All(r => string.IsNullOrWhiteSpace(r?.Text))) return result;

        var baseFamily = CreateFontFamily(s.FontFamily, s.FontFile);
        var baseFace = Resolve(baseFamily, s.Bold, s.Italic);
        double k = sizeMm / Em;
        double tolUnits = FlattenToleranceMm / k;
        double spacing = Math.Max(MinLineSpacing, s.LineSpacing);

        // split the runs into lines of (text, style) segments
        var lines = new List<List<(string Text, TextRun Style)>> { new() };
        foreach (var run in runs)
        {
            var parts = (run.Text ?? "").Replace("\r", "").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add(new());
                if (parts[i].Length > 0) lines[^1].Add((parts[i], run));
            }
        }

        double y = 0;
        foreach (var line in lines)
        {
            string text = string.Concat(line.Select(l => l.Text));
            // an empty FormattedText has height 0: measure a space so blank lines keep their spacing
            var ft = new FormattedText(text.Length == 0 ? " " : text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                baseFace, Em, Brushes.Black, 1.0);
            int pos = 0;
            foreach (var (t, st) in line)
            {
                if (!string.IsNullOrEmpty(st.FontFamily) || st.Bold != null || st.Italic != null)
                {
                    var family = string.IsNullOrEmpty(st.FontFamily) ? baseFamily : new FontFamily(st.FontFamily);
                    ft.SetFontTypeface(Resolve(family, st.Bold ?? s.Bold, st.Italic ?? s.Italic), pos, t.Length);
                }
                if (st.Size is > 0 && Math.Abs(st.Size.Value - s.TextSize) > 1e-9)
                    ft.SetFontSize(Em * st.Size.Value / s.TextSize, pos, t.Length);
                pos += t.Length;
            }
            double lineY = y;
            y += ft.Height * spacing;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var contours = Flatten(ft.BuildGeometry(new Point(0, 0)), tolUnits);
            if (contours.Count == 0) continue;
            double minX = contours.Min(c => c.Min(p => p.X)), maxX = contours.Max(c => c.Max(p => p.X));
            double dx = s.LineAlign switch { HAlign.Left => -minX, HAlign.Right => -maxX, _ => -(minX + maxX) / 2 };

            foreach (var c in contours)
                result.Add(new PathD(c.Select(p => new PointD((p.X + dx) * k, -(p.Y + lineY) * k))));
        }

        return result.Count == 0 ? result
            : Clipper.Union(result, new PathsD(), Clipper2Lib.FillRule.NonZero, Extrusion.Precision);
    }

    private static List<List<Point>> Flatten(Geometry g, double tolerance)
    {
        var res = new List<List<Point>>();
        var pg = g.GetFlattenedPathGeometry(tolerance, ToleranceType.Absolute);
        foreach (var fig in pg.Figures)
        {
            var pts = new List<Point> { fig.StartPoint };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case PolyLineSegment pl: pts.AddRange(pl.Points); break;
                    case LineSegment ls: pts.Add(ls.Point); break;
                }
            }
            if (pts.Count >= 3) res.Add(pts);
        }
        return res;
    }
}
