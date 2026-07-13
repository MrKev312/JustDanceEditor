using Avalonia.Data.Converters;
using Avalonia.Media;

using System;
using System.Globalization;

namespace JustDanceEditor.Editor.Converters;

public sealed class BoolToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush HasValueBrush = new(Colors.White);
    private static readonly SolidColorBrush NoValueBrush = new(Color.FromRgb(100, 100, 100));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? HasValueBrush : NoValueBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}