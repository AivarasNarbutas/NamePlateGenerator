using System.ComponentModel;
using NamePlate.Core;
using NamePlateApp.Converters;
using NamePlateApp.Services;
using NamePlateApp.ViewModels;

namespace NamePlate.Tests;

/// <summary>Scripted stand-in for the file/message dialogs.</summary>
public sealed class FakeDialogs : IDialogService
{
    public string? NextOpen, NextExportBase, NextFont;
    public bool ConfirmAnswer = true;
    public List<string> Infos = new(), Errors = new(), Confirms = new();

    public string? OpenProject() => NextOpen;
    public string? ChooseExportBase(string suggestedName) { LastSuggested = suggestedName; return NextExportBase; }
    public string? OpenFontFile() => NextFont;
    public bool Confirm(string message, string title) { Confirms.Add(message); return ConfirmAnswer; }
    public void ShowInfo(string message, string title) => Infos.Add(message);
    public void ShowError(string message, string title) => Errors.Add(message);
    public string? LastSuggested;
}

public static class ViewModelTests
{
    public static void Run()
    {
        T.Section("View-model");
        var dlg = new FakeDialogs();
        var vm = new MainViewModel(dlg, debounceMs: 30);
        vm.CurrentBuild.Wait();
        T.Check(vm.Model != null && vm.Model.Plate.CountOpenEdges() == 0, "builds a model on start");
        T.Check(vm.Status.Contains("175"), "status describes the plate: " + vm.Status);

        // rapid edits: only the last one counts (debounce + cancellation), and cancelling throws nothing
        // (a debugger stops on exceptions thrown inside Task.Run, even when the app handles them)
        int thrown = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count = (_, e) => { if (e.Exception is OperationCanceledException) Interlocked.Increment(ref thrown); };
        AppDomain.CurrentDomain.FirstChanceException += count;
        var quick = new MainViewModel(new FakeDialogs(), debounceMs: 0);
        for (int i = 0; i < 20; i++) { quick.Length = 100 + i; Thread.Sleep(3); }
        quick.CurrentBuild.Wait();
        AppDomain.CurrentDomain.FirstChanceException -= count;
        T.Check(thrown == 0 && quick.Model!.Settings.Length == 119, $"20 quick edits: last one shown, {thrown} cancellation exceptions thrown");
        vm.Length = 100; vm.Length = 120; vm.Length = 140;
        vm.CurrentBuild.Wait();
        var (mn, mx) = vm.Model!.Plate.Bounds();
        T.Check(Math.Abs(mx.X - mn.X - 140) < 1e-6, $"last of several quick edits wins (length {mx.X - mn.X})");

        // change notifications, including dependent properties
        var changed = new List<string?>();
        PropertyChangedEventHandler h = (_, e) => changed.Add(e.PropertyName);
        vm.PropertyChanged += h;
        vm.Mode = TextMode.Emboss;
        T.Check(changed.Contains(nameof(vm.Mode)) && changed.Contains(nameof(vm.UsesPocket)) && changed.Contains(nameof(vm.IsInlay)),
            "Mode change notifies UsesPocket and IsInlay");
        T.Check(!vm.UsesPocket && !vm.IsInlay, "emboss disables pocket depth and clearance");
        changed.Clear();
        vm.Faces = TextFaces.Both;
        T.Check(changed.Contains(nameof(vm.IsBackEditorEnabled)), "Faces change notifies IsBackEditorEnabled");
        changed.Clear();
        vm.Length = vm.Length;
        T.Check(changed.Count == 0, "setting an equal value raises nothing");
        vm.PropertyChanged -= h;

        // separate back text starts as a copy of the front
        vm.Faces = TextFaces.Both;
        vm.BackSameAsFront = false;
        T.Check(vm.IsBackEditorEnabled && string.Concat(vm.BackRuns.Select(r => r.Text)) == string.Concat(vm.FrontRuns.Select(r => r.Text)),
            "unticking 'same as front' copies the front text to the back");
        T.Check(!ReferenceEquals(vm.BackRuns[0], vm.FrontRuns[0]), "the copy is independent of the front runs");

        // fit-view requests
        int fits = 0;
        vm.FitViewRequested += (_, _) => fits++;
        vm.ShowPrintLayout = true;
        T.Check(fits == 1, "toggling print layout asks the view to refit");
        vm.Height = 50; vm.CurrentBuild.Wait();
        T.Check(fits == 2, "a size change refits once the new model exists");

        // export / import round trip through the commands
        var tmp = Path.Combine(Path.GetTempPath(), "nameplate_vm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            vm.Mode = TextMode.Inlay;
            vm.FrontRuns = new() { new TextRun { Text = "Jonas Jonaitis", Bold = true }, new TextRun { Text = "\nVadovas", Size = 8 } };
            dlg.NextExportBase = Path.Combine(tmp, "plate");
            vm.ExportCommand.ExecuteAsync().Wait();
            T.Check(dlg.LastSuggested == "Jonas_Jonaitis", "export suggests a file name from the first line: " + dlg.LastSuggested);
            T.Check(File.Exists(dlg.NextExportBase + ".3mf") && File.Exists(dlg.NextExportBase + "_plate.stl")
                    && File.Exists(dlg.NextExportBase + "_letters_back.stl") && File.Exists(dlg.NextExportBase + ".nameplate.json"),
                "export writes 3MF, STLs and project");
            T.Check(dlg.Infos.Count == 1 && dlg.Errors.Count == 0, "export reports success once");

            // second export asks before overwriting; "No" leaves the files alone
            dlg.ConfirmAnswer = false;
            var stamp = File.GetLastWriteTimeUtc(dlg.NextExportBase + ".3mf");
            Thread.Sleep(20);
            vm.ExportCommand.ExecuteAsync().Wait();
            T.Check(dlg.Confirms.Count == 1 && File.GetLastWriteTimeUtc(dlg.NextExportBase + ".3mf") == stamp, "re-export asks before overwriting and respects No");
            dlg.ConfirmAnswer = true;

            // nothing ticked
            vm.Export3Mf = vm.ExportStl = vm.ExportProject = false;
            vm.ExportCommand.ExecuteAsync().Wait();
            T.Check(dlg.Infos.Count == 2 && dlg.Infos[^1].Contains("at least one"), "export with no format explains itself");
            vm.Export3Mf = vm.ExportStl = vm.ExportProject = true;

            // import the 3MF into a fresh view-model
            var vm2 = new MainViewModel(dlg, debounceMs: 30);
            int allChanged = 0;
            vm2.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName)) allChanged++; };
            dlg.NextOpen = dlg.NextExportBase + ".3mf";
            vm2.ImportCommand.ExecuteAsync().Wait();
            vm2.CurrentBuild.Wait();
            T.Check(allChanged == 1, "import refreshes every binding");
            T.Check(vm2.Height == 50 && vm2.Faces == TextFaces.Both && !vm2.BackSameAsFront && vm2.FrontRuns.Count == 2
                    && vm2.FrontRuns[0].Bold == true && vm2.FrontRuns[1].Size == 8,
                "import restores dimensions, faces and per-character formatting");

            // broken import is reported, settings untouched
            File.WriteAllText(Path.Combine(tmp, "bad.nameplate.json"), "{ not json");
            dlg.NextOpen = Path.Combine(tmp, "bad.nameplate.json");
            vm2.ImportCommand.ExecuteAsync().Wait();
            T.Check(dlg.Errors.Count == 1 && vm2.Height == 50, "a broken project shows an error and keeps the current settings");

            // cancelled dialogs do nothing
            dlg.NextOpen = null; dlg.NextExportBase = null;
            int infos = dlg.Infos.Count;
            vm2.ImportCommand.ExecuteAsync().Wait(); vm2.ExportCommand.ExecuteAsync().Wait();
            T.Check(dlg.Infos.Count == infos && dlg.Errors.Count == 1, "cancelled dialogs do nothing");
        }
        finally { Directory.Delete(tmp, true); }

        // reset asks first
        dlg.ConfirmAnswer = false; vm.ResetCommand.Execute(null);
        T.Check(vm.Height == 50, "reset answered No keeps the settings");
        dlg.ConfirmAnswer = true; vm.ResetCommand.Execute(null); vm.CurrentBuild.Wait();
        T.Check(vm.Height == 40 && vm.BackSameAsFront, "reset answered Yes restores the defaults");

        // font file commands
        dlg.NextFont = @"C:\Windows\Fonts\arial.ttf";
        vm.BrowseFontFileCommand.Execute(null); vm.CurrentBuild.Wait();
        T.Check(vm.FontFile == dlg.NextFont && vm.Model!.Warnings.All(w => !w.Contains("Font file")), "browse sets a working font file");
        T.Check(vm.ClearFontFileCommand.CanExecute(null), "clear font file is enabled when a file is set");
        vm.ClearFontFileCommand.Execute(null);
        T.Check(vm.FontFile == "" && !vm.ClearFontFileCommand.CanExecute(null), "clear font file empties it and disables itself");

        T.Section("Number input");
        foreach (var (input, ok, value) in new (string, bool, double)[]
                 { ("2.5", true, 2.5), ("2,5", true, 2.5), (" 175 ", true, 175), ("-3", true, -3), ("1e1", true, 10),
                   ("abc", false, 0), ("", false, 0), ("NaN", false, 0), ("1,000.5", false, 0), ("∞", false, 0) })
        {
            bool parsed = Number.TryParse(input, out var v);
            T.Check(parsed == ok && (!ok || Math.Abs(v - value) < 1e-12), $"'{input}' -> {(parsed ? v.ToString() : "rejected")}");
        }
        var rule = new NumberRule();
        T.Check(!rule.Validate("x", System.Globalization.CultureInfo.CurrentCulture).IsValid, "invalid number is flagged in the UI");
    }
}
