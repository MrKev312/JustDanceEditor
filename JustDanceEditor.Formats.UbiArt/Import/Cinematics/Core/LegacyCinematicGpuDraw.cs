using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal sealed class LegacyCinematicGpuTextureSource
{
    private LegacyCinematicGpuTextureSource(
        object cacheKey,
        string name,
        int width,
        int height,
        Bgra32[] pixels,
        bool cacheable)
    {
        CacheKey = cacheKey;
        Name = name;
        Width = width;
        Height = height;
        Pixels = pixels;
        Cacheable = cacheable;
    }

    public object CacheKey { get; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public Bgra32[] Pixels { get; }
    public bool Cacheable { get; }

    public static LegacyCinematicGpuTextureSource FromMaterialized(MaterializedCinematicTexture texture) =>
        new(texture, texture.TexturePath, texture.Width, texture.Height, texture.Pixels, cacheable: true);

    public static LegacyCinematicGpuTextureSource FromPixels(
        object cacheKey,
        string name,
        int width,
        int height,
        Bgra32[] pixels,
        bool cacheable) =>
        new(cacheKey, name, width, height, pixels, cacheable);
}

internal sealed record LegacyCinematicGpuLayer(
    LegacyCinematicGpuTextureSource? Texture,
    LegacyCinematicUvSampler Sampler,
    int LayerIndex);

internal sealed record LegacyCinematicGpuDrawItem(
    Rectangle Bounds,
    ProjectedQuad Quad,
    float MaxY,
    float Alpha,
    RgbTint Tint,
    int BlendMode,
    IReadOnlyList<LegacyCinematicGpuLayer> Layers,
    LegacyCinematicUvRect? UvOverride = null,
    bool IsTriangle = false,
    Vector2 TrianglePoint0 = default,
    Vector2 TrianglePoint1 = default,
    Vector2 TrianglePoint2 = default,
    Vector2 TriangleUv0 = default,
    Vector2 TriangleUv1 = default,
    Vector2 TriangleUv2 = default,
    IReadOnlyList<LegacyCinematicGpuTriangle>? MeshTriangles = null);

internal readonly record struct LegacyCinematicGpuTriangle(
    Vector2 Point0,
    Vector2 Point1,
    Vector2 Point2,
    Vector2 Uv0,
    Vector2 Uv1,
    Vector2 Uv2);