using System.Windows;
using Microsoft.Win32;
using NamePlate.Core;

namespace NamePlateApp.Services;

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? OpenProject()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import name plate project",
            Filter = "Name plate project (*.nameplate.json;*.3mf)|*.nameplate.json;*.3mf|JSON (*.json)|*.json|All files|*.*",
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? ChooseExportBase(string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export name plate - choose the base file name",
            FileName = suggestedName,
            Filter = "3MF + STL + project|*.3mf",
            DefaultExt = ".3mf",
            AddExtension = true,
            OverwritePrompt = false, // the view-model asks about every file it would overwrite
        };
        if (dlg.ShowDialog(Owner) != true) return null;
        return StripKnownExtensions(dlg.FileName);
    }

    public static string StripKnownExtensions(string path)
    {
        foreach (var ext in new[] { ".3mf", Exporters.ProjectExtension, ".json", ".stl" })
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return path[..^ext.Length];
        return path;
    }

    public string? OpenFontFile()
    {
        var dlg = new OpenFileDialog { Title = "Choose a font file", Filter = "Fonts (*.ttf;*.otf)|*.ttf;*.otf" };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowInfo(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void ShowError(string message, string title) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private static MessageBoxResult Show(string m, string t, MessageBoxButton b, MessageBoxImage i) =>
        Owner != null ? MessageBox.Show(Owner, m, t, b, i) : MessageBox.Show(m, t, b, i);
}
