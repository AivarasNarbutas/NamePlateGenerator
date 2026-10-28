using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NamePlateApp.Converters;

/// <summary>Parses user input as a finite number, accepting both "2.5" and "2,5" (Lithuanian keyboards).
/// A plain WPF binding uses en-US, where "2,5" silently becomes 25.</summary>
public static class Number
{
    public static bool TryParse(object? value, out double result)
    {
        result = 0;
        if (value is not string s) return false;
        s = s.Trim().Replace(" ", "").Replace(' '.ToString(), "");
        if (s.Count(ch => ch is ',' or '.') > 1) return false; // no thousand separators: "1,000.5" is ambiguous
        return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
               && double.IsFinite(result);
    }

    public static string Format(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed class NumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Number.Format(d) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Number.TryParse(value, out var d) ? d : DependencyProperty.UnsetValue;
}

public sealed class NumberRule : ValidationRule
{
    public NumberRule() : base(ValidationStep.RawProposedValue, false) { }

    public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
        Number.TryParse(value, out _) ? ValidationResult.ValidResult : new ValidationResult(false, "Enter a number, e.g. 2.5");
}

/// <summary><c>{conv:NumberBinding Length}</c>: two-way number binding with tolerant parsing and a red border on bad input.
/// Commits on focus loss or Enter (see <see cref="Behaviors.TextBoxBehaviors"/>).</summary>
public sealed class NumberBinding : Binding
{
    private static readonly NumberConverter SharedConverter = new();

    public NumberBinding() => Init();
    public NumberBinding(string path) : base(path) => Init();

    private void Init()
    {
        Converter = SharedConverter;
        Mode = BindingMode.TwoWay;
        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        ValidationRules.Add(new NumberRule());
    }
}
