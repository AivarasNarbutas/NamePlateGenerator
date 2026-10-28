using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using NamePlate.Core;

namespace NamePlateApp.Controls;

/// <summary>
/// Word-like editor for <see cref="TextRun"/> lists: select characters and set font, size (mm), bold, italic.
/// <see cref="Runs"/> is the source of truth: properties a run does not set inherit the Default* values,
/// and the document is rebuilt from <see cref="Runs"/> whenever those defaults change.
/// </summary>
public partial class FormattedTextEditor : UserControl
{
    /// <summary>The editor shows mm sizes multiplied by this so small text stays readable.</summary>
    internal const double DisplayScale = 1.5;

    private bool _loading, _writing, _toolbarUpdating, _sizeEdited, _fontPicked;

    public FormattedTextEditor()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(Editor, OnPaste);
        // the plate cannot show underline, alignment per paragraph, lists or sub/superscript: switch those shortcuts off
        foreach (var cmd in new[]
                 {
                     EditingCommands.ToggleUnderline, EditingCommands.AlignLeft, EditingCommands.AlignCenter, EditingCommands.AlignRight,
                     EditingCommands.AlignJustify, EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
                     EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript,
                     EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation,
                 })
            Editor.CommandBindings.Add(new CommandBinding(cmd, (_, e) => e.Handled = true, (_, e) => { e.CanExecute = false; e.Handled = true; }));
        ApplyDefaults();
        LoadDocument();
    }

    // ---------------- dependency properties ----------------

    public static readonly DependencyProperty RunsProperty = DependencyProperty.Register(
        nameof(Runs), typeof(List<TextRun>), typeof(FormattedTextEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FormattedTextEditor)d).OnRunsChanged()));

    public List<TextRun>? Runs { get => (List<TextRun>?)GetValue(RunsProperty); set => SetValue(RunsProperty, value); }

    public static readonly DependencyProperty DefaultFontFamilyProperty = DependencyProperty.Register(
        nameof(DefaultFontFamily), typeof(FontFamily), typeof(FormattedTextEditor), new PropertyMetadata(new FontFamily("Arial"), OnDefaultsChanged));

    public FontFamily DefaultFontFamily { get => (FontFamily)GetValue(DefaultFontFamilyProperty); set => SetValue(DefaultFontFamilyProperty, value); }

    public static readonly DependencyProperty DefaultSizeProperty = DependencyProperty.Register(
        nameof(DefaultSize), typeof(double), typeof(FormattedTextEditor), new PropertyMetadata(12.0, OnDefaultsChanged));

    /// <summary>Default em height in mm.</summary>
    public double DefaultSize { get => (double)GetValue(DefaultSizeProperty); set => SetValue(DefaultSizeProperty, value); }

    public static readonly DependencyProperty DefaultBoldProperty = DependencyProperty.Register(
        nameof(DefaultBold), typeof(bool), typeof(FormattedTextEditor), new PropertyMetadata(false, OnDefaultsChanged));

    public bool DefaultBold { get => (bool)GetValue(DefaultBoldProperty); set => SetValue(DefaultBoldProperty, value); }

    public static readonly DependencyProperty DefaultItalicProperty = DependencyProperty.Register(
        nameof(DefaultItalic), typeof(bool), typeof(FormattedTextEditor), new PropertyMetadata(false, OnDefaultsChanged));

    public bool DefaultItalic { get => (bool)GetValue(DefaultItalicProperty); set => SetValue(DefaultItalicProperty, value); }

    public static readonly DependencyProperty FontNamesProperty = DependencyProperty.Register(
        nameof(FontNames), typeof(IEnumerable<string>), typeof(FormattedTextEditor), new PropertyMetadata(null));

    public IEnumerable<string>? FontNames { get => (IEnumerable<string>?)GetValue(FontNamesProperty); set => SetValue(FontNamesProperty, value); }

    // ---------------- sync: Runs <-> document ----------------

    private void OnRunsChanged()
    {
        if (!_writing) LoadDocument();
    }

    private static void OnDefaultsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ed = (FormattedTextEditor)d;
        ed.ApplyDefaults();
        ed.LoadDocument(); // local values that matched the old default must not survive as overrides
    }

    private void ApplyDefaults()
    {
        Editor.FontFamily = TextOutline.DisplayFamily(DefaultFontFamily ?? new FontFamily("Arial"));
        Editor.FontSize = Math.Clamp(DefaultSize, 1, 200) * DisplayScale;
        Editor.FontWeight = DefaultBold ? FontWeights.Bold : FontWeights.Normal;
        Editor.FontStyle = DefaultItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void LoadDocument()
    {
        _loading = true;
        try
        {
            var doc = new FlowDocument();
            var para = new Paragraph();
            foreach (var run in Runs ?? new List<TextRun>())
            {
                var parts = (run.Text ?? "").Replace("\r", "").Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) { doc.Blocks.Add(para); para = new Paragraph(); }
                    if (parts[i].Length == 0) continue;
                    var r = new Run(parts[i]);
                    if (!string.IsNullOrEmpty(run.FontFamily)) r.FontFamily = new FontFamily(run.FontFamily);
                    if (run.Size is > 0) r.FontSize = run.Size.Value * DisplayScale;
                    if (run.Bold != null) r.FontWeight = run.Bold.Value ? FontWeights.Bold : FontWeights.Normal;
                    if (run.Italic != null) r.FontStyle = run.Italic.Value ? FontStyles.Italic : FontStyles.Normal;
                    para.Inlines.Add(r);
                }
            }
            doc.Blocks.Add(para);
            Editor.Document = doc;
        }
        finally { _loading = false; }
        UpdateToolbar(); // the toolbar shows the new document, not the one it replaced
    }

    /// <summary>The value the user set on this text or an enclosing span/paragraph, or null when it inherits the default.
    /// (Comparing with the default instead would turn "bold" into "inherit" whenever the default happens to be bold.)</summary>
    private static object? Explicit(TextElement e, DependencyProperty p)
    {
        for (DependencyObject? cur = e; cur is TextElement te; cur = te.Parent)
        {
            var v = te.ReadLocalValue(p);
            if (v != DependencyProperty.UnsetValue) return v;
        }
        return null;
    }

    /// <summary>Reads the document back; only formatting the user applied is stored, everything else inherits.</summary>
    internal List<TextRun> ReadDocument()
    {
        var list = new List<TextRun>();

        void AddText(string text, TextElement src)
        {
            var tr = new TextRun
            {
                Text = text,
                FontFamily = (Explicit(src, TextElement.FontFamilyProperty) as FontFamily)?.Source,
                Size = Explicit(src, TextElement.FontSizeProperty) is double d ? Math.Round(d / DisplayScale, 3) : null,
                Bold = Explicit(src, TextElement.FontWeightProperty) is FontWeight w ? w == FontWeights.Bold : null,
                Italic = Explicit(src, TextElement.FontStyleProperty) is FontStyle st ? st == FontStyles.Italic : null,
            };
            var last = list.Count > 0 ? list[^1] : null;
            if (last != null && last.FontFamily == tr.FontFamily && last.Size == tr.Size && last.Bold == tr.Bold && last.Italic == tr.Italic)
                last.Text += text;
            else list.Add(tr);
        }

        // a line break carries no formatting of its own
        void AddBreak()
        {
            if (list.Count > 0) list[^1].Text += "\n"; else list.Add(new TextRun { Text = "\n" });
        }

        void Walk(Inline inl)
        {
            switch (inl)
            {
                case Run r: if (r.Text.Length > 0) AddText(r.Text, r); break;
                case LineBreak: AddBreak(); break;
                case Span sp: foreach (var c in sp.Inlines.ToList()) Walk(c); break;
            }
        }

        bool first = true;
        foreach (var block in Editor.Document.Blocks.ToList())
        {
            if (!first) AddBreak();
            first = false;
            switch (block)
            {
                case Paragraph p: foreach (var inl in p.Inlines.ToList()) Walk(inl); break;
                default: // lists/tables cannot appear (paste is plain text), but never drop text silently
                    var text = new TextRange(block.ContentStart, block.ContentEnd).Text.TrimEnd('\r', '\n');
                    if (text.Length > 0) AddText(text.Replace("\r\n", "\n"), block);
                    break;
            }
        }
        return list;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Commit();
        UpdateToolbar(); // Ctrl+B, Ctrl+I, Ctrl+] change formatting without moving the selection
    }

    private void Commit()
    {
        _writing = true;
        try { SetCurrentValue(RunsProperty, ReadDocument()); }
        finally { _writing = false; }
    }

    /// <summary>Pasted text takes the formatting at the caret; Word/HTML formatting, images and tables are not imported.</summary>
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.FormatToApply = DataFormats.UnicodeText;
        else e.CancelCommand();
    }

    // ---------------- formatting toolbar ----------------

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateToolbar();

    private void UpdateToolbar()
    {
        _toolbarUpdating = true;
        try
        {
            var sel = Editor.Selection;
            // text without its own font shows the default font's name (the editor may draw it under its WPF family name)
            var ff = sel.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            string name = ff == null ? ""
                : ff.Source == Editor.FontFamily.Source && DefaultFontFamily != null ? DefaultFontFamily.Source
                : ff.Source;
            if (name.Contains('#')) name = name[(name.LastIndexOf('#') + 1)..]; // font file: "file:///.../#Family"
            RunFont.SelectedItem = FontNames?.Contains(name) == true ? name : null;
            RunFont.Text = name;
            _fontPicked = false;
            RunSize.Text = sel.GetPropertyValue(TextElement.FontSizeProperty) is double d
                ? Math.Round(d / DisplayScale, 2).ToString("0.##", CultureInfo.InvariantCulture) : "";
            RunBold.IsChecked = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold;
            RunItalic.IsChecked = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
            _sizeEdited = false;
        }
        finally { _toolbarUpdating = false; }
    }

    internal void ApplyToSelection(DependencyProperty p, object value)
    {
        Editor.Selection.ApplyPropertyValue(p, value);
        Commit();
        UpdateToolbar();
    }

    internal void SelectRange(int start, int length)
    {
        var s = GetPosition(start); var e = GetPosition(start + length);
        Editor.Selection.Select(s, e);
    }

    /// <summary>Position of a character offset in the plain text, where each line break counts as one character.</summary>
    private TextPointer GetPosition(int charOffset)
    {
        var p = Editor.Document.ContentStart;
        int count = 0;
        while (p != null)
        {
            var ctx = p.GetPointerContext(LogicalDirection.Forward);
            if (ctx == TextPointerContext.Text)
            {
                int len = p.GetTextRunLength(LogicalDirection.Forward);
                if (count + len >= charOffset) return p.GetPositionAtOffset(charOffset - count)!;
                count += len;
            }
            else if (ctx == TextPointerContext.ElementEnd && p.GetAdjacentElement(LogicalDirection.Forward) is Paragraph { NextBlock: not null })
            {
                if (count == charOffset) return p;
                count++;
            }
            p = p.GetNextContextPosition(LogicalDirection.Forward);
        }
        return Editor.Document.ContentEnd;
    }

    // A font is applied when picked from the open list (on close) or confirmed with Enter. Typing letters in the
    // closed box jumps through the list; applying every jump would reformat the text font by font.
    private void RunFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_toolbarUpdating && RunFont.SelectedItem is string) _fontPicked = true;
    }

    private void RunFont_DropDownClosed(object? sender, EventArgs e) => ApplyPickedFont();

    private void RunFont_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || RunFont.IsDropDownOpen) return; // Enter in the open list closes it -> DropDownClosed
        ApplyPickedFont();
        e.Handled = true;
    }

    internal void ApplyPickedFont()
    {
        if (!_fontPicked || RunFont.SelectedItem is not string name) return;
        _fontPicked = false;
        ApplyToSelection(TextElement.FontFamilyProperty, TextOutline.DisplayFamily(new FontFamily(name)));
        Editor.Focus();
    }

    private void RunSize_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_toolbarUpdating) _sizeEdited = true;
    }

    private void RunSize_LostFocus(object sender, RoutedEventArgs e) => CommitSize();

    private void RunSize_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitSize();
        Editor.Focus();
    }

    /// <summary>Applies the typed size only when the user actually typed one (focus changes alone must not format).</summary>
    internal void CommitSize()
    {
        if (!_sizeEdited) return;
        _sizeEdited = false;
        if (Converters.Number.TryParse(RunSize.Text, out double mm) && mm >= 0.5 && mm <= 200)
            ApplyToSelection(TextElement.FontSizeProperty, mm * DisplayScale);
        else UpdateToolbar();
    }

    internal void SetSizeText(string text) { RunSize.Text = text; }

    private void RunBold_Click(object sender, RoutedEventArgs e)
    {
        ApplyToSelection(TextElement.FontWeightProperty, RunBold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
        Editor.Focus();
    }

    private void RunItalic_Click(object sender, RoutedEventArgs e)
    {
        ApplyToSelection(TextElement.FontStyleProperty, RunItalic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);
        Editor.Focus();
    }

    /// <summary>Removes every override from the selected characters (they follow the default font again).</summary>
    internal void ClearSelectionFormatting()
    {
        int a = Offset(Editor.Selection.Start), b = Offset(Editor.Selection.End);
        var result = new List<TextRun>();
        int pos = 0;
        foreach (var r in ReadDocument())
        {
            int s = pos, e = pos + r.Text.Length;
            pos = e;
            int c1 = Math.Clamp(a, s, e), c2 = Math.Clamp(b, s, e);
            void Piece(int from, int to, bool clear)
            {
                if (to <= from) return;
                string t = r.Text.Substring(from - s, to - from);
                var piece = clear ? new TextRun { Text = t } : new TextRun { Text = t, FontFamily = r.FontFamily, Size = r.Size, Bold = r.Bold, Italic = r.Italic };
                var last = result.Count > 0 ? result[^1] : null;
                if (last != null && last.FontFamily == piece.FontFamily && last.Size == piece.Size && last.Bold == piece.Bold && last.Italic == piece.Italic)
                    last.Text += t;
                else result.Add(piece);
            }
            Piece(s, c1, false); Piece(c1, c2, true); Piece(c2, e, false);
        }
        SetCurrentValue(RunsProperty, result); // not _writing: the document is rebuilt without the local values
        SelectRange(a, b - a);
        UpdateToolbar();
    }

    /// <summary>Plain-text offset of a position (a line break counts as one character, as in <see cref="TextRun.Text"/>).</summary>
    private int Offset(TextPointer p) => new TextRange(Editor.Document.ContentStart, p).Text.Replace("\r\n", "\n").Length;

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectionFormatting();
        Editor.Focus();
    }

    internal RichTextBox Box => Editor;
}
