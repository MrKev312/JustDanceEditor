using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public static class BitmapCache
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();
    private static readonly ConcurrentDictionary<string, bool> _pending = new();

    // Lazily-created red placeholder for missing files
    private static Bitmap? _redPlaceholder;

    public static bool TryGet(string path, out Bitmap? bmp) => _cache.TryGetValue(path, out bmp);

    private static Bitmap? GetRedPlaceholder()
    {
        if (_redPlaceholder != null)
            return _redPlaceholder;
        try
        {
            RenderTargetBitmap rtb = new(new Avalonia.PixelSize(200, 200));
            using (DrawingContext ctx = rtb.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.Red, new Avalonia.Rect(0, 0, 200, 200));
            }

            _redPlaceholder = rtb;
        }
        catch
        {
            // Avalonia not initialized; return null
            _redPlaceholder = null;
        }

        return _redPlaceholder;
    }

    public static void ScheduleLoad(string path, System.Action onLoaded)
    {
        if (_pending.ContainsKey(path))
            return;
        _pending.TryAdd(path, true);

        Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    using FileStream fs = File.OpenRead(path);
                    Bitmap b = Bitmap.DecodeToWidth(fs, 200);
                    _cache.TryAdd(path, b);
                }
                else
                {
                    // File doesn't exist; use red placeholder
                    Bitmap? red = GetRedPlaceholder();
                    _cache.TryAdd(path, red);
                }
            }
            finally
            {
                _pending.TryRemove(path, out _);
                Dispatcher.UIThread.Post(onLoaded);
            }
        });
    }
}