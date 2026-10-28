using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NamePlate.Core;

namespace NamePlateApp.Converters;

/// <summary>(PlateModel, bool printLayout, bool showHolder) -> the 3D scene shown in the Helix viewport.</summary>
public sealed class PreviewSceneConverter : IMultiValueConverter
{
    private static readonly Material PlateMat = Mat(Color.FromRgb(0xE8, 0xB4, 0x2C));
    private static readonly Material LetterMat = Mat(Color.FromRgb(0x24, 0x26, 0x2B));
    private static readonly Material HolderMat = Mat(Color.FromRgb(0x8F, 0xA3, 0xB8));

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not PlateModel m) return null;
        bool layout = values[1] is true;
        bool holder = values.Length < 3 || values[2] is true;
        var group = new Model3DGroup();
        if (layout)
        {
            // exactly what goes into the 3MF (the holder is there whenever it is enabled, whatever "Show holder" says)
            foreach (var (name, mesh, dx, dy) in Exporters.PrintLayout(m))
            {
                var mat = name == "Plate" ? PlateMat : name == "Holder" ? HolderMat : LetterMat;
                group.Children.Add(ToModel(mesh, mat, dy, dx));
            }
        }
        else
        {
            group.Children.Add(ToModel(m.Plate, PlateMat, 0));
            if (m.LettersFrontAssembled is { IsEmpty: false } f) group.Children.Add(ToModel(f, LetterMat, 0));
            if (m.LettersBackAssembled is { IsEmpty: false } b) group.Children.Add(ToModel(b, LetterMat, 0));
            if (holder && m.Holder is { Assembled.IsEmpty: false } h) group.Children.Add(ToModel(h.Assembled, HolderMat, 0));
        }
        group.Freeze();
        return group;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Material Mat(Color c)
    {
        var m = new MaterialGroup
        {
            Children = { new DiffuseMaterial(new SolidColorBrush(c)), new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 20) },
        };
        m.Freeze();
        return m;
    }

    /// <summary>Flat shading: three vertices per triangle carrying the face normal.</summary>
    public static GeometryModel3D ToModel(TriMesh mesh, Material mat, double dy, double dx = 0)
    {
        var v = mesh.Vertices; var ix = mesh.Indices;
        var pos = new Point3DCollection(ix.Count); var nor = new Vector3DCollection(ix.Count); var tri = new Int32Collection(ix.Count);
        for (int i = 0; i < ix.Count; i += 3)
        {
            var a = v[ix[i]]; var b = v[ix[i + 1]]; var c = v[ix[i + 2]];
            var n = Vector3D.CrossProduct(new Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z), new Vector3D(c.X - a.X, c.Y - a.Y, c.Z - a.Z));
            if (n.LengthSquared == 0) continue; // slivers carry no area; skip them in the preview
            n.Normalize();
            foreach (var p in new[] { a, b, c })
            {
                tri.Add(pos.Count);
                pos.Add(new Point3D(p.X + dx, p.Y + dy, p.Z));
                nor.Add(n);
            }
        }
        var g = new MeshGeometry3D { Positions = pos, Normals = nor, TriangleIndices = tri };
        g.Freeze();
        var model = new GeometryModel3D(g, mat) { BackMaterial = mat };
        model.Freeze();
        return model;
    }
}

/// <summary>(FontFamily name, font file) -> WPF FontFamily, the same resolution the geometry uses.</summary>
public sealed class FontFamilyConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try { return TextOutline.CreateFontFamily(values.ElementAtOrDefault(0) as string, values.ElementAtOrDefault(1) as string); }
        catch { return new FontFamily("Arial"); }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true or a non-empty string -> Visible, anything else -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true || value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
