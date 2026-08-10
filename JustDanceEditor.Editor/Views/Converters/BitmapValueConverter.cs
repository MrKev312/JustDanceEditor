using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.Services;

using System;
using System.Globalization;
using System.IO;

namespace JustDanceEditor.Editor.Views.Converters;

public class BitmapValueConverter : IValueConverter
{
    public static readonly BitmapValueConverter Instance = new();

    // red placeholder bitmap used when the requested file doesn't exist (lazily created)
    private static Bitmap? _redPlaceholder;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    return new Bitmap(path);
                }
                catch (Exception ex)
                {
                    EditorLog.Fallback(ex, $"Decode bitmap '{path}'");
                    return GetRedPlaceholder();
                }
            }
            else
            {
                return GetRedPlaceholder();
            }
        }

        return null;
    }

    private static Bitmap? GetRedPlaceholder()
    {
        if (_redPlaceholder != null)
            return _redPlaceholder;
        try
        {
            _redPlaceholder = CreateRedBitmap(64, 64);
        }
        catch (System.InvalidOperationException)
        {
            // Avalonia not initialized (e.g., in unit tests)
            return null;
        }

        return _redPlaceholder;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }

    private static RenderTargetBitmap CreateRedBitmap(int width, int height)
    {
        RenderTargetBitmap bmp = new(new PixelSize(width, height));
        using (DrawingContext ctx = bmp.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Red, new Rect(0, 0, width, height));
        }

        return bmp;
    }
}
