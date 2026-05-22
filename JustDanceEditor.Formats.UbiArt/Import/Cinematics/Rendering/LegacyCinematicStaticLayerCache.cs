using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal sealed class StaticLayerCache(int outputWidth, int outputHeight) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<CachedStaticLayer>> layers = new(StringComparer.Ordinal);

    public CachedStaticLayer GetOrCreate(IReadOnlyList<FrameDrawItem> frameItems, int start, int end)
    {
        string key = CreateKey(frameItems, start, end);
        Lazy<CachedStaticLayer> layer = layers.GetOrAdd(
            key,
            _ =>
            {
                FrameDrawItem[] items = new FrameDrawItem[end - start];
                for (int i = 0; i < items.Length; i++)
                    items[i] = frameItems[start + i];

                return new Lazy<CachedStaticLayer>(
                    () => CreateLayer(items),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            });

        return layer.Value;
    }

    public void Dispose()
    {
        foreach (Lazy<CachedStaticLayer> layer in layers.Values)
        {
            if (layer.IsValueCreated)
                layer.Value.Dispose();
        }

        layers.Clear();
    }

    private CachedStaticLayer CreateLayer(IReadOnlyList<FrameDrawItem> items)
    {
        CanvasBuffer image = new(outputWidth, outputHeight);
        foreach (FrameDrawItem item in items)
        {
            MaterializedCinematicImage? itemImage = LegacyCinematicActorTiming.GetItemImage(item);
            if (itemImage != null)
            {
                LegacyCinematicRasterizer.DrawActorImage(
                    image,
                    item.Actor,
                    itemImage,
                    LegacyCinematicActorTiming.GetItemGeometry(item),
                    item.State,
                    item.Quad,
                    materialElapsedSeconds: LegacyCinematicActorTiming.GetItemMaterialElapsedSeconds(item, 0.0),
                    materialOverrides: item.MaterialOverrides,
                    uvOverride: item.UvOverride,
                    cameraOverride: item.CameraOverride);
            }
        }

        Rectangle bounds = ComputeAlphaBounds(image);
        return new CachedStaticLayer(image, bounds);
    }

    internal static string CreateKey(IReadOnlyList<FrameDrawItem> frameItems, int start, int end)
    {
        StringBuilder builder = new((end - start) * 192);
        for (int i = start; i < end; i++)
        {
            if (i > start)
                builder.Append('\u001f');

            FrameDrawItem item = frameItems[i];
            builder.Append(item.Actor.Actor.Key);
            builder.Append('|');
            AppendKey(builder, item.State.PositionX);
            AppendKey(builder, item.State.PositionY);
            AppendKey(builder, item.State.PositionZ);
            AppendKey(builder, item.State.ScaleX);
            AppendKey(builder, item.State.ScaleY);
            AppendKey(builder, item.State.Angle);
            builder.Append(item.State.XFlipped ? "|f1" : "|f0");
            AppendKey(builder, item.State.Alpha);
            AppendKey(builder, item.State.Tint.Red);
            AppendKey(builder, item.State.Tint.Green);
            AppendKey(builder, item.State.Tint.Blue);
            AppendKey(builder, item.Quad.TopLeft.X);
            AppendKey(builder, item.Quad.TopLeft.Y);
            AppendKey(builder, item.Quad.TopRight.X);
            AppendKey(builder, item.Quad.TopRight.Y);
            AppendKey(builder, item.Quad.BottomRight.X);
            AppendKey(builder, item.Quad.BottomRight.Y);
            AppendKey(builder, item.Quad.BottomLeft.X);
            AppendKey(builder, item.Quad.BottomLeft.Y);
            AppendKey(builder, item.UvOverride?.Left ?? -1);
            AppendKey(builder, item.UvOverride?.Top ?? -1);
            AppendKey(builder, item.UvOverride?.Right ?? -1);
            AppendKey(builder, item.UvOverride?.Bottom ?? -1);
            if (item.MaterialOverrides != null)
            {
                builder.Append(item.MaterialOverrides.CacheKey);
            }
        }

        return builder.ToString();
    }

    internal static void AppendKey(StringBuilder builder, double value)
    {
        builder.Append('|');
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    internal static void AppendKey(StringBuilder builder, float value)
    {
        builder.Append('|');
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    internal static Rectangle ComputeAlphaBounds(CanvasBuffer image)
    {
        int minX = image.Width;
        int minY = image.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < image.Height; y++)
        {
            Span<Bgra32> row = image.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x].A == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return Rectangle.Empty;

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}

internal sealed class CachedStaticLayer(CanvasBuffer image, Rectangle bounds) : IDisposable
{
    public CanvasBuffer Image { get; } = image;
    public Rectangle Bounds { get; } = bounds;

    public void Dispose()
    {
    }
}

internal readonly record struct MaterialActorTimeOffset(string Selector, double OffsetSeconds);