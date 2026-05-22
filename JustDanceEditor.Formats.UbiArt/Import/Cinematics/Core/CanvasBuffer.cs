using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

internal sealed class CanvasBuffer(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public Bgra32[] Pixels { get; } = new Bgra32[checked(width * height)];

    public Span<Bgra32> GetRowSpan(int y) =>
        Pixels.AsSpan(y * Width, Width);
}