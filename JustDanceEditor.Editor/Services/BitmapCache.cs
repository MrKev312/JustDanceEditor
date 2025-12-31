using Avalonia.Media.Imaging;
using Avalonia.Threading;

using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public static class BitmapCache
{
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private static readonly ConcurrentDictionary<string, bool> _pending = new();

    public static bool TryGet(string path, out Bitmap? bmp) => _cache.TryGetValue(path, out bmp);

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
                    var b = Bitmap.DecodeToWidth(fs, 200);
                    _cache.TryAdd(path, b);
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
