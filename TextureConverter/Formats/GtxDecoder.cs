using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.TextureType;

namespace TextureConverter.Formats;

/// <summary>
/// Decoder for WiiU GTX texture format.
/// </summary>
public class GtxDecoder : IImageDecoder
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
            // Check for wrapper header like XTX decoder does
            byte[] header = new byte[7];
            if (stream.Read(header, 0, header.Length) == header.Length && 
                header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && 
                header[3] == 0x09 && header[4] == (byte)'T' && header[5] == (byte)'E' && 
                header[6] == (byte)'X')
            {
                // Game texture wrapper found, skip to actual GTX data at offset 0x2C
                if (stream.CanSeek)
                    stream.Seek(pos + 0x2C, SeekOrigin.Begin);
            }
            else
            {
                // No wrapper, reset to original position
                if (stream.CanSeek)
                    stream.Seek(pos, SeekOrigin.Begin);
            }

            Image<Bgra32> decoded = GTX.GetImage(stream);

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
