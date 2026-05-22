using SixLabors.ImageSharp;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal static class LegacyCinematicFxIds
{
    public const uint Invalid = uint.MaxValue;
    public static IReadOnlySet<uint> EmptySet { get; } = new HashSet<uint>();

    public static bool IsValid(uint id) => id is not 0 and not Invalid;
}

internal enum CinematicLayerPlane
{
    Background,
    Foreground,
    Composite
}

internal enum CinematicRenderKind
{
    Image,
    PleoVideo
}

internal readonly record struct RgbTint(double Red, double Green, double Blue)
{
    public static RgbTint White { get; } = new(1, 1, 1);

    public bool IsWhite =>
        Math.Abs(Red - 1) < 0.0001 &&
        Math.Abs(Green - 1) < 0.0001 &&
        Math.Abs(Blue - 1) < 0.0001;

    public RgbTint Multiply(RgbTint other) =>
        new(Red * other.Red, Green * other.Green, Blue * other.Blue);
}

internal enum CinematicGeometrySource
{
    NoAtlasQuad,
    RectangleAtlasQuad,
    MeshAtlas,
    PleoVideoQuad
}

internal static class LegacyCinematicNames
{
    public static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/', '!', '"', '\'', '\0').Trim();
        int worldIndex = normalized.IndexOf("world/", StringComparison.OrdinalIgnoreCase);
        if (worldIndex >= 0)
            normalized = normalized[worldIndex..];

        return normalized;
    }

    public static string NormalizeKey(IEnumerable<string> segments) =>
        string.Join("/", segments.Select(segment => segment.Trim())).ToLowerInvariant();
}