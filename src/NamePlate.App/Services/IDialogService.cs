namespace NamePlateApp.Services;

/// <summary>Everything the view-model needs from the user that is not a bound control.</summary>
public interface IDialogService
{
    /// <summary>Path of a .nameplate.json or .3mf, or null when cancelled.</summary>
    string? OpenProject();
    /// <summary>Base path (without extension) for the exported files, or null when cancelled.</summary>
    string? ChooseExportBase(string suggestedName);
    string? OpenFontFile();
    bool Confirm(string message, string title);
    void ShowInfo(string message, string title);
    void ShowError(string message, string title);
}
