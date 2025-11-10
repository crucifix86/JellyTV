using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace JellyTV.Converters;

public class PlayPauseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPaused)
        {
            return isPaused ? "▶ Play" : "⏸ Pause";
        }
        return "▶ Play";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
