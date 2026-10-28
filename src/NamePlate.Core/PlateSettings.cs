using System.Text.Json;
using System.Text.Json.Serialization;

namespace NamePlate.Core;

public enum TextMode
{
    /// <summary>Pocket in the plate plus separate letter bodies that are pressed in (the reference model's method).</summary>
    Inlay,
    /// <summary>Pocket in the plate only.</summary>
    Engrave,
    /// <summary>Letters raised from the plate surface, one body.</summary>
    Emboss,
}

/// <summary>A piece of text with optional overrides of the default font settings (null = inherit).</summary>
public sealed class TextRun
{
    public string Text { get; set; } = "";
    public string? FontFamily { get; set; }
    /// <summary>Font em height in mm.</summary>
    public double? Size { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
}

public enum TextFaces { Front, Back, Both }
public enum HAlign { Left, Center, Right }
public enum VAlign { Top, Middle, Bottom }

/// <summary>All user-editable parameters. Units: mm. Serialised as the project file and embedded in the 3MF.</summary>
public sealed class PlateSettings
{
    // ---- plate (defaults taken from the reference blankplate.step) ----
    public double Length { get; set; } = 175;
    public double Height { get; set; } = 40;
    public double Thickness { get; set; } = 2.975;
    /// <summary>45° chamfer on the long edges (0 = none).</summary>
    public double Chamfer { get; set; } = 0.5;
    /// <summary>Also chamfer the two short ends (the reference plate does not).</summary>
    public bool ChamferEnds { get; set; } = false;

    // ---- text ----
    /// <summary>Formatted text: runs may contain '\n' for line breaks. Unset run properties inherit the settings below.</summary>
    public List<TextRun> FrontRuns { get; set; } = new() { new TextRun { Text = "Your Name\nJob title" } };
    public List<TextRun> BackRuns { get; set; } = new();

    /// <summary>Plain-text convenience (setting it drops the formatting).</summary>
    [JsonIgnore]
    public string FrontText { get => string.Concat(FrontRuns.Select(r => r.Text)); set => FrontRuns = new() { new TextRun { Text = value } }; }
    [JsonIgnore]
    public string BackText { get => string.Concat(BackRuns.Select(r => r.Text)); set => BackRuns = new() { new TextRun { Text = value } }; }
    public bool BackSameAsFront { get; set; } = true;

    public string FontFamily { get; set; } = "Arial Rounded MT Bold";
    /// <summary>Optional .ttf/.otf file; overrides <see cref="FontFamily"/> when it exists.</summary>
    public string FontFile { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    /// <summary>Font em height in mm (cap height is roughly 70 % of it).</summary>
    public double TextSize { get; set; } = 12;
    /// <summary>Scale the text so it just fits inside the margins.</summary>
    public bool AutoFit { get; set; }
    public double LineSpacing { get; set; } = 1.0;

    // ---- placement ----
    public HAlign HAlign { get; set; } = HAlign.Center;
    public VAlign VAlign { get; set; } = VAlign.Middle;
    /// <summary>Alignment of the lines relative to each other.</summary>
    public HAlign LineAlign { get; set; } = HAlign.Center;
    /// <summary>Minimum distance between text and plate edge.</summary>
    public double Margin { get; set; } = 2;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    // ---- faces / depth ----
    public TextFaces Faces { get; set; } = TextFaces.Front;
    public TextMode Mode { get; set; } = TextMode.Inlay;
    /// <summary>Inlay/Engrave: pocket depth into the plate.</summary>
    public double PocketDepth { get; set; } = 1.5;
    /// <summary>Inlay: height of the letter bodies. Emboss: height above the surface.</summary>
    public double TextHeight { get; set; } = 1.5;
    /// <summary>Inlay: letters are inset by this much on every side for a press fit (reference: 0.08).</summary>
    public double Clearance { get; set; } = 0.08;
    /// <summary>Gap between objects in the print layout.</summary>
    public double PrintGap { get; set; } = 5;

    // ---- holder (stand) ----
    public HolderSettings Holder { get; set; } = new();

    public PlateSettings Clone() => JsonSerializer.Deserialize<PlateSettings>(ToJson(), Options)!.Normalize();

    /// <summary>Replaces nulls and non-finite numbers (NaN, ∞) with defaults. Range limits are applied by the builder, with warnings.</summary>
    public PlateSettings Normalize()
    {
        var d = new PlateSettings();
        static double F(double v, double fallback) => double.IsFinite(v) ? v : fallback;
        Length = F(Length, d.Length); Height = F(Height, d.Height); Thickness = F(Thickness, d.Thickness);
        Chamfer = F(Chamfer, d.Chamfer); TextSize = F(TextSize, d.TextSize); LineSpacing = F(LineSpacing, d.LineSpacing);
        Margin = F(Margin, d.Margin); OffsetX = F(OffsetX, 0); OffsetY = F(OffsetY, 0);
        PocketDepth = F(PocketDepth, d.PocketDepth); TextHeight = F(TextHeight, d.TextHeight);
        Clearance = F(Clearance, d.Clearance); PrintGap = F(PrintGap, d.PrintGap);
        FontFamily ??= d.FontFamily; FontFile ??= "";
        FrontRuns = Fix(FrontRuns); BackRuns = Fix(BackRuns);
        Holder ??= new HolderSettings();
        Holder.Clearance = F(Holder.Clearance, d.Holder.Clearance);
        Holder.ExtraLength = F(Holder.ExtraLength, 0);
        Holder.Points = (Holder.Points ?? d.Holder.Points).Where(p => p != null).ToList();
        foreach (var p in Holder.Points) { p.X = F(p.X, 0); p.Y = F(p.Y, 0); }
        return this;

        static List<TextRun> Fix(List<TextRun>? runs)
        {
            var r = (runs ?? new()).Where(x => x != null).ToList();
            foreach (var x in r)
            {
                x.Text ??= "";
                if (x.Size is double s && (!double.IsFinite(s) || s <= 0)) x.Size = null;
            }
            return r;
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static PlateSettings FromJson(string json) =>
        (JsonSerializer.Deserialize<PlateSettings>(json, Options) ?? throw new InvalidDataException("Empty project file.")).Normalize();
}
