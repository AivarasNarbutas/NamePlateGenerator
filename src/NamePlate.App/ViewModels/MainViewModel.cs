using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using NamePlate.Core;
using NamePlateApp.Mvvm;
using NamePlateApp.Services;

namespace NamePlateApp.ViewModels;

/// <summary>
/// State of the whole window. Every setting is a property here; any change queues a rebuild that runs on a
/// worker thread (debounced, older builds cancelled). The view never touches <see cref="PlateSettings"/> directly.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IUserDefaultsStore? _defaults;
    private readonly int _debounceMs;
    private PlateSettings _s;
    private CancellationTokenSource? _buildCts;
    private bool _fitPending = true;

    public MainViewModel(IDialogService dialogs, IUserDefaultsStore? defaults = null, int debounceMs = 250)
    {
        _dialogs = dialogs;
        _defaults = defaults;
        _debounceMs = debounceMs;
        _s = NewSettings();
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ResetCommand = new RelayCommand(Reset);
        BrowseFontFileCommand = new RelayCommand(BrowseFontFile);
        ClearFontFileCommand = new RelayCommand(() => FontFile = "", () => !string.IsNullOrEmpty(FontFile));
        SaveFontDefaultsCommand = new RelayCommand(SaveFontDefaults, () => _defaults != null);
        AddHolderPointCommand = new RelayCommand(AddHolderPoint, () => HolderPoints.Count >= 2);
        DeleteHolderPointCommand = new RelayCommand(DeleteHolderPoint, () => SelectedHolderPoint != null && HolderPoints.Count > 3);
        ApplyHolderTemplateCommand = new RelayCommand(ApplyHolderTemplate);
        RebuildHolderPoints();
        QueueBuild(0);
        FontListLoaded = LoadFontNamesAsync();
    }

    // ---------------- lists for combo boxes ----------------
    private IReadOnlyList<string> _fontNames = Array.Empty<string>();
    /// <summary>Installed fonts as Word lists them. Empty for the first seconds: the list loads in the background.</summary>
    public IReadOnlyList<string> FontNames { get => _fontNames; private set => SetField(ref _fontNames, value); }

    /// <summary>Completes when <see cref="FontNames"/> is filled (tests await it).</summary>
    public Task FontListLoaded { get; }

    private async Task LoadFontNamesAsync()
    {
        try { FontNames = await Task.Run(TextOutline.InstalledFonts); }
        catch (Exception ex) { Status = "Could not list the installed fonts: " + ex.Message; }
    }

    public IReadOnlyList<TextMode> Modes { get; } = Enum.GetValues<TextMode>();
    public IReadOnlyList<TextFaces> FaceOptions { get; } = Enum.GetValues<TextFaces>();
    public IReadOnlyList<HAlign> HAligns { get; } = Enum.GetValues<HAlign>();
    public IReadOnlyList<VAlign> VAligns { get; } = Enum.GetValues<VAlign>();

    // ---------------- plate ----------------
    public double Length { get => _s.Length; set => Set(_s.Length, value, v => _s.Length = v, refit: true); }
    public double Height { get => _s.Height; set { if (Set(_s.Height, value, v => _s.Height = v, refit: true)) RefreshHolderFrame(); } }
    public double Thickness { get => _s.Thickness; set { if (Set(_s.Thickness, value, v => _s.Thickness = v)) RefreshHolderFrame(); } }
    public double Chamfer { get => _s.Chamfer; set => Set(_s.Chamfer, value, v => _s.Chamfer = v); }
    public bool ChamferEnds { get => _s.ChamferEnds; set => Set(_s.ChamferEnds, value, v => _s.ChamferEnds = v); }

    // ---------------- text ----------------
    public List<TextRun> FrontRuns { get => _s.FrontRuns; set => Set(_s.FrontRuns, value ?? new(), v => _s.FrontRuns = v); }
    public List<TextRun> BackRuns { get => _s.BackRuns; set => Set(_s.BackRuns, value ?? new(), v => _s.BackRuns = v); }

    public bool BackSameAsFront
    {
        get => _s.BackSameAsFront;
        set
        {
            if (!Set(_s.BackSameAsFront, value, v => _s.BackSameAsFront = v)) return;
            // start the separate back text from the front text instead of an empty box
            if (!value && BackRuns.All(r => string.IsNullOrEmpty(r.Text)))
                BackRuns = FrontRuns.Select(Copy).ToList();
            OnPropertyChanged(nameof(IsBackEditorEnabled));
            OnPropertyChanged(nameof(BackTextHint));
        }
    }

    public bool IsBackEditorEnabled => !BackSameAsFront && Faces != TextFaces.Front;

    /// <summary>Tells the user what to change when the back editor is disabled.</summary>
    public string BackTextHint =>
        Faces == TextFaces.Front ? "No text on the back. Set \"Text on\" to Back or Both to add some."
        : BackSameAsFront ? "The back repeats the front text. Untick the box above to type a different back text."
        : "";

    // ---------------- default font ----------------
    public string FontFamily { get => _s.FontFamily; set => Set(_s.FontFamily, value ?? "", v => _s.FontFamily = v); }
    public string FontFile { get => _s.FontFile; set => Set(_s.FontFile, value ?? "", v => _s.FontFile = v); }
    public bool Bold { get => _s.Bold; set => Set(_s.Bold, value, v => _s.Bold = v); }
    public bool Italic { get => _s.Italic; set => Set(_s.Italic, value, v => _s.Italic = v); }
    public double TextSize { get => _s.TextSize; set => Set(_s.TextSize, value, v => _s.TextSize = v); }
    public bool AutoFit { get => _s.AutoFit; set => Set(_s.AutoFit, value, v => _s.AutoFit = v); }
    public double LineSpacing { get => _s.LineSpacing; set => Set(_s.LineSpacing, value, v => _s.LineSpacing = v); }

    // ---------------- placement ----------------
    public HAlign HAlign { get => _s.HAlign; set => Set(_s.HAlign, value, v => _s.HAlign = v); }
    public VAlign VAlign { get => _s.VAlign; set => Set(_s.VAlign, value, v => _s.VAlign = v); }
    public HAlign LineAlign { get => _s.LineAlign; set => Set(_s.LineAlign, value, v => _s.LineAlign = v); }
    public double Margin { get => _s.Margin; set => Set(_s.Margin, value, v => _s.Margin = v); }
    public double OffsetX { get => _s.OffsetX; set => Set(_s.OffsetX, value, v => _s.OffsetX = v); }
    public double OffsetY { get => _s.OffsetY; set => Set(_s.OffsetY, value, v => _s.OffsetY = v); }

    // ---------------- faces / depth ----------------
    public TextFaces Faces
    {
        get => _s.Faces;
        set
        {
            if (!Set(_s.Faces, value, v => _s.Faces = v)) return;
            OnPropertyChanged(nameof(IsBackEditorEnabled));
            OnPropertyChanged(nameof(BackTextHint));
        }
    }

    public TextMode Mode
    {
        get => _s.Mode;
        set
        {
            if (!Set(_s.Mode, value, v => _s.Mode = v)) return;
            OnPropertyChanged(nameof(UsesPocket));
            OnPropertyChanged(nameof(IsInlay));
            OnPropertyChanged(nameof(UsesTextHeight));
        }
    }

    public bool UsesPocket => Mode != TextMode.Emboss;
    public bool IsInlay => Mode == TextMode.Inlay;
    /// <summary>Engrave cuts pockets only; the letter height has no effect there.</summary>
    public bool UsesTextHeight => Mode != TextMode.Engrave;

    public double PocketDepth { get => _s.PocketDepth; set => Set(_s.PocketDepth, value, v => _s.PocketDepth = v); }
    public double TextHeight { get => _s.TextHeight; set => Set(_s.TextHeight, value, v => _s.TextHeight = v); }
    public double Clearance { get => _s.Clearance; set => Set(_s.Clearance, value, v => _s.Clearance = v); }
    public double PrintGap { get => _s.PrintGap; set => Set(_s.PrintGap, value, v => _s.PrintGap = v); }

    // ---------------- holder (stand) ----------------
    public IReadOnlyList<HolderTemplate> HolderTemplateOptions { get; } = Enum.GetValues<HolderTemplate>();
    public IReadOnlyList<XAnchor> XAnchors { get; } = Enum.GetValues<XAnchor>();
    public IReadOnlyList<YAnchor> YAnchors { get; } = Enum.GetValues<YAnchor>();

    public bool HolderEnabled { get => _s.Holder.Enabled; set => Set(_s.Holder.Enabled, value, v => _s.Holder.Enabled = v); }

    public double HolderClearance
    {
        get => _s.Holder.Clearance;
        set { if (Set(_s.Holder.Clearance, value, v => _s.Holder.Clearance = v)) RefreshHolderFrame(); }
    }

    public double HolderExtraLength { get => _s.Holder.ExtraLength; set => Set(_s.Holder.ExtraLength, value, v => _s.Holder.ExtraLength = v); }

    /// <summary>Where the plate faces/edges (plus clearance) are; the profile points are measured from these.</summary>
    public HolderFrame HolderFrame => HolderFrame.For(_s);

    /// <summary>Plate cross-section after the builder's limits (what the 2D editor draws).</summary>
    public double EffectiveThickness => PlateBuilder.EffectiveSize(_s).T;
    public double EffectiveHeight => PlateBuilder.EffectiveSize(_s).H;

    public ObservableCollection<HolderPointViewModel> HolderPoints { get; } = new();

    private HolderPointViewModel? _selectedHolderPoint;
    public HolderPointViewModel? SelectedHolderPoint
    {
        get => _selectedHolderPoint;
        set { if (SetField(ref _selectedHolderPoint, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    private HolderTemplate _selectedHolderTemplate = HolderTemplate.ChannelStand;
    public HolderTemplate SelectedHolderTemplate { get => _selectedHolderTemplate; set => SetField(ref _selectedHolderTemplate, value); }

    private bool _showHolder = true;
    /// <summary>Preview only: show the holder around the plate (export follows <see cref="HolderEnabled"/>).</summary>
    public bool ShowHolder { get => _showHolder; set => SetField(ref _showHolder, value); }

    /// <summary>The whole profile was replaced (template, import, reset): the 2D editor should refit.</summary>
    public event EventHandler? HolderProfileReset;

    // ---------------- export options ----------------
    private bool _export3Mf = true, _exportStl = true, _exportProject = true;
    public bool Export3Mf { get => _export3Mf; set => SetField(ref _export3Mf, value); }
    public bool ExportStl { get => _exportStl; set => SetField(ref _exportStl, value); }
    public bool ExportProject { get => _exportProject; set => SetField(ref _exportProject, value); }

    // ---------------- preview / status ----------------
    private bool _showPrintLayout;
    public bool ShowPrintLayout
    {
        get => _showPrintLayout;
        set { if (SetField(ref _showPrintLayout, value)) FitViewRequested?.Invoke(this, EventArgs.Empty); }
    }

    private PlateModel? _model;
    public PlateModel? Model { get => _model; private set => SetField(ref _model, value); }

    private bool _isBuilding;
    public bool IsBuilding { get => _isBuilding; private set => SetField(ref _isBuilding, value); }

    private string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    private IReadOnlyList<string> _warnings = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get => _warnings; private set => SetField(ref _warnings, value); }

    /// <summary>Asks the view to frame the whole scene (after import, reset, size change, layout switch).</summary>
    public event EventHandler? FitViewRequested;

    /// <summary>The build that will produce the next <see cref="Model"/> (tests await it).</summary>
    public Task CurrentBuild { get; private set; } = Task.CompletedTask;

    /// <summary>Copy of the current settings (what export and save use).</summary>
    public PlateSettings Snapshot() => _s.Clone();

    // ---------------- commands ----------------
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand BrowseFontFileCommand { get; }
    public ICommand ClearFontFileCommand { get; }
    public ICommand SaveFontDefaultsCommand { get; }
    public ICommand AddHolderPointCommand { get; }
    public ICommand DeleteHolderPointCommand { get; }
    public ICommand ApplyHolderTemplateCommand { get; }

    // ---------------- implementation ----------------

    private bool Set<T>(T current, T value, Action<T> apply, bool refit = false, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        apply(value);
        OnPropertyChanged(name);
        if (refit) _fitPending = true;
        QueueBuild(_debounceMs);
        return true;
    }

    private static TextRun Copy(TextRun r) =>
        new() { Text = r.Text, FontFamily = r.FontFamily, Size = r.Size, Bold = r.Bold, Italic = r.Italic };

    private void QueueBuild(int delayMs)
    {
        _buildCts?.Cancel();
        var cts = _buildCts = new CancellationTokenSource();
        CurrentBuild = BuildAsync(_s.Clone(), delayMs, cts.Token);
    }

    private async Task BuildAsync(PlateSettings snapshot, int delayMs, CancellationToken ct)
    {
        try
        {
            // a newer edit cancels this build; nothing here throws for that (WhenAny swallows the cancelled delay)
            if (delayMs > 0) await Task.WhenAny(Task.Delay(delayMs, ct));
            if (ct.IsCancellationRequested) return;
            IsBuilding = true;
            var model = await Task.Run(() => PlateBuilder.TryBuild(snapshot, ct));
            if (model == null || ct.IsCancellationRequested) return;
            Model = model;
            Warnings = model.Warnings;
            Status = Describe(model);
            if (_fitPending) { _fitPending = false; FitViewRequested?.Invoke(this, EventArgs.Empty); }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Status = "Build failed: " + ex.Message;
            Warnings = Array.Empty<string>();
        }
        if (!ct.IsCancellationRequested) IsBuilding = false;
    }

    private static string Describe(PlateModel m)
    {
        var (mn, mx) = m.Plate.Bounds();
        var parts = new List<string>
        {
            $"Plate {mx.X - mn.X:0.###} × {mx.Y - mn.Y:0.###} × {mx.Z - mn.Z:0.###} mm",
            $"{m.Plate.TriangleCount:N0} triangles",
        };
        if (m.LettersFront != null) parts.Add($"front letters {m.LettersFront.Volume():0} mm³");
        if (m.LettersBack != null) parts.Add($"back letters {m.LettersBack.Volume():0} mm³");
        parts.Add($"default text size {m.EffectiveTextSize:0.##} mm");
        return string.Join("   |   ", parts);
    }

    /// <summary>Replaces all settings (import / reset) and refreshes every binding.</summary>
    public void Load(PlateSettings settings)
    {
        _s = settings.Clone();
        RebuildHolderPoints();
        OnPropertyChanged(string.Empty);
        HolderProfileReset?.Invoke(this, EventArgs.Empty);
        _fitPending = true;
        QueueBuild(0);
    }

    /// <summary>Built-in defaults plus the font the user saved as default.</summary>
    private PlateSettings NewSettings()
    {
        var s = new PlateSettings();
        _defaults?.LoadFonts()?.ApplyTo(s);
        return s;
    }

    private void SaveFontDefaults()
    {
        try
        {
            _defaults!.SaveFonts(FontDefaults.From(_s));
            string name = string.IsNullOrEmpty(FontFile) ? FontFamily : Path.GetFileName(FontFile);
            _dialogs.ShowInfo($"New plates and Reset now start with \"{name}\", {TextSize:0.##} mm" +
                              $"{(Bold ? ", bold" : "")}{(Italic ? ", italic" : "")}, line spacing {LineSpacing:0.##}.", "Default font");
        }
        catch (Exception ex) { _dialogs.ShowError("Could not save the default font:\n" + ex.Message, "Default font"); }
    }

    // ---------------- holder points ----------------

    private void RebuildHolderPoints()
    {
        HolderPoints.Clear();
        foreach (var p in _s.Holder.Points) HolderPoints.Add(CreatePoint(p));
        Renumber();
        SelectedHolderPoint = null;
    }

    private HolderPointViewModel CreatePoint(HolderPoint p) => new(p, () => HolderFrame, OnHolderPointEdited);

    private void Renumber()
    {
        for (int i = 0; i < HolderPoints.Count; i++) HolderPoints[i].Number = i + 1;
    }

    /// <summary>Writes the edited profile back into the settings and rebuilds.</summary>
    private void OnHolderPointEdited()
    {
        _s.Holder.Points = HolderPoints.Select(p => p.ToModel()).ToList();
        QueueBuild(_debounceMs);
    }

    private void RefreshHolderFrame()
    {
        foreach (var p in HolderPoints) p.FrameChanged();
        OnPropertyChanged(nameof(HolderFrame));
        OnPropertyChanged(nameof(EffectiveThickness));
        OnPropertyChanged(nameof(EffectiveHeight));
    }

    /// <summary>Inserts a point halfway along the edge after the selected point (or after the last point),
    /// measured from the same face/edge as the point before it.</summary>
    private void AddHolderPoint()
    {
        if (HolderPoints.Count < 2) return;
        int prev = SelectedHolderPoint != null ? HolderPoints.IndexOf(SelectedHolderPoint) : HolderPoints.Count - 1;
        var a = HolderPoints[prev];
        var b = HolderPoints[(prev + 1) % HolderPoints.Count];
        var f = HolderFrame;
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        var p = CreatePoint(new HolderPoint(a.XFrom, Math.Round(mx - f.XOf(a.XFrom), 2), a.YFrom, Math.Round(my - f.YOf(a.YFrom), 2)));
        HolderPoints.Insert(prev + 1, p);
        Renumber();
        SelectedHolderPoint = p;
        OnHolderPointEdited();
    }

    private void DeleteHolderPoint()
    {
        if (SelectedHolderPoint == null || HolderPoints.Count <= 3) return;
        int i = HolderPoints.IndexOf(SelectedHolderPoint);
        HolderPoints.RemoveAt(i);
        Renumber();
        SelectedHolderPoint = HolderPoints[Math.Min(i, HolderPoints.Count - 1)];
        OnHolderPointEdited();
    }

    private void ApplyHolderTemplate()
    {
        if (!_dialogs.Confirm($"Replace the holder profile with the \"{SelectedHolderTemplate}\" template? Your point edits are lost.", "Holder template"))
            return;
        _s.Holder.Points = HolderTemplates.Create(SelectedHolderTemplate);
        RebuildHolderPoints();
        _fitPending = true;
        QueueBuild(0);
        HolderProfileReset?.Invoke(this, EventArgs.Empty);
    }

    private void Reset()
    {
        if (_dialogs.Confirm("Reset every setting and the text to the defaults?", "Reset")) Load(NewSettings());
    }

    private void BrowseFontFile()
    {
        var file = _dialogs.OpenFontFile();
        if (file != null) FontFile = file;
    }

    private async Task ImportAsync()
    {
        var path = _dialogs.OpenProject();
        if (path == null) return;
        try
        {
            Load(await Task.Run(() => Exporters.LoadProject(path)));
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not import {Path.GetFileName(path)}:\n{ex.Message}", "Import");
        }
    }

    private async Task ExportAsync()
    {
        if (!Export3Mf && !ExportStl && !ExportProject)
        {
            _dialogs.ShowInfo("Tick at least one file format.", "Export");
            return;
        }
        var basePath = _dialogs.ChooseExportBase(SuggestedFileName());
        if (basePath == null) return;

        var snapshot = Snapshot();
        try
        {
            var model = await Task.Run(() => PlateBuilder.Build(snapshot));
            var targets = Exporters.ExportTargets(model, basePath, ExportStl, Export3Mf, ExportProject);
            var existing = targets.Where(File.Exists).Select(Path.GetFileName).ToList();
            if (existing.Count > 0 &&
                !_dialogs.Confirm("These files already exist and will be replaced:\n" + string.Join("\n", existing), "Export"))
                return;
            bool stl = ExportStl, mf = Export3Mf, proj = ExportProject;
            var files = await Task.Run(() => Exporters.ExportAll(model, basePath, stl, mf, proj));
            var note = model.Warnings.Count > 0 ? "\n\nWarnings:\n" + string.Join("\n", model.Warnings) : "";
            _dialogs.ShowInfo("Exported:\n" + string.Join("\n", files) + note, "Export");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Export failed:\n" + ex.Message, "Export");
        }
    }

    /// <summary>First line of the front text as a file name ("Your Name" -> "Your_Name").</summary>
    public string SuggestedFileName()
    {
        var first = string.Concat(FrontRuns.Select(r => r.Text)).Replace("\r", "").Split('\n')[0].Trim();
        var safe = Regex.Replace(first, @"[^\p{L}\p{N}\-]+", "_").Trim('_');
        if (safe.Length > 40) safe = safe[..40];
        return safe.Length > 0 ? safe : "nameplate";
    }
}
