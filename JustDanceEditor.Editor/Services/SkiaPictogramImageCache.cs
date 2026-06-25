using Avalonia.Threading;

using SkiaSharp;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

internal sealed record SkiaPictogramImage(SKImage Image, int Width, int Height);

internal static class SkiaPictogramImageCache
{
    private static readonly ConcurrentDictionary<string, SkiaPictogramImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> Pending = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string path, out SkiaPictogramImage? image)
        => Cache.TryGetValue(path, out image);

    public static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Cache.TryRemove(path, out SkiaPictogramImage? cached))
            cached.Image.Dispose();

        Pending.TryRemove(path, out _);
    }

    public static void ScheduleLoad(string path, Action onLoaded)
    {
        if (string.IsNullOrWhiteSpace(path) || Cache.ContainsKey(path))
            return;

        if (!Pending.TryAdd(path, true))
            return;

        Task.Run(() =>
        {
            try
            {
                SkiaPictogramImage image = File.Exists(path)
                    ? LoadImage(path)
                    : CreateMissingImage();

                Cache.TryAdd(path, image);
            }
            catch
            {
                Cache.TryAdd(path, CreateMissingImage());
            }
            finally
            {
                Pending.TryRemove(path, out _);
                Dispatcher.UIThread.Post(onLoaded, DispatcherPriority.Render);
            }
        });
    }

    private static SkiaPictogramImage LoadImage(string path)
    {
        using SKData data = SKData.Create(path);
        SKImage image = SKImage.FromEncodedData(data)
            ?? throw new InvalidDataException("Unable to decode pictogram image.");

        return new SkiaPictogramImage(image, image.Width, image.Height);
    }

    private static SkiaPictogramImage CreateMissingImage()
    {
        const int size = 128;
        SKImageInfo info = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(160, 32, 32));

        using SKPaint paint = new()
        {
            Color = new SKColor(255, 255, 255, 190),
            StrokeWidth = 8,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        canvas.DrawLine(28, 28, 100, 100, paint);
        canvas.DrawLine(100, 28, 28, 100, paint);
        return new SkiaPictogramImage(surface.Snapshot(), size, size);
    }
}