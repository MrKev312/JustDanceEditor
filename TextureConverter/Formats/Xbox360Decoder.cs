using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.TextureType;

namespace TextureConverter.Formats;

public class Xbox360Decoder : IImageDecoder
{
    public ImageInfo Identify(DecoderOptions options, Stream stream)
    {
        long pos = stream.CanSeek ? stream.Position : 0;
        try
        {
            using Image<Bgra32> img = Decode<Bgra32>(options, stream);
            return new ImageInfo(img.PixelType, new Size(img.Width, img.Height), img.Metadata);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Seek(pos, SeekOrigin.Begin);
        }
    }

    public Task<ImageInfo> IdentifyAsync(DecoderOptions options, Stream stream, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Identify(options, stream));
    }

    public Image<TPixel> Decode<TPixel>(DecoderOptions options, Stream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        long pos = stream.CanSeek ? stream.Position : 0;
        try
        {
            Image<Bgra32> decoded = Xbox360.GetImage(stream);

            if (typeof(TPixel) == typeof(Bgra32))
                return (Image<TPixel>)(object)decoded;

            return decoded.CloneAs<TPixel>();
        }
        finally
        {
            if (stream.CanSeek)
                stream.Seek(pos, SeekOrigin.Begin);
        }
    }

    public Image Decode(DecoderOptions options, Stream stream)
    {
        return Decode<Bgra32>(options, stream);
    }

    public Task<Image<TPixel>> DecodeAsync<TPixel>(DecoderOptions options, Stream stream, CancellationToken cancellationToken = default)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        return Task.FromResult(Decode<TPixel>(options, stream));
    }

    public Task<Image> DecodeAsync(DecoderOptions options, Stream stream, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Decode(options, stream));
    }
}
