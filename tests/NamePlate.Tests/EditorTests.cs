using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using NamePlate.Core;
using NamePlateApp.Controls;

namespace NamePlate.Tests;

/// <summary>Drives the rich-text editor control directly (needs the STA thread the runner provides).</summary>
public static class EditorTests
{
    public static void Run()
    {
        T.Section("Formatted text editor");
        var ed = new FormattedTextEditor { DefaultFontFamily = new FontFamily("Arial"), DefaultSize = 10 };
        ed.Runs = new() { new TextRun { Text = "Hello world\nSecond" } };
        T.Check(Text(ed) == "Hello world\nSecond", "loads plain runs: " + Text(ed));

        // bold "world" only
        ed.SelectRange(6, 5);
        ed.ApplyToSelection(TextElement.FontWeightProperty, FontWeights.Bold);
        var r = ed.Runs!;
        // a line break carries no formatting, so it is appended to the run before it
        T.Check(r.Count == 3 && r[0].Text == "Hello " && r[0].Bold == null && r[1].Text.TrimEnd('\n') == "world" && r[1].Bold == true
                && r[2].Text == "Second" && r[2].Bold == null,
            "bolding a word changes only that word: " + Dump(r));
        T.Check(Text(ed) == "Hello world\nSecond", "text unchanged by formatting");

        // a size typed in the box applies to the selection
        ed.SelectRange(0, 5);
        ed.SetSizeText("16");
        ed.CommitSize();
        T.Check(ed.Runs![0].Text == "Hello" && ed.Runs[0].Size == 16, "typed size 16 mm applies to the selection: " + Dump(ed.Runs));

        // leaving the size box without typing must not format anything (old bug)
        var before = Dump(ed.Runs!);
        ed.SelectRange(12, 6);
        ed.CommitSize();
        T.Check(Dump(ed.Runs!) == before, "focus leaving the size box without typing changes nothing");

        // per-run font
        ed.SelectRange(12, 6);
        ed.ApplyToSelection(TextElement.FontFamilyProperty, new FontFamily("Times New Roman"));
        T.Check(ed.Runs!.Last().FontFamily == "Times New Roman" && ed.Runs.Last().Text == "Second", "font of the selection changes: " + Dump(ed.Runs));

        // clear returns the selection to inherit
        ed.SelectRange(0, 18);
        ed.ClearSelectionFormatting();
        T.Check(ed.Runs!.Count == 1 && ed.Runs[0].Bold == null && ed.Runs[0].Size == null && ed.Runs[0].FontFamily == null,
            "clear formatting removes every override: " + Dump(ed.Runs));

        // text without overrides follows a change of the default size, in the editor too
        ed.DefaultSize = 14;
        T.Check(ed.Runs![0].Size == null && Math.Abs(ed.Box.Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().First().FontSize - 14 * FormattedTextEditor.DisplayScale) < 1e-9,
            "changing the default size updates inherited text in the editor too");

        // a size the user typed survives the default passing through the same value
        ed.SelectRange(0, 5);
        ed.SetSizeText("9");
        ed.CommitSize();
        ed.DefaultSize = 9;
        ed.Box.CaretPosition = ed.Box.Document.ContentEnd;
        ed.Box.CaretPosition.InsertTextInRun("y");
        ed.DefaultSize = 14;
        T.Check(ed.Runs![0].Text == "Hello" && ed.Runs[0].Size == 9, "a typed size is kept after the default was briefly the same: " + Dump(ed.Runs));
        ed.SelectRange(0, 5); ed.ClearSelectionFormatting();

        // regression: bold word, default turns bold, user types, default turns back -> the word must stay bold
        ed.SelectRange(6, 5);
        ed.ApplyToSelection(TextElement.FontWeightProperty, FontWeights.Bold);
        ed.DefaultBold = true;
        ed.Box.CaretPosition = ed.Box.Document.ContentEnd;
        ed.Box.CaretPosition.InsertTextInRun("x");
        ed.DefaultBold = false;
        T.Check(ed.Runs!.Any(r => r.Text.StartsWith("world") && r.Bold == true), "bold stays bold after the default was bold for a while: " + Dump(ed.Runs!));

        // Clear on part of a formatted run keeps the rest
        ed.SelectRange(6, 2); // "wo"
        ed.ClearSelectionFormatting();
        T.Check(ed.Runs!.Any(r => r.Text.EndsWith("wo") && r.Bold == null) && ed.Runs.Any(r => r.Text.StartsWith("rld") && r.Bold == true),
            "Clear on part of a word only clears that part: " + Dump(ed.Runs));
        T.Check(ed.Box.Selection.Text == "wo", "Clear keeps the selection");

        // keyboard shortcuts: Ctrl+B refreshes the toolbar; underline/alignment are switched off
        ed.SelectRange(0, 5);
        System.Windows.Documents.EditingCommands.ToggleBold.Execute(null, ed.Box);
        var boldBtn = (System.Windows.Controls.Primitives.ToggleButton)ed.FindName("RunBold");
        T.Check(boldBtn.IsChecked == true && ed.Runs![0].Bold == true, "Ctrl+B updates the B button and the runs");
        T.Check(!System.Windows.Documents.EditingCommands.ToggleUnderline.CanExecute(null, ed.Box)
                && !System.Windows.Documents.EditingCommands.AlignCenter.CanExecute(null, ed.Box),
            "underline and alignment shortcuts are disabled (the plate cannot show them)");
        ed.SelectRange(0, 30); ed.ClearSelectionFormatting();

        // explicit overrides survive a default change; inherited text follows it
        ed.SelectRange(0, 5);
        ed.ApplyToSelection(TextElement.FontWeightProperty, FontWeights.Bold);
        ed.DefaultBold = true;
        var inl = ed.Box.Document.Blocks.OfType<Paragraph>().Last().Inlines.OfType<Run>().Last();
        T.Check(ed.Runs![0].Bold == true && ed.Runs.Skip(1).All(x => x.Bold == null) && inl.FontWeight == FontWeights.Bold,
            "explicit bold is kept, inherited text turns bold with the default: " + Dump(ed.Runs));
        ed.DefaultBold = false;

        // runs set from outside (import) replace the document
        ed.Runs = new() { new TextRun { Text = "A", Italic = true }, new TextRun { Text = "B\n\nC" } };
        T.Check(Text(ed) == "A B\n\nC".Replace(" ", "") && ed.ReadDocument().Count == 2, "external runs with an empty line load correctly: " + Dump(ed.ReadDocument()));
    }

    private static string Text(FormattedTextEditor ed) => string.Concat(ed.ReadDocument().Select(x => x.Text));

    private static string Dump(IEnumerable<TextRun> runs) =>
        string.Join(" ", runs.Select(r => $"[{r.Text.Replace("\n", "\\n")}{(r.Bold == true ? " B" : "")}{(r.Size is double s ? $" {s}mm" : "")}{(r.FontFamily != null ? " " + r.FontFamily : "")}]"));
}
