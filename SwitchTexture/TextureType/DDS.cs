using Pfim;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

namespace SwitchTexture.TextureType;

public class DDS
{
    internal static Image<Bgra32> GetImage(string inputPath)
    {
        using IImage image = Pfimage.FromFile(inputPath);
        if (image.Format != ImageFormat.Rgba32)
            throw new Exception("Image is not in Rgba32 format!");

        Image<Bgra32> newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);
        return newImage;
    }

    internal static byte[] GenerateHeader(uint mipCount, uint width, uint height, XTX.XTXImageFormat format, (uint, uint, uint, uint) compSel, uint size, bool compressed)
    {
        byte[] hdr = new byte[128];
        (uint, uint, uint, uint, uint) compSels = (0, 0, 0, 0, 0);

        bool luminance = false;
        bool RGB = false;
        bool hasAlpha = false;
        uint fmtBPP = 0;

        switch (format)
        {
            case XTX.XTXImageFormat.NVN_FORMAT_RGBA8:
            case XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB:
                RGB = true;
                compSels = (0xFF, 0xFF00, 0xFF0000, 0xFF000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_RGB10A2:
                RGB = true;
                compSels = (0x3FF00000, 0xFFC00, 0x3FF, 0xC0000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_RGB565:
                RGB = true;
                compSels = (0x1F, 0x7E0, 0xF800, 0, 0);
                fmtBPP = 2;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_RGB5A1:
                RGB = true;
                compSels = (0x1F, 0x3E0, 0x7C00, 0x8000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_RGBA4:
                RGB = true;
                compSels = (0xF, 0xF0, 0xF00, 0xF000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_R8:
                luminance = true;
                compSels = (0xFF, 0, 0, 0, 0);
                fmtBPP = 1;
                break;

            case XTX.XTXImageFormat.NVN_FORMAT_RG8:
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

            if (format == XTX.XTXImageFormat.DXT1)
                fourCC = Encoding.ASCII.GetBytes("DXT1");
            else if (format == XTX.XTXImageFormat.DXT3)
                fourCC = Encoding.ASCII.GetBytes("DXT3");
            else if (format == XTX.XTXImageFormat.DXT5)
                fourCC = Encoding.ASCII.GetBytes("DXT5");
            else if (format == XTX.XTXImageFormat.BC4U)
                fourCC = Encoding.ASCII.GetBytes("ATI1");
            else if (format == XTX.XTXImageFormat.BC4S)
                fourCC = Encoding.ASCII.GetBytes("BC4S");
            else if (format == XTX.XTXImageFormat.BC5U)
                fourCC = Encoding.ASCII.GetBytes("BC5U");
            else if (format == XTX.XTXImageFormat.BC5S)
                fourCC = Encoding.ASCII.GetBytes("BC5S");
            else
                throw new Exception("Unsupported format!");
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

        if (compressed)
            Array.Copy(fourCC, 0, hdr, 84, 4);
        else
        {
            Array.Copy(BitConverter.GetBytes(fmtBPP << 3), 0, hdr, 88, 4);

            Array.Copy(BitConverter.GetBytes(compSels.Item1), 0, hdr, 92, 4);
            Array.Copy(BitConverter.GetBytes(compSels.Item2), 0, hdr, 96, 4);
            Array.Copy(BitConverter.GetBytes(compSels.Item3), 0, hdr, 100, 4);
            Array.Copy(BitConverter.GetBytes(compSels.Item4), 0, hdr, 104, 4);
        }

        Array.Copy(BitConverter.GetBytes(caps), 0, hdr, 108, 4);

        return hdr;
    }
}