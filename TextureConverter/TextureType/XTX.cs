using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.TextureConverterHelpers;

using static TextureConverter.TextureType.DDS;

namespace TextureConverter.TextureType;

public class XTX
{
    public enum XTXImageFormat : uint
    {
        NVN_FORMAT_RGBA8 = 0x00000025,
        NVN_FORMAT_RGBA8_SRGB = 0x00000038,
        NVN_FORMAT_RGB10A2 = 0x0000003d,
        NVN_FORMAT_RGB565 = 0x0000003c,
        NVN_FORMAT_RGB5A1 = 0x0000003b,
        NVN_FORMAT_RGBA4 = 0x00000039,
        NVN_FORMAT_R8 = 0x00000001,
        NVN_FORMAT_RG8 = 0x0000000d,
        DXT1 = 0x00000042,
        DXT3 = 0x00000043,
        DXT5 = 0x00000044,
        BC4U = 0x00000049,
        BC4S = 0x0000004a,
        BC5U = 0x0000004b,
        BC5S = 0x0000004c
    };

    public static int GetBPP(XTXImageFormat format)
    {
        return format switch
        {
            XTXImageFormat.NVN_FORMAT_RGBA8 or XTXImageFormat.NVN_FORMAT_RGBA8_SRGB or XTXImageFormat.NVN_FORMAT_RGB10A2 => 4,
            XTXImageFormat.NVN_FORMAT_RGB565 or XTXImageFormat.NVN_FORMAT_RGB5A1 or XTXImageFormat.NVN_FORMAT_RGBA4 => 2,
            XTXImageFormat.NVN_FORMAT_R8 => 1,
            XTXImageFormat.NVN_FORMAT_RG8 => 2,
            XTXImageFormat.DXT1 => 8,
            XTXImageFormat.DXT3 or XTXImageFormat.DXT5 => 16,
            XTXImageFormat.BC4U or XTXImageFormat.BC4S => 8,
            XTXImageFormat.BC5U or XTXImageFormat.BC5S => 16,
            _ => 0
        };
    }

    public enum BlockType : uint
    {
        Texture = 2,
        Data = 3,
    }

    long dataOffset = 0;
    public uint HeaderSize { get; set; }
    public uint MajorVersion { get; set; }
    public uint MinorVersion { get; set; }
    public List<BlockHeader> Blocks { get; set; } = [];
    public List<TextureHeader> TextureInfos { get; set; } = [];
    public List<byte[]> TextureBlocks { get; set; } = [];

    public void LoadFile(Stream data)
    {
        dataOffset = data.Position;

        Blocks = [];
        TextureInfos = [];
        TextureBlocks = [];

        EndianBinaryReader reader = new(data);
        string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (signature != "DFvN")
            throw new Exception($"Invalid signature {signature}! Expected DFvN.");

        HeaderSize = reader.ReadUInt32();
        MajorVersion = reader.ReadUInt32();
        MinorVersion = reader.ReadUInt32();

        reader.BaseStream.Seek(dataOffset + HeaderSize, SeekOrigin.Begin);

        bool blockB = false;
        bool blockC = false;

        uint imageInfo = 0;
        uint images = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            BlockHeader blockHeader = new(reader);
            Blocks.Add(blockHeader);

            switch (blockHeader.BlockType)
            {
                case BlockType.Texture:
                    imageInfo += 1;
                    blockB = true;

                    MemoryStream stream = new(blockHeader.Data);
                    EndianBinaryReader dataReader = new(stream);
                    TextureHeader textureHeader = new(dataReader);
                    TextureInfos.Add(textureHeader);
                    break;

                case BlockType.Data:
                    images += 1;
                    blockC = true;

                    TextureBlocks.Add(blockHeader.Data);
                    break;
            }
        }

        if (!blockB || !blockC)
            throw new Exception("Invalid XTX file! Missing texture or data block.");
    }

    public Image<Bgra32> ConvertToImage()
    {
        (byte[][] data, byte[] hdr) = DeswizzleData(0);

        byte[] output = [.. hdr, .. data.SelectMany(x => x)];

        MemoryStream memoryStream = new(output);
        memoryStream.Seek(0, SeekOrigin.Begin);

        return DDS.GetImage(memoryStream);
    }

    public static Image<Bgra32> GetImage(Stream data)
    {
        XTX xtx = new();
        xtx.LoadFile(data);

        return xtx.ConvertToImage();
    }

    public (byte[][] data, byte[] hdr) DeswizzleData(int i)
    {
        TextureHeader texInfo = TextureInfos[i];
        byte[] data = TextureBlocks[i];

        if (texInfo.Depth != 1)
            throw new Exception("Deswizzling only supported for 2D textures!");

        int bpp = GetBPP(texInfo.Format);
        DDSFormat ddsFormat = ConvertXTXToDDSFormat(texInfo.Format);

        byte[][] result = new byte[texInfo.MipCount][];
        for (int level = 0; level < texInfo.MipCount; level++)
        {
            int linearSize = BCnFormats.Contains(ddsFormat)
                ? (int)(((Math.Max(1, texInfo.Width >> level) + 3) >> 2) * ((Math.Max(1, texInfo.Height >> level) + 3) >> 2) * bpp)
                : (int)(Math.Max(1, texInfo.Width >> level) * Math.Max(1, texInfo.Height >> level) * bpp);

            int mipOffset = (int)texInfo.MipOffsets[level];

            // Calculate length of aligned mip data.
            // If offsets are provided for next level, use difference. Otherwise use rest of DataSize.
            // Writer currently writes offsets as 0, so valid primarily for single mip or if offsets are corrected.
            // We use DataSize for the single/last mip case.
            int dataLength = (level < texInfo.MipCount - 1)
                ? (int)(texInfo.MipOffsets[level + 1] - mipOffset)
                : (int)(texInfo.DataSize - (ulong)mipOffset);

            // If offsets are 0 (e.g. from our Writer), default to using remaining data for level 0.
            if (dataLength <= 0 && level == 0)
                dataLength = data.Length - mipOffset;

            // Ensure we don't exceed array bounds
            dataLength = Math.Min(dataLength, data.Length - mipOffset);

            byte[] mipData = [.. data.Skip(mipOffset).Take(dataLength)];

            byte[] deswizzled = Swizzle.Deswizzle(Math.Max(1, texInfo.Width >> level), Math.Max(1, texInfo.Height >> level), texInfo.Format, mipData);

            // Result must be linear size for final image composition
            result[level] = [.. deswizzled.Take(linearSize)];
        }

        byte[] hdr = GenerateHeader(texInfo.MipCount, texInfo.Width, texInfo.Height, ddsFormat, texInfo.GetCompSel(), (uint)texInfo.DataSize);

        return (result, hdr);
    }

    public static DDSFormat ConvertXTXToDDSFormat(XTXImageFormat format)
    {
        return format switch
        {
            XTXImageFormat.NVN_FORMAT_RGBA8 => DDSFormat.RGBA8,
            XTXImageFormat.NVN_FORMAT_RGBA8_SRGB => DDSFormat.RGBA_SRGB,
            XTXImageFormat.NVN_FORMAT_RGB10A2 => DDSFormat.RGB10A2,
            XTXImageFormat.NVN_FORMAT_RGB565 => DDSFormat.RGB565,
            XTXImageFormat.NVN_FORMAT_RGB5A1 => DDSFormat.RGB5A1,
            XTXImageFormat.NVN_FORMAT_RGBA4 => DDSFormat.RGBA4,
            XTXImageFormat.NVN_FORMAT_R8 => DDSFormat.L8,
            XTXImageFormat.NVN_FORMAT_RG8 => DDSFormat.LA8,
            XTXImageFormat.DXT1 => DDSFormat.BC1,
            XTXImageFormat.DXT3 => DDSFormat.BC2,
            XTXImageFormat.DXT5 => DDSFormat.BC3,
            XTXImageFormat.BC4U => DDSFormat.BC4U,
            XTXImageFormat.BC4S => DDSFormat.BC4S,
            XTXImageFormat.BC5U => DDSFormat.BC5U,
            XTXImageFormat.BC5S => DDSFormat.BC5S,
            _ => throw new Exception("Invalid format!")
        };
    }

    public class BlockHeader
    {
        public uint BlockSize { get; set; }
        public ulong DataSize { get; set; }
        public long DataOffset { get; set; }
        public BlockType BlockType { get; set; }
        public uint GlobalBlockIndex { get; set; }
        public uint IncBlockTypeIndex { get; set; }

        public byte[] Data { get; set; } = [];

        public BlockHeader(EndianBinaryReader reader)
        {
            long blockHeaderStartPosition = reader.BaseStream.Position;

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "HBvN")
                throw new Exception($"Invalid signature {signature}! Expected HBvN.");

            BlockSize = reader.ReadUInt32();
            DataSize = reader.ReadUInt64();
            DataOffset = reader.ReadInt64();
            BlockType = (BlockType)reader.ReadUInt32();
            GlobalBlockIndex = reader.ReadUInt32();
            IncBlockTypeIndex = reader.ReadUInt32();

            reader.BaseStream.Seek(blockHeaderStartPosition + DataOffset, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);

            // Align
            for (long pos = reader.BaseStream.Position; pos + 4 <= reader.BaseStream.Length; pos++)
            {
                reader.BaseStream.Seek(pos, SeekOrigin.Begin);
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "HBvN")
                    continue;
                reader.BaseStream.Seek(pos, SeekOrigin.Begin);
                break;
            }
        }
    }

    public class TextureHeader
    {
        public ulong DataSize { get; set; }
        public uint Alignment { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Depth { get; set; }
        public uint Target { get; set; }
        public XTXImageFormat Format { get; set; }
        public uint MipCount { get; set; }
        public uint SliceSize { get; set; }
        public uint[] MipOffsets { get; set; }
        public uint TextureLayout1;
        public uint TextureLayout2;
        public uint Boolean;

        public (uint, uint, uint, uint) GetCompSel()
        {
            return Format switch
            {
                XTXImageFormat.NVN_FORMAT_R8 => (0, 0, 0, 5),
                XTXImageFormat.NVN_FORMAT_RG8 => (0, 0, 0, 1),
                XTXImageFormat.NVN_FORMAT_RGB565 => (0, 1, 2, 5),
                _ => (0, 1, 2, 3),
            };
        }

        public TextureHeader(EndianBinaryReader reader)
        {
            DataSize = reader.ReadUInt64();
            Alignment = reader.ReadUInt32();
            Width = reader.ReadUInt32();
            Height = reader.ReadUInt32();
            Depth = reader.ReadUInt32();
            Target = reader.ReadUInt32();
            Format = (XTXImageFormat)reader.ReadInt32();
            MipCount = reader.ReadUInt32();
            SliceSize = reader.ReadUInt32();
            MipOffsets = new uint[MipCount];
            for (int i = 0; i < MipCount; i++)
                MipOffsets[i] = reader.ReadUInt32();
            TextureLayout1 = reader.ReadUInt32();
            TextureLayout2 = reader.ReadUInt32();
            Boolean = reader.ReadUInt32();
        }
    }

    public static void ConvertToFile(Image<Bgra32> image, XTXImageFormat format, Stream output)
    {
        uint width = (uint)image.Width;
        uint height = (uint)image.Height;
        uint mipCount = 1;
        uint alignment = 2048;

        byte[] imageData = ConvertImageDataToFormat(image, format);
        byte[] swizzledData = Swizzle.SwizzleData(width, height, format, imageData);

        WriteXTXFile(output, width, height, mipCount, format, alignment, swizzledData);
    }

    private static void WriteXTXFile(Stream output, uint width, uint height, uint mipCount, XTXImageFormat format, uint alignment, byte[] textureData)
    {
        using EndianBinaryWriter writer = new(output, Encoding.Default, true, false);

        writer.Write(Encoding.ASCII.GetBytes("DFvN"));
        writer.Write(48U); // HeaderSize
        writer.Write(1U);
        writer.Write(0U);

        long currentPos = output.Position;
        for (long i = currentPos; i < 48; i++)
            writer.Write((byte)0);

        WriteTextureBlockHeader(writer, width, height, mipCount, format, alignment, (uint)textureData.Length);
        WriteDataBlockHeader(writer, textureData);
    }

    private static void WriteTextureBlockHeader(EndianBinaryWriter writer, uint width, uint height, uint mipCount, XTXImageFormat format, uint alignment, uint dataSize)
    {
        MemoryStream headerData = new();
        using (EndianBinaryWriter headerWriter = new(headerData, Encoding.Default, false))
        {
            headerWriter.Write((ulong)dataSize);
            headerWriter.Write(alignment);
            headerWriter.Write(width);
            headerWriter.Write(height);
            headerWriter.Write(1U);
            headerWriter.Write(1U);
            headerWriter.Write((int)format);
            headerWriter.Write(mipCount);
            headerWriter.Write(dataSize);
            for (int i = 0; i < mipCount; i++)
                headerWriter.Write(0U);
            headerWriter.Write(0U);
            headerWriter.Write(0U);
            headerWriter.Write(0U);
        }

        byte[] data = headerData.ToArray();

        writer.Write(Encoding.ASCII.GetBytes("HBvN"));
        writer.Write(36U); // BlockSize (header size including self)
        writer.Write((ulong)data.Length);
        writer.Write(36L); // DataOffset (offset from start of this block to data)
        writer.Write(2U); // BlockType.Texture
        writer.Write(0U);
        writer.Write(0U);

        writer.Write(data);
    }

    private static void WriteDataBlockHeader(EndianBinaryWriter writer, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes("HBvN"));
        writer.Write(36U);
        writer.Write((ulong)data.Length);
        writer.Write(36L);
        writer.Write(3U); // BlockType.Data
        writer.Write(0U);
        writer.Write(0U);

        writer.Write(data);
    }

    private static byte[] ConvertImageDataToFormat(Image<Bgra32> image, XTXImageFormat format)
    {
        return format switch
        {
            XTXImageFormat.NVN_FORMAT_RGBA8 => ConvertToBGRA8(image),
            XTXImageFormat.NVN_FORMAT_RGBA8_SRGB => ConvertToBGRA8(image),
            XTXImageFormat.NVN_FORMAT_RGB10A2 => ConvertToRGB10A2(image),
            XTXImageFormat.NVN_FORMAT_RGB565 => ConvertToRGB565(image),
            XTXImageFormat.NVN_FORMAT_RGB5A1 => ConvertToRGB5A1(image),
            XTXImageFormat.NVN_FORMAT_RGBA4 => ConvertToRGBA4(image),
            XTXImageFormat.NVN_FORMAT_R8 => ConvertToR8(image),
            XTXImageFormat.NVN_FORMAT_RG8 => ConvertToRG8(image),
            _ => throw new NotImplementedException($"Format {format} is not supported.")
        };
    }

    private static byte[] ConvertToBGRA8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 4];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = row[x].B;
                    result[offset++] = row[x].G;
                    result[offset++] = row[x].R;
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGB10A2(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 4];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 6) & 0x3FF;
                    uint g = (uint)(row[x].G >> 6) & 0x3FF;
                    uint b = (uint)(row[x].B >> 6) & 0x3FF;
                    uint a = (uint)(row[x].A >> 6) & 0x3;
                    uint packed = (a << 30) | (r << 20) | (g << 10) | b;
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 4);
                    offset += 4;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGB565(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 2) & 0x3F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    ushort packed = (ushort)((r << 11) | (g << 5) | b);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGB5A1(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 3) & 0x1F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    uint a = (row[x].A > 128 ? 1U : 0U) & 0x1;
                    ushort packed = (ushort)((a << 15) | (r << 10) | (g << 5) | b);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGBA4(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 4) & 0xF;
                    uint g = (uint)(row[x].G >> 4) & 0xF;
                    uint b = (uint)(row[x].B >> 4) & 0xF;
                    uint a = (uint)(row[x].A >> 4) & 0xF;
                    ushort packed = (ushort)((a << 12) | (b << 8) | (g << 4) | r);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToR8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height];
        int offset = 0;
        image.ProcessPixelRows(accessor => {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRG8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor => {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }
}