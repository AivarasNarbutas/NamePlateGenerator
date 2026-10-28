using System.Text.Json;
using NamePlate.Core;

namespace NamePlateApp.Services;

/// <summary>The font settings a user saved as "my default"; new plates and Reset start from them.</summary>
public sealed record FontDefaults(string FontFamily, string FontFile, double TextSize, bool Bold, bool Italic, double LineSpacing)
{
    public static FontDefaults From(PlateSettings s) => new(s.FontFamily, s.FontFile, s.TextSize, s.Bold, s.Italic, s.LineSpacing);

    public void ApplyTo(PlateSettings s)
    {
        if (!string.IsNullOrWhiteSpace(FontFamily)) s.FontFamily = FontFamily;
        s.FontFile = FontFile ?? "";
        if (double.IsFinite(TextSize) && TextSize > 0) s.TextSize = TextSize;
        s.Bold = Bold;
        s.Italic = Italic;
        if (double.IsFinite(LineSpacing) && LineSpacing > 0) s.LineSpacing = LineSpacing;
    }
}

public interface IUserDefaultsStore
{
    FontDefaults? LoadFonts();
    void SaveFonts(FontDefaults defaults);
}

/// <summary>Stores the defaults in %APPDATA%\NamePlateGenerator\defaults.json.</summary>
public sealed class FileUserDefaultsStore : IUserDefaultsStore
{
    private readonly string _path;

    public FileUserDefaultsStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NamePlateGenerator", "defaults.json");

    public FontDefaults? LoadFonts()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<FontDefaults>(File.ReadAllText(_path)) : null; }
        catch (Exception) { return null; } // a damaged preferences file must not stop the program; built-in defaults apply
    }

    public void SaveFonts(FontDefaults defaults)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
    }
}
