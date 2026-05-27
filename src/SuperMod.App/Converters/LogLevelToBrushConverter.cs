using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SuperMod.App.Converters;

/// <summary>Colours a log level string (Error/Warning/etc.).</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Critical" or "Error" => new SolidColorBrush(Color.Parse("#ED4245")),
        "Warning" => new SolidColorBrush(Color.Parse("#FAA61A")),
        "Information" => new SolidColorBrush(Color.Parse("#3BA55D")),
        _ => new SolidColorBrush(Color.Parse("#80848E"))
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
