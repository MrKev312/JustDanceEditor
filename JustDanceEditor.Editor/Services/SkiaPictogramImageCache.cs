using Avalonia.Threading;

using SkiaSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

internal sealed record SkiaPictogramImage(SKImage Image, int Width, int Height);

internal static class SkiaPictogramImageCache
{
    private static readonly ConcurrentDictionary<string, SkiaPictogramImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PendingLoad> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int> Versions = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string path, out SkiaPictogramImage? image)
        => Cache.TryGetValue(path, out image);

    public static void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        IncrementVersion(path);

        if (Pending.TryRemove(path, out PendingLoad? pendingLoad))
            pendingLoad.Cancel();

        if (Cache.TryRemove(path, out SkiaPictogramImage? cached))
            cached.Image.Dispose();
    }

    public static void Preload(IEnumerable<string?> paths, Action? onLoaded = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<(string Path, PendingLoad Pending)> scheduledLoads = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path) || Cache.ContainsKey(path))
                continue;

            if (Pending.TryGetValue(path, out PendingLoad? existingPending))
            {
                existingPending.AddCallback(onLoaded);
                continue;
            }

            PendingLoad pendingLoad = new(GetVersion(path));
            pendingLoad.AddCallback(onLoaded);
            if (Pending.TryAdd(path, pendingLoad))
            {
                scheduledLoads.Add((path, pendingLoad));
            }
            else if (Pending.TryGetValue(path, out existingPending))
            {
                existingPending.AddCallback(onLoaded);
            }
        }

        if (scheduledLoads.Count == 0)
            return;

        Task.Run(() =>
        {
            foreach ((string path, PendingLoad pendingLoad) in scheduledLoads)
                LoadPending(path, pendingLoad);
        });
    }

    public static void ScheduleLoad(string? path, Action onLoaded)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Cache.ContainsKey(path))
        {
            Dispatcher.UIThread.Post(onLoaded, DispatcherPriority.Render);
            return;
        }

        if (Pending.TryGetValue(path, out PendingLoad? existingPending))
        {
            existingPending.AddCallback(onLoaded);
            return;
        }

        PendingLoad pendingLoad = new(GetVersion(path));
        pendingLoad.AddCallback(onLoaded);
        if (!Pending.TryAdd(path, pendingLoad))
        {
            if (Pending.TryGetValue(path, out existingPending))
                existingPending.AddCallback(onLoaded);

            return;
        }

        Task.Run(() => LoadPending(path, pendingLoad));
    }

    private static void LoadPending(string path, PendingLoad pendingLoad)
    {
        try
        {
            if (!Cache.ContainsKey(path))
            {
                SkiaPictogramImage image = File.Exists(path)
                    ? LoadImage(path)
                    : CreateMissingImage();

                CacheOrDisposeLoadedImage(path, pendingLoad, image);
            }
        }
        catch
        {
            SkiaPictogramImage missingImage = CreateMissingImage();
            CacheOrDisposeLoadedImage(path, pendingLoad, missingImage);
        }
        finally
        {
            RemovePendingLoad(path, pendingLoad);
            pendingLoad.PostCallbacks();
        }
    }

    private static void CacheOrDisposeLoadedImage(string path, PendingLoad pendingLoad, SkiaPictogramImage image)
    {
        if (pendingLoad.IsCanceled || pendingLoad.Version != GetVersion(path))
        {
            image.Image.Dispose();
            return;
        }

        if (!Cache.TryAdd(path, image))
        {
            image.Image.Dispose();
            return;
        }

        if ((pendingLoad.IsCanceled || pendingLoad.Version != GetVersion(path))
            && RemoveCacheEntry(path, image))
        {
            image.Image.Dispose();
        }
    }

    private static int GetVersion(string path)
        => Versions.TryGetValue(path, out int version) ? version : 0;

    private static void IncrementVersion(string path)
        => Versions.AddOrUpdate(path, 1, static (_, version) => unchecked(version + 1));

    private static bool RemovePendingLoad(string path, PendingLoad pendingLoad)
        => ((ICollection<KeyValuePair<string, PendingLoad>>)Pending)
            .Remove(new KeyValuePair<string, PendingLoad>(path, pendingLoad));

    private static bool RemoveCacheEntry(string path, SkiaPictogramImage image)
        => ((ICollection<KeyValuePair<string, SkiaPictogramImage>>)Cache)
            .Remove(new KeyValuePair<string, SkiaPictogramImage>(path, image));

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

    private sealed class PendingLoad
    {
        private readonly object _gate = new();
        private List<Action>? _callbacks;
        private int _isCanceled;

        public PendingLoad(int version)
        {
            Version = version;
        }

        public int Version { get; }

        public bool IsCanceled => Volatile.Read(ref _isCanceled) != 0;

        public void Cancel() => Volatile.Write(ref _isCanceled, 1);

        public void AddCallback(Action? callback)
        {
            if (callback == null)
                return;

            lock (_gate)
            {
                (_callbacks ??= []).Add(callback);
            }
        }

        public void PostCallbacks()
        {
            Action[] callbacks;
            lock (_gate)
            {
                if (_callbacks == null || _callbacks.Count == 0)
                    return;

                callbacks = [.. _callbacks];
                _callbacks.Clear();
            }

            foreach (Action callback in callbacks)
                Dispatcher.UIThread.Post(callback, DispatcherPriority.Render);
        }
    }
}
