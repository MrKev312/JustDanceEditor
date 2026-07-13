using Avalonia.Data.Converters;
using Avalonia.Media;

using KevInc.Avalonia;

using System;
using System.Globalization;

namespace JustDanceEditor.Editor.Converters;

/// <summary>
/// Converts a bool (isActive step) to a brush for the step indicator.
/// Active = the live platform accent, inactive = dark gray.
/// </summary>
public class BoolToStepBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(60, 60, 60));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? PlatformTheme.SystemAccentBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}