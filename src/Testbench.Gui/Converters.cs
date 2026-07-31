using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Testbench.Gui;

/// <summary>Log severity to colour.</summary>
public sealed class LogKindBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info = Freeze("#E4E7EC");
    private static readonly SolidColorBrush Detail = Freeze("#7E8798");
    private static readonly SolidColorBrush Good = Freeze("#7BD88F");
    private static readonly SolidColorBrush Warn = Freeze("#E8B44A");
    private static readonly SolidColorBrush Bad = Freeze("#F07178");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogKind.Detail => Detail,
        LogKind.Good => Good,
        LogKind.Warn => Warn,
        LogKind.Bad => Bad,
        _ => Info,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>true is green, false is red. Used for status text and card edges.</summary>
public sealed class OkBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = LogKindBrushConverter.Freeze("#7BD88F");
    private static readonly SolidColorBrush Bad = LogKindBrushConverter.Freeze("#F07178");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Good : Bad;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a string has content.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a collection has entries.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
