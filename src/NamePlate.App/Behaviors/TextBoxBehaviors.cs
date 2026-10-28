using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NamePlateApp.Behaviors;

/// <summary>Attached behaviour: Enter commits a single-line TextBox's binding (instead of waiting for focus loss).</summary>
public static class TextBoxBehaviors
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(TextBoxBehaviors), new PropertyMetadata(false, OnChanged));

    public static bool GetCommitOnEnter(DependencyObject d) => (bool)d.GetValue(CommitOnEnterProperty);
    public static void SetCommitOnEnter(DependencyObject d, bool v) => d.SetValue(CommitOnEnterProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        tb.KeyDown -= OnKeyDown;
        if (e.NewValue is true) tb.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { AcceptsReturn: false } tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        tb.SelectAll();
        e.Handled = true;
    }
}

/// <summary>Attached behaviour: focusing a box inside a ListBoxItem selects that item (a style trigger would
/// undo the selection again when focus leaves, e.g. when the user then clicks "Delete point").</summary>
public static class ListBoxItemBehaviors
{
    public static readonly DependencyProperty SelectOnFocusWithinProperty = DependencyProperty.RegisterAttached(
        "SelectOnFocusWithin", typeof(bool), typeof(ListBoxItemBehaviors), new PropertyMetadata(false, OnChanged));

    public static bool GetSelectOnFocusWithin(DependencyObject d) => (bool)d.GetValue(SelectOnFocusWithinProperty);
    public static void SetSelectOnFocusWithin(DependencyObject d, bool v) => d.SetValue(SelectOnFocusWithinProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBoxItem item) return;
        item.PreviewGotKeyboardFocus -= OnFocus;
        if (e.NewValue is true) item.PreviewGotKeyboardFocus += OnFocus;
    }

    private static void OnFocus(object sender, KeyboardFocusChangedEventArgs e) => ((ListBoxItem)sender).IsSelected = true;
}

/// <summary>Attached behaviour for an editable ComboBox whose Text binding commits on focus loss (so typing a
/// half name does not apply it): picking an item from the list, or pressing Enter, commits immediately.</summary>
public static class ComboBoxBehaviors
{
    public static readonly DependencyProperty CommitTextOnPickProperty = DependencyProperty.RegisterAttached(
        "CommitTextOnPick", typeof(bool), typeof(ComboBoxBehaviors), new PropertyMetadata(false, OnChanged));

    public static bool GetCommitTextOnPick(DependencyObject d) => (bool)d.GetValue(CommitTextOnPickProperty);
    public static void SetCommitTextOnPick(DependencyObject d, bool v) => d.SetValue(CommitTextOnPickProperty, v);

    // set while the user moves through the open list; auto-complete while typing changes the selection too,
    // but with the list closed, and must not apply half-typed names
    private static readonly DependencyProperty PickedProperty = DependencyProperty.RegisterAttached(
        "Picked", typeof(bool), typeof(ComboBoxBehaviors), new PropertyMetadata(false));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox cb) return;
        cb.SelectionChanged -= OnSelectionChanged;
        cb.DropDownClosed -= OnDropDownClosed;
        cb.PreviewKeyDown -= OnKeyDown;
        if (e.NewValue is not true) return;
        cb.SelectionChanged += OnSelectionChanged;
        cb.DropDownClosed += OnDropDownClosed;
        cb.PreviewKeyDown += OnKeyDown;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cb = (ComboBox)sender;
        if (cb.IsDropDownOpen && cb.SelectedItem != null) cb.SetValue(PickedProperty, true);
    }

    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        var cb = (ComboBox)sender!;
        if (!(bool)cb.GetValue(PickedProperty)) return;
        cb.SetValue(PickedProperty, false);
        // the ComboBox copies the item into Text after closing
        cb.Dispatcher.BeginInvoke(() => cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var cb = (ComboBox)sender;
        if (cb.IsDropDownOpen) { cb.SetValue(PickedProperty, true); return; } // Enter closes the list -> DropDownClosed commits
        cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
