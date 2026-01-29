using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public class GameTextureDetector : IImageFormatDetector
{
    public int HeaderSize => 0x30;

    private static readonly byte[] TexHeader = [0x00, 0x00, 0x00, 0x09, 0x54, 0x45, 0x58];

    public bool TryDetectFormat(ReadOnlySpan<byte> header, out IImageFormat format)
    {
        format = null!;

        if (header.Length < 4)
            return false;

        bool hasTex = false;
        if (header.Length >= TexHeader.Length)
            hasTex = header[..TexHeader.Length].SequenceEqual(TexHeader);

        int offset = hasTex ? 0x2C : 0;

        if (header.Length < offset + 4)
            return false;

        ReadOnlySpan<byte> sig = header.Slice(offset, 4);

        // Little Endian DDS
        if (sig.SequenceEqual([(byte)'D', (byte)'D', (byte)'S', (byte)' ']))
        {
            format = DdsFormat.Instance;
            return true;
        }

        // Big Endian DDS (Wii) - Magic is " SDD"
        if (sig.SequenceEqual([(byte)' ', (byte)'S', (byte)'D', (byte)'D']))
        {
            format = SsdFormat.Instance;
            return true;
        }

        if (sig.SequenceEqual([(byte)'D', (byte)'F', (byte)'v', (byte)'N']))
        {
            format = XtxFormat.Instance;
            return true;
        }

        if (sig.SequenceEqual([(byte)'G', (byte)'f', (byte)'x', (byte)'2']))
        {
            format = GtxFormat.Instance;
            return true;
        }

        return false;
    }
}