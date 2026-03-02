using Avalonia.Data.Converters;
using Avalonia.Media;

using System;
using System.Globalization;

namespace JustDanceEditor.Editor.Converters;

/// <summary>
/// Converts a bool (isActive step) to a brush for the step indicator.
/// Active = accent blue, inactive = dark gray.
/// </summary>
public class BoolToStepBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(60, 60, 60));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a bool (has value) to foreground brush.
/// True = white, False = dim gray.
/// </summary>
public class BoolToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush HasValueBrush = new(Colors.White);
    private static readonly SolidColorBrush NoValueBrush = new(Color.FromRgb(100, 100, 100));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HasValueBrush : NoValueBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
