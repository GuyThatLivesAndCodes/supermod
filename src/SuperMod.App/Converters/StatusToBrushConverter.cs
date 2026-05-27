using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SuperMod.Discord;

namespace SuperMod.App.Converters;

/// <summary>Maps a <see cref="BotStatus"/> to a status-dot colour.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        BotStatus.Running => new SolidColorBrush(Color.Parse("#3BA55D")),
        BotStatus.Starting or BotStatus.Stopping => new SolidColorBrush(Color.Parse("#FAA61A")),
        BotStatus.Faulted => new SolidColorBrush(Color.Parse("#ED4245")),
        _ => new SolidColorBrush(Color.Parse("#80848E"))
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
