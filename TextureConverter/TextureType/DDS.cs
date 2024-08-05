using Pfim;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

namespace TextureConverter.TextureType;

public class DDS
{
    public enum DDSFormat
    {
        // Common formats
        ETC1,
        DXT1,
        DXT3,
        DXT5,
        BC1,
        BC2,
        BC3,
        BC4U,
        BC4S,
        BC5U,
        BC5S,
        // NVN formats
        RGBA8,
        RGBA_SRGB,
        RGB10A2,
        RGB565,
        RGB5A1,
        RGBA4,
        L8,
        LA8,
        LA4
    }

    public static readonly DDSFormat[] BCnFormats =
    [
        DDSFormat.DXT1,
        DDSFormat.DXT3,
        DDSFormat.DXT5,
        DDSFormat.BC1,
        DDSFormat.BC2,
        DDSFormat.BC3,
        DDSFormat.BC4U,
        DDSFormat.BC4S,
        DDSFormat.BC5U,
        DDSFormat.BC5S
    ];

    internal static Image<Bgra32> GetImage(string inputPath)
    {
        using IImage image = Pfimage.FromFile(inputPath);
        if (image.Format != ImageFormat.Rgba32)
            throw new Exception("Image is not in Rgba32 format!");

        Image<Bgra32> newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
        return newImage;
    }

    internal static byte[] GenerateHeader(uint mipCount, uint width, uint height, DDSFormat format, (uint, uint, uint, uint) compSel, uint size)
    {
        bool compressed = BCnFormats.Contains(format);
        byte[] hdr = new byte[128];
        (uint, uint, uint, uint, uint) compSels = (0, 0, 0, 0, 0);

        bool luminance = false;
        bool RGB = false;
        bool hasAlpha = false;
        uint fmtBPP = 0;

        switch (format)
        {
            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA8:
            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB:
            case DDSFormat.RGBA8:
            case DDSFormat.RGBA_SRGB:
                RGB = true;
                compSels = (0xFF, 0xFF00, 0xFF0000, 0xFF000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB10A2:
            case DDSFormat.RGB10A2:
                RGB = true;
                compSels = (0x3FF00000, 0xFFC00, 0x3FF, 0xC0000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB565:
            case DDSFormat.RGB565:
                RGB = true;
                compSels = (0x1F, 0x7E0, 0xF800, 0, 0);
                fmtBPP = 2;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB5A1:
            case DDSFormat.RGB5A1:
                RGB = true;
                compSels = (0x1F, 0x3E0, 0x7C00, 0x8000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA4:
            case DDSFormat.RGBA4:
                RGB = true;
                compSels = (0xF, 0xF0, 0xF00, 0xF000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_R8:
            case DDSFormat.L8:
                luminance = true;
                compSels = (0xFF, 0, 0, 0, 0);
                fmtBPP = 1;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RG8:
            case DDSFormat.LA8:
                luminance = true;
                compSels = (0xFF, 0xFF00, 0, 0, 0);
                fmtBPP = 2;
                break;
        }

        uint flags = 0x00000001 | 0x00001000 | 0x00000004 | 0x00000002;
        uint caps = 0x00001000;

        byte[] fourCC = new byte[4];

        if (mipCount == 0)
            mipCount = 1;
        else if (mipCount != 1)
        {
            flags |= 0x00020000;
            caps |= 0x00400008;
        }

        uint pFlags;
        if (compressed)
        {
            flags |= 0x00080000;
            pFlags = 0x00000004;

            fourCC = format switch
            {
                DDSFormat.ETC1 => Encoding.ASCII.GetBytes("ETC1"),
                DDSFormat.DXT1 => Encoding.ASCII.GetBytes("DXT1"),
                DDSFormat.DXT3 => Encoding.ASCII.GetBytes("DXT3"),
                DDSFormat.DXT5 => Encoding.ASCII.GetBytes("DXT5"),
                DDSFormat.BC1 => Encoding.ASCII.GetBytes("DXT1"),
                DDSFormat.BC2 => Encoding.ASCII.GetBytes("DXT3"),
                DDSFormat.BC3 => Encoding.ASCII.GetBytes("DXT5"),
                DDSFormat.BC4U => Encoding.ASCII.GetBytes("ATI1"),
                DDSFormat.BC4S => Encoding.ASCII.GetBytes("BC4S"),
                DDSFormat.BC5U => Encoding.ASCII.GetBytes("BC5U"),
                DDSFormat.BC5S => Encoding.ASCII.GetBytes("BC5S"),
                _ => throw new Exception("Unsupported format!"),
            };
        }
        else
        {
            flags |= 0x00080000;

            bool a = false;

            if (compSel.Item1 != 0 && compSel.Item2 != 0 && compSel.Item3 != 0 && compSel.Item4 != 0)
            {
                a = true;
                pFlags = 0x00000002;
            }
            else if (luminance)
            {
                pFlags = 0x00020000;
            }
            else if (RGB)
            {
                pFlags = 0x00000040;
            }
            else
                throw new Exception("Unsupported format!");

            if (hasAlpha && !a)
                pFlags |= 0x00000001;

            size = width * fmtBPP;
        }

        Array.Copy(Encoding.ASCII.GetBytes("DDS "), 0, hdr, 0, 4);
        Array.Copy(BitConverter.GetBytes((uint)124), 0, hdr, 4, 4);
        Array.Copy(BitConverter.GetBytes(flags), 0, hdr, 8, 4);
        Array.Copy(BitConverter.GetBytes(height), 0, hdr, 12, 4);
        Array.Copy(BitConverter.GetBytes(width), 0, hdr, 16, 4);
        Array.Copy(BitConverter.GetBytes(size), 0, hdr, 20, 4);
        Array.Copy(BitConverter.GetBytes(mipCount), 0, hdr, 28, 4);
        Array.Copy(BitConverter.GetBytes((uint)32), 0, hdr, 76, 4);
        Array.Copy(BitConverter.GetBytes(pFlags), 0, hdr, 80, 4);

        uint[] compSelsArr = [
            compSels.Item1,
            compSels.Item2,
            compSels.Item3,
            compSels.Item4,
            compSels.Item5
            ];

        if (compressed)
            Array.Copy(fourCC, 0, hdr, 84, 4);
        else
        {
            Array.Copy(BitConverter.GetBytes(fmtBPP << 3), 0, hdr, 88, 4);

            Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item1]), 0, hdr, 92, 4);
            Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item2]), 0, hdr, 96, 4);
            Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item3]), 0, hdr, 100, 4);
            Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item4]), 0, hdr, 104, 4);
        }

        Array.Copy(BitConverter.GetBytes(caps), 0, hdr, 108, 4);

        hdr = format switch
        {
            DDSFormat.BC4U => [.. hdr, .. new byte[] { 0x50, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC4S => [.. hdr, .. new byte[] { 0x51, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC5U => [.. hdr, .. new byte[] { 0x53, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC5S => [.. hdr, .. new byte[] { 0x54, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            _ => hdr,
        };

        return hdr;
    }
}