using Clipper2Lib;

namespace NamePlate.Core;

public sealed class PlateModel
{
    /// <summary>Plate with pockets (or raised text when Mode = Emboss), lying flat, front face up.</summary>
    public TriMesh Plate { get; init; } = new();
    /// <summary>Loose letter bodies in print orientation (flat on the bed, visible side up). Null when not applicable.</summary>
    public TriMesh? LettersFront { get; init; }
    public TriMesh? LettersBack { get; init; }
    /// <summary>The same letters sitting in their pockets (for the preview).</summary>
    public TriMesh? LettersFrontAssembled { get; init; }
    public TriMesh? LettersBackAssembled { get; init; }
    public List<string> Warnings { get; init; } = new();
    public double EffectiveTextSize { get; init; }
    public PlateSettings Settings { get; init; } = new();
    /// <summary>The stand, or null when it is switched off or its profile is unusable.</summary>
    public HolderModel? Holder { get; init; }
}

public static class PlateBuilder
{
    public const double MinFloor = 0.4;
    /// <summary>Minimum material between a pocket and the plate edge (measured from where the chamfer starts).</summary>
    public const double MinWall = 0.4;
    public const double MaxHolderClearance = 1.0;
    private const double MinLength = 5, MaxLength = 1000, MinHeight = 5, MaxHeight = 1000, MinThickness = 0.8, MaxThickness = 50;

    /// <summary>Plate length, height and thickness after the range limits the builder applies.</summary>
    public static (double L, double H, double T) EffectiveSize(PlateSettings s) =>
        (Lim(s.Length, MinLength, MaxLength), Lim(s.Height, MinHeight, MaxHeight), Lim(s.Thickness, MinThickness, MaxThickness));

    private static double Lim(double v, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : min;

    public static PlateModel Build(PlateSettings input) => BuildCore(input, CancellationToken.None)!;

    /// <summary>Like <see cref="Build(PlateSettings)"/>, but returns null as soon as <paramref name="ct"/> is cancelled.
    /// (Not an exception: superseded previews are cancelled on almost every keystroke, and a debugger would stop on each.)</summary>
    public static PlateModel? TryBuild(PlateSettings input, CancellationToken ct) => BuildCore(input, ct);

    private static PlateModel? BuildCore(PlateSettings input, CancellationToken ct)
    {
        var s = input.Clone();
        var warn = new List<string>();

        double Clamp(double v, double min, double max, string what)
        {
            if (v < min) { warn.Add($"{what} raised to the minimum of {min:0.##} mm."); return min; }
            if (v > max) { warn.Add($"{what} lowered to the maximum of {max:0.##} mm."); return max; }
            return v;
        }

        double L = Clamp(s.Length, MinLength, MaxLength, "Length");
        double H = Clamp(s.Height, MinHeight, MaxHeight, "Height");
        double T = Clamp(s.Thickness, MinThickness, MaxThickness, "Thickness");
        double c = Math.Max(0, s.Chamfer);
        double cMax = Math.Min(T / 2 - 0.05, Math.Min(H, L) / 4);
        if (c > cMax) { c = cMax; warn.Add($"Chamfer reduced to {c:0.##} mm (plate too thin)."); }
        double h = Clamp(s.TextHeight, 0.1, 20, "Text height");
        double clearance = s.Clearance;
        if (clearance < 0) { clearance = 0; warn.Add("A negative fit clearance would make the letters bigger than their pockets; 0 used."); }

        CheckFonts(s, warn);
        if (s.LineSpacing < TextOutline.MinLineSpacing)
            warn.Add($"Line spacing raised to {TextOutline.MinLineSpacing}.");

        bool useFront = s.Faces is TextFaces.Front or TextFaces.Both;
        bool useBack = s.Faces is TextFaces.Back or TextFaces.Both;
        var frontRuns = s.FrontRuns;
        var backRuns = s.BackSameAsFront ? s.FrontRuns : s.BackRuns;
        // keep a wall between pockets and the edge, and never let text touch the outline (non-manifold mesh)
        double margin = Math.Max(s.Margin, c + MinWall);
        double availW = L - 2 * margin, availH = H - 2 * margin;
        if (availW < 1 || availH < 1)
        {
            warn.Add("The edge margin leaves no room for text.");
            useFront = useBack = false;
        }

        // ---- text outlines at the requested size, then optional auto-fit ----
        var frontOutline = useFront ? TextOutline.Create(s, frontRuns, s.TextSize) : new PathsD();
        var backOutline = useBack ? TextOutline.Create(s, backRuns, s.TextSize) : new PathsD();
        if (s.TextSize <= 0 && (useFront || useBack)) warn.Add("Text size must be greater than 0.");
        double scale = 1;
        if (s.AutoFit && s.TextSize > 0 && (useFront || useBack))
        {
            scale = double.MaxValue;
            foreach (var o in new[] { frontOutline, backOutline })
            {
                if (o.Count == 0) continue;
                var b = Extrusion.Bounds(o);
                double w = b.MaxX - b.MinX, hh = b.MaxY - b.MinY;
                if (w > 0) scale = Math.Min(scale, availW / w);
                if (hh > 0) scale = Math.Min(scale, availH / hh);
            }
            if (scale == double.MaxValue) scale = 1;
            // a single dash or dot would otherwise be blown up to plate height
            scale = Math.Min(scale, availH / s.TextSize);
        }

        var allowed = Extrusion.Rect(margin, margin, L - margin, H - margin);
        PathsD Place(PathsD outline, string face)
        {
            if (outline.Count == 0) return outline;
            outline = Extrusion.Scale(outline, scale);
            var b = Extrusion.Bounds(outline);
            double x = s.HAlign switch
            {
                HAlign.Left => margin + s.OffsetX - b.MinX,
                HAlign.Right => L - margin + s.OffsetX - b.MaxX,
                _ => L / 2 + s.OffsetX - (b.MinX + b.MaxX) / 2,
            };
            double y = s.VAlign switch
            {
                VAlign.Top => H - margin + s.OffsetY - b.MaxY,
                VAlign.Bottom => margin + s.OffsetY - b.MinY,
                _ => H / 2 + s.OffsetY - (b.MinY + b.MaxY) / 2,
            };
            var placed = Extrusion.Translate(outline, x, y);
            var clipped = Extrusion.Clean(Clipper.Intersect(placed, allowed, FillRule.NonZero, Extrusion.Precision));
            if (Extrusion.Area(clipped) < Extrusion.Area(placed) * 0.995)
                warn.Add(clipped.Count == 0
                    ? $"{face} text lies completely outside the usable area of the plate."
                    : $"{face} text does not fit inside the plate and was clipped. Reduce the size, enable auto-fit or enlarge the plate.");
            return clipped;
        }

        // Regions in "reading" orientation (as seen looking at that face). Back is mirrored into plate coordinates below.
        if (ct.IsCancellationRequested) return null;
        var frontRead = Place(frontOutline, "Front");
        var backRead = Place(backOutline, "Back");
        var frontPlate = frontRead;
        var backPlate = Extrusion.MirrorX(backRead, L);

        // ---- depths ----
        bool pocket = s.Mode != TextMode.Emboss;
        double dF = 0, dB = 0;
        if (pocket && (frontRead.Count > 0 || backRead.Count > 0))
        {
            double d = s.PocketDepth;
            bool both = frontRead.Count > 0 && backRead.Count > 0;
            double allow = (T - MinFloor) / (both ? 2 : 1);
            if (d < 0.1) { d = 0.1; warn.Add("Pocket depth raised to the minimum of 0.1 mm."); }
            if (d > allow)
            {
                d = allow;
                // letters meant to fill the pocket should shrink with it, not suddenly stand proud
                bool lettersFollow = s.Mode == TextMode.Inlay && h > d && h <= s.PocketDepth + 1e-9;
                if (lettersFollow) h = d;
                warn.Add($"Pocket depth limited to {d:0.##} mm to keep a {MinFloor} mm floor" +
                         (both ? " between the two text faces" : "") + (lettersFollow ? "; the letters were made as tall as the pocket." : "."));
            }
            dF = frontRead.Count > 0 ? d : 0;
            dB = backRead.Count > 0 ? d : 0;
        }
        if (!pocket && backPlate.Count > 0)
            warn.Add("Raised text on the back: the plate cannot lie flat on the bed. Print it on supports or use Inlay/Engrave for the back.");

        if (ct.IsCancellationRequested) return null;
        // ---- plate shell: chamfered box built from rings ----
        var plate = new TriMesh();
        double ix = s.ChamferEnds ? c : 0;
        var rings = c > 0
            ? new[] { (z: 0.0, ix, iy: c), (z: c, ix: 0.0, iy: 0.0), (z: T - c, ix: 0.0, iy: 0.0), (z: T, ix, iy: c) }
            : new[] { (z: 0.0, ix: 0.0, iy: 0.0), (z: T, ix: 0.0, iy: 0.0) };
        PathD Ring((double z, double ix, double iy) r) => Extrusion.Rect(r.ix, r.iy, L - r.ix, H - r.iy)[0];
        for (int i = 0; i + 1 < rings.Length; i++)
            plate.Band(Ring(rings[i]), rings[i].z, Ring(rings[i + 1]), rings[i + 1].z);

        var faceRect = c > 0 ? Extrusion.Rect(ix, c, L - ix, H - c) : Extrusion.Rect(0, 0, L, H);

        // Faces are tessellated from the outline rectangle plus the very same text contours the walls use
        // (even-odd fill makes them holes), so every wall edge has a matching face edge.
        plate.Cap(Extrusion.Concat(faceRect, frontPlate), T, true);
        if (frontPlate.Count > 0)
        {
            if (pocket) { plate.Walls(frontPlate, T - dF, T, false); plate.Cap(frontPlate, T - dF, true); }
            else { plate.Walls(frontPlate, T, T + h, true); plate.Cap(frontPlate, T + h, true); }
        }
        plate.Cap(Extrusion.Concat(faceRect, backPlate), 0, false);
        if (backPlate.Count > 0)
        {
            if (pocket) { plate.Walls(backPlate, 0, dB, false); plate.Cap(backPlate, dB, false); }
            else { plate.Walls(backPlate, -h, 0, true); plate.Cap(backPlate, -h, false); }
        }

        if (ct.IsCancellationRequested) return null;
        // ---- loose letters (inlay mode) ----
        TriMesh? lf = null, lb = null, lfa = null, lba = null;
        if (s.Mode == TextMode.Inlay)
        {
            PathsD Inset(PathsD r, string face)
            {
                if (clearance <= 0 || r.Count == 0) return r;
                var res = Extrusion.Clean(Clipper.InflatePaths(r, -clearance, JoinType.Round, EndType.Polygon, 2.0, Extrusion.Precision, 0.01));
                double kept = Extrusion.Area(res) / Extrusion.Area(r);
                if (res.Count == 0) warn.Add($"The fit clearance removes all {face.ToLowerInvariant()} letters. Reduce it.");
                else if (kept < 0.6) warn.Add($"The fit clearance removes {100 * (1 - kept):0}% of the {face.ToLowerInvariant()} letter area; thin strokes may be lost.");
                return res;
            }
            TriMesh Body(PathsD r)
            {
                var m = new TriMesh();
                m.Cap(r, 0, false); m.Cap(r, h, true); m.Walls(r, 0, h, true);
                return m;
            }
            if (frontRead.Count > 0)
            {
                lf = Body(Inset(frontRead, "Front"));
                lfa = lf.Transformed(p => new V3(p.X, p.Y, p.Z + T - dF));
            }
            if (backRead.Count > 0)
            {
                lb = Body(Inset(backRead, "Back"));
                // rotate 180° about the plate's Y axis: reading orientation -> mirrored pocket, visible side down
                lba = lb.Transformed(p => new V3(L - p.X, p.Y, dB - p.Z));
            }
            double deepest = Math.Max(dF, dB);
            if ((lf != null || lb != null) && h > deepest + 1e-9)
                warn.Add($"Letters are {h - deepest:0.##} mm taller than the pocket and will stand proud.");
        }

        if (ct.IsCancellationRequested) return null;
        HolderModel? holder = null;
        if (s.Holder.Enabled)
        {
            holder = HolderBuilder.Build(s, L, H, T, warn);
            if (holder != null) HolderBuilder.CheckCoveredText(holder, frontPlate, backPlate, L, H, T, warn);
        }

        return new PlateModel
        {
            Holder = holder,
            Plate = plate, LettersFront = lf, LettersBack = lb, LettersFrontAssembled = lfa, LettersBackAssembled = lba,
            Settings = s, EffectiveTextSize = s.TextSize * scale, Warnings = warn,
        };
    }

    private static void CheckFonts(PlateSettings s, List<string> warn)
    {
        bool fileOk = TextOutline.FontFileUsable(s.FontFile);
        if (!string.IsNullOrWhiteSpace(s.FontFile) && !fileOk)
            warn.Add($"Font file not found: {s.FontFile}. Using \"{s.FontFamily}\" instead.");
        if (!fileOk && !TextOutline.IsInstalled(s.FontFamily))
            warn.Add($"Font \"{s.FontFamily}\" is not installed on this computer; Windows substitutes a different font.");
        foreach (var f in s.FrontRuns.Concat(s.BackRuns).Select(r => r.FontFamily).Where(f => !string.IsNullOrEmpty(f))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            if (!TextOutline.IsInstalled(f))
                warn.Add($"Font \"{f}\" (used in the formatted text) is not installed; Windows substitutes a different font.");
    }
}
