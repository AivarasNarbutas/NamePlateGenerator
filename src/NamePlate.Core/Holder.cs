using Clipper2Lib;

namespace NamePlate.Core;

/// <summary>Which plate face a profile point's X is measured from (X runs through the plate thickness, front to back).</summary>
public enum XAnchor { Front, Back }

/// <summary>Which plate edge a profile point's Y is measured from (Y runs along the plate height).</summary>
public enum YAnchor { Bottom, Top }

/// <summary>
/// One corner of the holder cross-section, stored relative to the plate so the profile follows plate changes:
/// a point "2 mm behind the back face" stays 2 mm behind it when the plate gets thicker.
/// </summary>
public sealed class HolderPoint
{
    public XAnchor XFrom { get; set; }
    public double X { get; set; }
    public YAnchor YFrom { get; set; }
    public double Y { get; set; }

    public HolderPoint() { }
    public HolderPoint(XAnchor xFrom, double x, YAnchor yFrom, double y) { XFrom = xFrom; X = x; YFrom = yFrom; Y = y; }

    public HolderPoint Copy() => new(XFrom, X, YFrom, Y);
}

public enum HolderTemplate
{
    /// <summary>The reference stand.stl: the plate slides in from the side, lips hold its edges, a leg leans it back.</summary>
    ChannelStand,
    /// <summary>A low block with a slot; the plate stands upright in it.</summary>
    SlotBase,
}

public sealed class HolderSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Gap between plate and holder on every side of the slot.</summary>
    public double Clearance { get; set; } = 0.025;
    /// <summary>Holder length = plate length + this (negative: shorter than the plate).</summary>
    public double ExtraLength { get; set; }
    public List<HolderPoint> Points { get; set; } = HolderTemplates.Create(HolderTemplate.ChannelStand);
}

public static class HolderTemplates
{
    public static List<HolderPoint> Create(HolderTemplate t)
    {
        HolderPoint P(XAnchor xa, double x, YAnchor ya, double y) => new(xa, x, ya, y);
        const XAnchor F = XAnchor.Front, B = XAnchor.Back;
        const YAnchor Bot = YAnchor.Bottom, Top = YAnchor.Top;
        return t switch
        {
            // traced from the reference stand.stl cross-section (counter-clockwise)
            HolderTemplate.ChannelStand => new()
            {
                P(F, -2, Bot, -3), P(B, 2, Bot, -3), P(B, 26, Bot, 21), P(B, 22, Bot, 25), P(B, 5, Bot, 8),
                P(B, 5, Top, 3), P(F, -2, Top, 3), P(F, -2, Top, -2), P(F, 0, Top, -2), P(F, 0, Top, 0),
                P(B, 0, Top, 0), P(B, 0, Bot, 0), P(F, 0, Bot, 0), P(F, 0, Bot, 2), P(F, -2, Bot, 2),
            },
            HolderTemplate.SlotBase => new()
            {
                P(F, -12, Bot, -4), P(B, 12, Bot, -4), P(B, 12, Bot, 3), P(B, 2, Bot, 10), P(B, 0, Bot, 10),
                P(B, 0, Bot, 0), P(F, 0, Bot, 0), P(F, 0, Bot, 10), P(F, -2, Bot, 10), P(F, -12, Bot, 3),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(t)),
        };
    }
}

/// <summary>Where the anchors are for a given plate: the slot walls (plate surface plus clearance).</summary>
public readonly record struct HolderFrame(double Front, double Back, double Bottom, double Top)
{
    public static HolderFrame For(PlateSettings s)
    {
        var (_, h, t) = PlateBuilder.EffectiveSize(s);
        double c = Math.Clamp(double.IsFinite(s.Holder?.Clearance ?? 0) ? s.Holder!.Clearance : 0, 0, PlateBuilder.MaxHolderClearance);
        return new HolderFrame(-c, t + c, -c, h + c);
    }

    public double XOf(XAnchor a) => a == XAnchor.Front ? Front : Back;
    public double YOf(YAnchor a) => a == YAnchor.Bottom ? Bottom : Top;
    public (double X, double Y) Resolve(HolderPoint p) => (XOf(p.XFrom) + p.X, YOf(p.YFrom) + p.Y);
}

public sealed class HolderModel
{
    /// <summary>Standing on its end: cross-section on the bed, extruded upwards (prints without supports).</summary>
    public TriMesh Print { get; init; } = new();
    /// <summary>Around the plate, in plate coordinates (for the preview).</summary>
    public TriMesh Assembled { get; init; } = new();
    /// <summary>Resolved cross-section corners: X through the thickness (0 = front face), Y along the height.</summary>
    public IReadOnlyList<(double X, double Y)> Outline { get; init; } = Array.Empty<(double, double)>();
    public double Length { get; init; }
}

public static class HolderBuilder
{
    /// <summary>Builds the holder for a plate of length <paramref name="L"/>, height <paramref name="H"/>, thickness <paramref name="T"/>.</summary>
    public static HolderModel? Build(PlateSettings s, double L, double H, double T, List<string> warn)
    {
        var hs = s.Holder;
        if (hs.Points.Count < 3) { warn.Add("The holder needs at least 3 profile points."); return null; }
        if (hs.Clearance < 0) warn.Add("A negative holder clearance would squeeze the plate; 0 used.");
        if (hs.Clearance > PlateBuilder.MaxHolderClearance) warn.Add($"Holder clearance limited to {PlateBuilder.MaxHolderClearance} mm.");

        var frame = HolderFrame.For(s);
        var outline = hs.Points.Select(frame.Resolve).ToList();
        var poly = new PathD(outline.Select(p => new PointD(p.X, p.Y)));
        double signed = Clipper.Area(poly);
        if (Math.Abs(signed) < 1) { warn.Add("The holder outline encloses no area."); return null; }

        var region = Clipper.Union(new PathsD { poly }, new PathsD(), FillRule.NonZero, Extrusion.Precision);
        if (region.Count != 1 || Math.Abs(Extrusion.Area(region) - Math.Abs(signed)) > Math.Abs(signed) * 0.005)
            warn.Add("The holder outline crosses itself. Check the order of the points.");

        var plateSection = Extrusion.Rect(0, 0, T, H);
        double overlap = Extrusion.Area(Clipper.Intersect(region, plateSection, FillRule.NonZero, Extrusion.Precision));
        if (overlap > 0.01)
            warn.Add($"The holder profile overlaps the plate ({overlap:0.##} mm² of the cross-section); the plate will not fit.");

        double len = L + hs.ExtraLength;
        if (len < 1) { len = 1; warn.Add("Holder length raised to the minimum of 1 mm."); }

        region = Extrusion.Clean(region);
        var profile = new TriMesh(); // x = through the thickness, y = along the height, z = along the length
        profile.Cap(region, 0, false);
        profile.Cap(region, len, true);
        profile.Walls(region, 0, len, true);

        var b = Extrusion.Bounds(region);
        double x0 = (L - len) / 2;
        return new HolderModel
        {
            Print = profile.Transformed(p => new V3(p.X - b.MinX, p.Y - b.MinY, p.Z)),
            // proper rotation (det +1): length -> plate X, height -> plate Y, front face (x = 0) -> plate top (z = T)
            Assembled = profile.Transformed(p => new V3(p.Z + x0, p.Y, T - p.X)),
            Outline = outline,
            Length = len,
        };
    }

    /// <summary>Front-face text that sits behind the holder's lips (seen from the front) gets a warning.</summary>
    public static void CheckCoveredText(HolderModel holder, PathsD frontText, PathsD backTextMirrored, double L, double H, double T, List<string> warn)
    {
        var region = new PathsD { new PathD(holder.Outline.Select(p => new PointD(p.X, p.Y))) };
        region = Clipper.Union(region, new PathsD(), FillRule.NonZero, Extrusion.Precision);

        void Check(PathsD text, PathsD side, string face)
        {
            if (text.Count == 0) return;
            var tb = Extrusion.Bounds(text);
            foreach (var piece in Clipper.Intersect(region, side, FillRule.NonZero, Extrusion.Precision))
            {
                var pb = Extrusion.Bounds(new PathsD { piece });
                double lo = Math.Max(pb.MinY, tb.MinY), hi = Math.Min(pb.MaxY, tb.MaxY);
                if (hi - lo > 0.05)
                {
                    warn.Add($"The holder hides part of the {face} text (height {lo:0.#}-{hi:0.#} mm). Increase the edge margin or move the text.");
                    return;
                }
            }
        }

        // material in front of the front face / behind the back face, within the plate height
        Check(frontText, Extrusion.Rect(-1000, 0, 0, H), "front");
        Check(backTextMirrored, Extrusion.Rect(T, 0, T + 1000, H), "back");
    }
}
