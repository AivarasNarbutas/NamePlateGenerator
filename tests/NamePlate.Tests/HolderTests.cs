using System.Windows.Media.Media3D;
using NamePlate.Core;
using NamePlateApp.Controls;
using NamePlateApp.Services;
using NamePlateApp.ViewModels;

namespace NamePlate.Tests;

public static class HolderTests
{
    public static void Run()
    {
        T.Section("Holder geometry");
        var def = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial" });
        var h = def.Holder!;
        T.Closed("default holder (print)", h.Print);
        T.Closed("default holder (assembled)", h.Assembled);
        var (mn, mx) = h.Print.Bounds();
        T.Check(Math.Abs(mx.X - mn.X - 31.05) < 0.1 && Math.Abs(mx.Y - mn.Y - 46.05) < 0.1 && Math.Abs(mx.Z - mn.Z - 175) < 1e-6,
            $"default holder matches stand.stl: section {mx.X - mn.X:0.##} x {mx.Y - mn.Y:0.##}, length {mx.Z - mn.Z:0.##}");
        // stand.stl holds stand + plate: 93493.5 - 20711.0 mm3 for the stand alone
        T.Check(Math.Abs(h.Print.Volume() - 72782.5) / 72782.5 < 0.02, $"holder volume {h.Print.Volume():0} ~ reference stand 72783 mm3");
        T.Check(def.Warnings.Count == 0, "default plate + holder has no warnings: " + string.Join(" | ", def.Warnings));

        // plate in the slot: holder and plate must not overlap (assembled bounding check on the slot walls)
        var (amn, amx) = h.Assembled.Bounds();
        T.Check(amn.Z < 0 && amx.Z > 2.975 && amn.Y < 0 && amx.Y > 40, "assembled holder surrounds the plate");

        // plate changes move the profile
        var thick = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Thickness = 5, Height = 60, Length = 120 });
        var (tmn, tmx) = thick.Holder!.Print.Bounds();
        T.Check(Math.Abs(tmx.X - tmn.X - (31.05 + 2.025)) < 0.1 && Math.Abs(tmx.Y - tmn.Y - 66.05) < 0.1 && Math.Abs(tmx.Z - 120) < 1e-6,
            $"thicker/taller/shorter plate resizes the holder: {tmx.X - tmn.X:0.##} x {tmx.Y - tmn.Y:0.##} x {tmx.Z:0.##}");
        T.Check(thick.Warnings.All(w => !w.Contains("holder", StringComparison.OrdinalIgnoreCase)), "resized holder still fits the plate");
        T.Closed("resized holder", thick.Holder.Print);

        var extra = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = new HolderSettings { ExtraLength = -20 } });
        T.Check(Math.Abs(extra.Holder!.Length - 155) < 1e-9 && Math.Abs(extra.Holder.Assembled.Bounds().Min.X - 10) < 1e-6,
            "extra length -20 gives a 155 mm holder centred on the plate");

        var slot = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = new HolderSettings { Points = HolderTemplates.Create(HolderTemplate.SlotBase) } });
        T.Closed("slot base template", slot.Holder!.Print);

        // problems are explained
        var bad = new HolderSettings();
        bad.Points[11] = new HolderPoint(XAnchor.Back, -1.5, YAnchor.Bottom, 5); // pushed into the plate
        T.Check(PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = bad }).Warnings.Any(w => w.Contains("overlaps the plate")),
            "a point inside the plate gives an overlap warning");
        var crossed = new HolderSettings();
        (crossed.Points[1], crossed.Points[3]) = (crossed.Points[3], crossed.Points[1]);
        T.Check(PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = crossed }).Warnings.Any(w => w.Contains("crosses itself")),
            "swapped points give a self-crossing warning");
        var two = new HolderSettings { Points = HolderTemplates.Create(HolderTemplate.ChannelStand).Take(2).ToList() };
        var twoM = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = two });
        T.Check(twoM.Holder == null && twoM.Warnings.Any(w => w.Contains("at least 3")), "fewer than 3 points: no holder, explained");
        var hidden = PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Margin = 0.5, AutoFit = true });
        T.Check(hidden.Warnings.Any(w => w.Contains("hides part of the front text")), "text under the holder lips is reported");
        T.Check(PlateBuilder.Build(new PlateSettings { FontFamily = "Arial", Holder = new HolderSettings { Enabled = false } }).Holder == null,
            "switched-off holder is not built");

        // files
        var tmp = Path.Combine(Path.GetTempPath(), "nameplate_holder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var files = Exporters.ExportAll(def, Path.Combine(tmp, "p"), true, true, true);
            T.Check(files.Any(f => f.EndsWith("_holder.stl")) && Exporters.ReadStl(Path.Combine(tmp, "p_holder.stl")).CountOpenEdges() == 0,
                "holder STL is exported and closed");
            T.Check(Exporters.Check3Mf(Path.Combine(tmp, "p.3mf"), out var why) && Exporters.PrintLayout(def).Any(o => o.Name == "Holder"),
                "3MF with the holder is valid " + why);
            var back = Exporters.LoadProject(Path.Combine(tmp, "p.3mf"));
            T.Check(back.Holder.Points.Count == 15 && back.Holder.Points[2].XFrom == XAnchor.Back && back.Holder.Points[2].X == 26,
                "holder profile survives the 3MF round trip");
        }
        finally { Directory.Delete(tmp, true); }

        T.Section("Holder view-model");
        var dlg = new FakeDialogs();
        var vm = new MainViewModel(dlg, debounceMs: 20);
        vm.CurrentBuild.Wait();
        T.Check(vm.HolderPoints.Count == 15 && vm.HolderPoints[0].Number == 1 && vm.Model!.Holder != null, "15 template points, numbered, holder built");

        var backPt = vm.HolderPoints[1]; // (Back + 2, Bottom - 3)
        double x0 = backPt.X;
        vm.Thickness = 4.975;
        T.Check(Math.Abs(backPt.X - x0 - 2) < 1e-9 && backPt.XOffset == 2, "plate 2 mm thicker moves back-anchored points 2 mm, offset unchanged");
        var topPt = vm.HolderPoints[5]; // (Back + 5, Top + 3)
        double y0 = topPt.Y;
        vm.Height = 50;
        T.Check(Math.Abs(topPt.Y - y0 - 10) < 1e-9, "plate 10 mm taller moves top-anchored points 10 mm");

        double abs = topPt.Y;
        topPt.YFrom = YAnchor.Bottom;
        T.Check(Math.Abs(topPt.Y - abs) < 0.01 && topPt.YFrom == YAnchor.Bottom, "switching the anchor keeps the point in place");

        backPt.XOffset = 4;
        vm.CurrentBuild.Wait();
        T.Check(vm.Snapshot().Holder.Points[1].X == 4, "typed offset reaches the settings and rebuilds");

        vm.SelectedHolderPoint = vm.HolderPoints[0];
        var a = vm.HolderPoints[0]; var b = vm.HolderPoints[1];
        vm.AddHolderPointCommand.Execute(null);
        var added = vm.HolderPoints[1];
        T.Check(vm.HolderPoints.Count == 16 && Math.Abs(added.X - (a.X + b.X) / 2) < 0.01 && Math.Abs(added.Y - (a.Y + b.Y) / 2) < 0.01
                && vm.SelectedHolderPoint == added && vm.HolderPoints[15].Number == 16,
            "add inserts a point halfway after the selected one");
        vm.DeleteHolderPointCommand.Execute(null);
        T.Check(vm.HolderPoints.Count == 15, "delete removes the selected point");
        while (vm.HolderPoints.Count > 3) { vm.SelectedHolderPoint = vm.HolderPoints[0]; vm.DeleteHolderPointCommand.Execute(null); }
        vm.SelectedHolderPoint = vm.HolderPoints[0];
        T.Check(!vm.DeleteHolderPointCommand.CanExecute(null), "cannot delete below 3 points");

        int resets = 0; vm.HolderProfileReset += (_, _) => resets++;
        dlg.ConfirmAnswer = false; vm.ApplyHolderTemplateCommand.Execute(null);
        T.Check(vm.HolderPoints.Count == 3, "template not applied when the user says No");
        dlg.ConfirmAnswer = true; vm.SelectedHolderTemplate = HolderTemplate.SlotBase; vm.ApplyHolderTemplateCommand.Execute(null);
        vm.CurrentBuild.Wait();
        T.Check(vm.HolderPoints.Count == 10 && resets == 1 && vm.Model!.Holder != null, "applying a template replaces the profile and asks the 2D view to refit");

        T.Section("Back text hint and default font");
        vm.Faces = TextFaces.Front;
        T.Check(vm.BackTextHint.Contains("Text on") && !vm.IsBackEditorEnabled, "front-only: hint says to set 'Text on'");
        vm.Faces = TextFaces.Both; vm.BackSameAsFront = true;
        T.Check(vm.BackTextHint.Contains("Untick") && !vm.IsBackEditorEnabled, "same as front: hint says to untick");
        vm.BackSameAsFront = false;
        T.Check(vm.BackTextHint == "" && vm.IsBackEditorEnabled, "back editor enabled, no hint");

        var store = new MemoryDefaults();
        var vmFonts = new MainViewModel(dlg, store, debounceMs: 20);
        vmFonts.FontFamily = "Segoe UI"; vmFonts.TextSize = 9; vmFonts.Bold = true;
        vmFonts.SaveFontDefaultsCommand.Execute(null);
        var fresh = new MainViewModel(dlg, store, debounceMs: 20);
        T.Check(fresh.FontFamily == "Segoe UI" && fresh.TextSize == 9 && fresh.Bold, "saved default font is used by a new plate");
        fresh.FontFamily = "Arial"; dlg.ConfirmAnswer = true; fresh.ResetCommand.Execute(null);
        T.Check(fresh.FontFamily == "Segoe UI", "Reset returns to the saved default font, not the built-in one");
        T.Check(!new MainViewModel(dlg, null, 20).SaveFontDefaultsCommand.CanExecute(null), "save is disabled without a store");
        fresh.CurrentBuild.Wait(); vmFonts.CurrentBuild.Wait(); vm.CurrentBuild.Wait();

        vm.Mode = TextMode.Engrave;
        T.Check(!vm.UsesTextHeight, "Text height is disabled in Engrave mode (it has no effect there)");
        vm.Mode = TextMode.Inlay;
        T.Check(vm.UsesTextHeight, "Text height is enabled again for Inlay");

        // print layout shows what the 3MF contains, whatever 'Show holder' says
        var conv = new NamePlateApp.Converters.PreviewSceneConverter();
        var layoutHidden = (System.Windows.Media.Media3D.Model3DGroup)conv.Convert(new object[] { def, true, false }, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture)!;
        var assembledHidden = (System.Windows.Media.Media3D.Model3DGroup)conv.Convert(new object[] { def, false, false }, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture)!;
        T.Check(layoutHidden.Children.Count == Exporters.PrintLayout(def).Count && assembledHidden.Children.Count == Exporters.PrintLayout(def).Count - 1,
            "print layout always shows the exported holder; 'Show holder' only hides it in the assembled view");

        T.Section("Holder 2D editor");
        var vm2 = new MainViewModel(dlg, debounceMs: 20);
        var ed = new HolderProfileEditor { Points = vm2.HolderPoints, PlateThickness = 2.975, PlateHeight = 40 };
        T.Check(ed.MarkerCount == 15 && ed.Handles.Count == 0, "one marker per point, no handles without selection");
        var p = vm2.HolderPoints[2];
        ed.SelectedPoint = p;
        T.Check(ed.Handles.Count == 2, "selecting a point shows the X and Y arrows");
        double px = p.X, py = p.Y;
        var hx = ed.Handles.First(m => m.Direction.X != 0);
        hx.Position = new Point3D(px + 3, py + 5, 0.2); // a diagonal drag on the X arrow
        T.Check(Math.Abs(p.X - px - 3) < 0.01 && Math.Abs(p.Y - py) < 1e-9, $"dragging the X arrow moves only X ({p.X - px:0.##}, {p.Y - py:0.##})");
        var hy = ed.Handles.First(m => m.Direction.Y != 0);
        hy.Position = new Point3D(p.X + 7, py - 2, 0.2);
        T.Check(Math.Abs(p.Y - py + 2) < 0.01 && Math.Abs(p.X - px - 3) < 0.01, "dragging the Y arrow moves only Y");
        vm2.AddHolderPointCommand.Execute(null);
        T.Check(ed.MarkerCount == 16, "adding a point adds a marker");
        vm2.CurrentBuild.Wait();
    }

    private sealed class MemoryDefaults : IUserDefaultsStore
    {
        private FontDefaults? _f;
        public FontDefaults? LoadFonts() => _f;
        public void SaveFonts(FontDefaults defaults) => _f = defaults;
    }
}
