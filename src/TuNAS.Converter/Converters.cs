using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TuNAS.Converter;

/// <summary>Für Auswahlknöpfe: aktiv, wenn der Wert dem Parameter entspricht (z. B. Qualität "Mittel").</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Markiert den gerade gewählten Formatknopf: wahr, wenn beide Werte dasselbe Objekt sind.</summary>
public sealed class ReferenceEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
