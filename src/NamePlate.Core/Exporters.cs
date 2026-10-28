using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace NamePlate.Core;

public static class Exporters
{
    public const string SettingsEntry = "Metadata/NamePlateSettings.json";
    public const string ProjectExtension = ".nameplate.json";

    public static void WriteBinaryStl(TriMesh mesh, string path, string label = "NamePlateGenerator")
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        var header = new byte[80];
        Encoding.ASCII.GetBytes(label, 0, Math.Min(label.Length, 80), header, 0);
        w.Write(header);
        w.Write((uint)mesh.TriangleCount);
        var v = mesh.Vertices; var ix = mesh.Indices;
        for (int i = 0; i < ix.Count; i += 3)
        {
            var a = v[ix[i]]; var b = v[ix[i + 1]]; var c = v[ix[i + 2]];
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (len > 0) { nx /= len; ny /= len; nz /= len; }
            w.Write((float)nx); w.Write((float)ny); w.Write((float)nz);
            foreach (var p in new[] { a, b, c }) { w.Write((float)p.X); w.Write((float)p.Y); w.Write((float)p.Z); }
            w.Write((ushort)0);
        }
    }

    public static TriMesh ReadStl(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var mesh = new TriMesh();
        uint n = BitConverter.ToUInt32(bytes, 80);
        if (84 + 50L * n != bytes.Length) throw new InvalidDataException("Only binary STL is supported.");
        for (int i = 0; i < n; i++)
        {
            int o = 84 + 50 * i + 12;
            var id = new int[3];
            for (int k = 0; k < 3; k++)
                id[k] = mesh.AddVertex(BitConverter.ToSingle(bytes, o + k * 12), BitConverter.ToSingle(bytes, o + k * 12 + 4), BitConverter.ToSingle(bytes, o + k * 12 + 8));
            mesh.AddTriangle(id[0], id[1], id[2]);
        }
        return mesh;
    }

    /// <summary>Where each printable object goes on the bed (shared by the 3MF and the preview):
    /// letters in rows below the plate, the holder standing on its end to the right of it.</summary>
    public static List<(string Name, TriMesh Mesh, double Dx, double Dy)> PrintLayout(PlateModel model)
    {
        var objects = new List<(string, TriMesh, double, double)>();
        double gap = Math.Max(0, model.Settings.PrintGap);
        var (pmin, pmax) = model.Plate.Bounds();
        double step = pmax.Y - pmin.Y + gap, row = 0;
        objects.Add(("Plate", model.Plate, 0, 0));
        if (model.LettersFront is { IsEmpty: false } lf) objects.Add(("Letters front", lf, 0, row -= step));
        if (model.LettersBack is { IsEmpty: false } lb) objects.Add(("Letters back", lb, 0, row -= step));
        if (model.Holder is { Print.IsEmpty: false } h) objects.Add(("Holder", h.Print, pmax.X + gap, 0));
        return objects;
    }

    /// <summary>One 3MF with every object laid out for printing; the project settings are embedded so the file can be re-imported.</summary>
    public static void Write3Mf(PlateModel model, string path)
    {
        var objects = PrintLayout(model);

        var inv = CultureInfo.InvariantCulture;
        var ws = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false) };
        using var ms = new MemoryStream();
        using (var x = XmlWriter.Create(ms, ws))
        {
            const string ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
            x.WriteStartDocument();
            x.WriteStartElement("model", ns);
            x.WriteAttributeString("unit", "millimeter");
            x.WriteAttributeString("xml", "lang", null, "en-US");
            x.WriteStartElement("metadata"); x.WriteAttributeString("name", "Application"); x.WriteString("NamePlateGenerator"); x.WriteEndElement();
            x.WriteStartElement("resources");
            for (int i = 0; i < objects.Count; i++)
            {
                var (name, mesh, _, _) = objects[i];
                x.WriteStartElement("object");
                x.WriteAttributeString("id", (i + 1).ToString(inv));
                x.WriteAttributeString("type", "model");
                x.WriteAttributeString("name", name);
                x.WriteStartElement("mesh");
                x.WriteStartElement("vertices");
                foreach (var p in mesh.Vertices)
                {
                    x.WriteStartElement("vertex");
                    x.WriteAttributeString("x", p.X.ToString("0.####", inv));
                    x.WriteAttributeString("y", p.Y.ToString("0.####", inv));
                    x.WriteAttributeString("z", p.Z.ToString("0.####", inv));
                    x.WriteEndElement();
                }
                x.WriteEndElement();
                x.WriteStartElement("triangles");
                for (int t = 0; t < mesh.Indices.Count; t += 3)
                {
                    x.WriteStartElement("triangle");
                    x.WriteAttributeString("v1", mesh.Indices[t].ToString(inv));
                    x.WriteAttributeString("v2", mesh.Indices[t + 1].ToString(inv));
                    x.WriteAttributeString("v3", mesh.Indices[t + 2].ToString(inv));
                    x.WriteEndElement();
                }
                x.WriteEndElement(); x.WriteEndElement(); x.WriteEndElement();
            }
            x.WriteEndElement();
            x.WriteStartElement("build");
            for (int i = 0; i < objects.Count; i++)
            {
                x.WriteStartElement("item");
                x.WriteAttributeString("objectid", (i + 1).ToString(inv));
                x.WriteAttributeString("transform", FormattableString.Invariant($"1 0 0 0 1 0 0 0 1 {objects[i].Dx:0.####} {objects[i].Dy:0.####} 0"));
                x.WriteEndElement();
            }
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndDocument();
        }

        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, byte[] data) { using var s = zip.CreateEntry(name).Open(); s.Write(data); }
        Add("[Content_Types].xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>" +
            "<Default Extension=\"json\" ContentType=\"application/json\"/></Types>"));
        Add("_rels/.rels", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>"));
        Add("3D/3dmodel.model", ms.ToArray());
        Add(SettingsEntry, Encoding.UTF8.GetBytes(model.Settings.ToJson()));
    }

    /// <summary>Structural check of a 3MF written by <see cref="Write3Mf"/>: package parts, XML, index ranges, build items.</summary>
    public static bool Check3Mf(string path, out string problem)
    {
        problem = "";
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "3D/3dmodel.model" })
                if (zip.GetEntry(part) == null) { problem = "missing " + part; return false; }
            var doc = new XmlDocument();
            using (var s = zip.GetEntry("3D/3dmodel.model")!.Open()) doc.Load(s);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("m", "http://schemas.microsoft.com/3dmanufacturing/core/2015/02");
            var ids = new HashSet<string>();
            foreach (XmlElement obj in doc.SelectNodes("//m:object", ns)!)
            {
                ids.Add(obj.GetAttribute("id"));
                int nv = obj.SelectNodes("m:mesh/m:vertices/m:vertex", ns)!.Count;
                foreach (XmlElement t in obj.SelectNodes("m:mesh/m:triangles/m:triangle", ns)!)
                {
                    int a = int.Parse(t.GetAttribute("v1")), b = int.Parse(t.GetAttribute("v2")), c = int.Parse(t.GetAttribute("v3"));
                    if (a < 0 || b < 0 || c < 0 || a >= nv || b >= nv || c >= nv || a == b || b == c || a == c)
                    { problem = $"bad triangle in object {obj.GetAttribute("id")}"; return false; }
                }
            }
            foreach (XmlElement item in doc.SelectNodes("//m:build/m:item", ns)!)
                if (!ids.Contains(item.GetAttribute("objectid"))) { problem = "build item without object"; return false; }
            return ids.Count > 0;
        }
        catch (Exception ex) { problem = ex.Message; return false; }
    }

    public static void SaveProject(PlateSettings s, string path) => File.WriteAllText(path, s.ToJson(), new UTF8Encoding(false));

    /// <summary>Reads settings from a project .json or from a 3MF written by this program.</summary>
    public static PlateSettings LoadProject(string path)
    {
        if (path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var e = zip.GetEntry(SettingsEntry)
                ?? throw new InvalidDataException("This 3MF has no embedded name-plate settings (it was not exported by this program).");
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return PlateSettings.FromJson(r.ReadToEnd());
        }
        return PlateSettings.FromJson(File.ReadAllText(path));
    }

    /// <summary>The files <see cref="ExportAll"/> would write (to ask before overwriting).</summary>
    public static List<string> ExportTargets(PlateModel model, string basePath, bool stl, bool threeMf, bool project)
    {
        var files = new List<string>();
        if (threeMf) files.Add(basePath + ".3mf");
        if (stl)
        {
            files.Add(basePath + "_plate.stl");
            if (model.LettersFront is { IsEmpty: false }) files.Add(basePath + "_letters_front.stl");
            if (model.LettersBack is { IsEmpty: false }) files.Add(basePath + "_letters_back.stl");
            if (model.Holder is { Print.IsEmpty: false }) files.Add(basePath + "_holder.stl");
        }
        if (project) files.Add(basePath + ProjectExtension);
        return files;
    }

    /// <summary>Writes the selected formats next to <paramref name="basePath"/> (no extension). Returns the created files.</summary>
    public static List<string> ExportAll(PlateModel model, string basePath, bool stl, bool threeMf, bool project)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(basePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var files = new List<string>();
        if (threeMf) { Write3Mf(model, basePath + ".3mf"); files.Add(basePath + ".3mf"); }
        if (stl)
        {
            Write(model.Plate, basePath + "_plate.stl");
            if (model.LettersFront is { IsEmpty: false } f) Write(f, basePath + "_letters_front.stl");
            if (model.LettersBack is { IsEmpty: false } b) Write(b, basePath + "_letters_back.stl");
            if (model.Holder is { Print.IsEmpty: false } h) Write(h.Print, basePath + "_holder.stl");
        }
        if (project) { SaveProject(model.Settings, basePath + ProjectExtension); files.Add(basePath + ProjectExtension); }
        return files;

        void Write(TriMesh m, string p) { WriteBinaryStl(m, p); files.Add(p); }
    }
}
