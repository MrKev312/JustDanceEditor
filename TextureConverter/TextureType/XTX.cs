using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;

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
            // For BCn, GetBPP returns Bytes Per Block (16 bytes for 4x4 BC3)
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

    public uint HeaderSize { get; set; }
    public uint MajorVersion { get; set; }
    public uint MinorVersion { get; set; }
    public List<BlockHeader> Blocks { get; set; } = [];
    public List<TextureHeader> TextureInfos { get; set; } = [];
    public List<byte[]> TextureBlocks { get; set; } = [];

    long dataOffset = 0;

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

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            BlockHeader blockHeader = new(reader);
            Blocks.Add(blockHeader);

            switch (blockHeader.BlockType)
            {
                case BlockType.Texture:
                    blockB = true;
                    MemoryStream stream = new(blockHeader.Data);
                    EndianBinaryReader dataReader = new(stream);
                    TextureHeader textureHeader = new(dataReader);
                    TextureInfos.Add(textureHeader);
                    break;

                case BlockType.Data:
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
        int blockHeightLog2 = (int)(texInfo.TextureLayout1 & 0x7);

        byte[][] result = new byte[texInfo.MipCount][];
        for (int level = 0; level < texInfo.MipCount; level++)
        {
            int mipWidth = Math.Max(1, (int)texInfo.Width >> level);
            int mipHeight = Math.Max(1, (int)texInfo.Height >> level);

            // Logic for BCn formats (Blocks vs Pixels)
            int swizzleWidth = mipWidth;
            int swizzleHeight = mipHeight;
            bool isBCn = BCnFormats.Contains(ddsFormat);

            if (isBCn)
            {
                // Convert pixel dims to block dims
                swizzleWidth = (mipWidth + 3) / 4;
                swizzleHeight = (mipHeight + 3) / 4;
            }

            int linearSize = isBCn
                ? swizzleWidth * swizzleHeight * bpp
                : mipWidth * mipHeight * bpp;

            int mipOffset = (int)texInfo.MipOffsets[level];

            // Calculate available data length safely
            int dataLength;
            if (level < texInfo.MipCount - 1)
                dataLength = (int)(texInfo.MipOffsets[level + 1] - mipOffset);
            else
                dataLength = (int)(texInfo.DataSize - (ulong)mipOffset);

            if (dataLength <= 0 && level == 0)
                dataLength = data.Length - mipOffset; // Fallback

            dataLength = Math.Min(dataLength, data.Length - mipOffset);
            byte[] mipData = [.. data.Skip(mipOffset).Take(dataLength)];

            byte[] deswizzled = Swizzle.Deswizzle(swizzleWidth, swizzleHeight, bpp, blockHeightLog2, mipData);
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
            long startPos = reader.BaseStream.Position;
            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "HBvN")
                throw new Exception($"Invalid signature {signature}! Expected HBvN.");

            BlockSize = reader.ReadUInt32();
            DataSize = reader.ReadUInt64();
            DataOffset = reader.ReadInt64();
            BlockType = (BlockType)reader.ReadUInt32();
            GlobalBlockIndex = reader.ReadUInt32();
            IncBlockTypeIndex = reader.ReadUInt32();

            reader.BaseStream.Seek(startPos + DataOffset, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);

            // Re-align to next block for reading loop
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
            MipOffsets = new uint[17];
            for (int i = 0; i < 17; i++)
                MipOffsets[i] = reader.ReadUInt32();
            TextureLayout1 = reader.ReadUInt32();
            TextureLayout2 = reader.ReadUInt32();
            Boolean = reader.ReadUInt32();
        }
    }

    // --- ENCODING ---

    public static void ConvertToFile(Image<Bgra32> image, XTXImageFormat format, Stream output)
    {
        uint width = (uint)image.Width;
        uint height = (uint)image.Height;
        uint mipCount = 1;
        uint alignment = 512;

        // 1. Convert Image to Raw Bytes (Linear)
        byte[] imageData = ConvertImageDataToFormat(image, format);

        // 2. Prepare Dimensions for Swizzling
        int swizzleWidth = (int)width;
        int swizzleHeight = (int)height;
        int bpp = GetBPP(format);

        bool isBCn = format is XTXImageFormat.DXT1 or XTXImageFormat.DXT3 or XTXImageFormat.DXT5 or
                     XTXImageFormat.BC4U or XTXImageFormat.BC4S or
                     XTXImageFormat.BC5U or XTXImageFormat.BC5S;

        if (isBCn)
        {
            // For BCn, we operate on Blocks, not Pixels.
            // Width/Height in Blocks. BPP is bytes-per-block (16 or 8).
            swizzleWidth = ((int)width + 3) / 4;
            swizzleHeight = ((int)height + 3) / 4;
        }

        // 3. Calculate Block Height Parameter based on Dimensions
        int blockHeightLog2 = Swizzle.GetBlockHeightLog2(swizzleHeight);

        // 4. Swizzle
        byte[] swizzledData = Swizzle.SwizzleData(swizzleWidth, swizzleHeight, bpp, blockHeightLog2, imageData);

        // 5. Write
        WriteXTXFile(output, width, height, mipCount, format, alignment, swizzledData, (uint)blockHeightLog2);
    }

    private static void WriteXTXFile(Stream output, uint width, uint height, uint mipCount, XTXImageFormat format, uint alignment, byte[] textureData, uint blockHeightLog2)
    {
        using EndianBinaryWriter writer = new(output, Encoding.Default, true, false);

        // 1. File Header
        writer.Write(Encoding.ASCII.GetBytes("DFvN"));
        writer.Write(16U); // HeaderSize
        writer.Write(1U);  // Major Version
        writer.Write(1U);  // Minor Version

        // 2. Texture Info Block
        byte[] textureHeaderData = GenerateTextureHeader(width, height, mipCount, format, alignment, (uint)textureData.Length, blockHeightLog2);

        WriteBlockHeader(writer, BlockType.Texture, textureHeaderData.Length, (w) => w.Write(textureHeaderData));

        // 3. Texture Data Block
        WriteBlockHeader(writer, BlockType.Data, textureData.Length, (w) =>
        {
            // Align start of data relative to file
            long currentPos = w.BaseStream.Position;
            long alignedPos = (currentPos + (alignment - 1)) & ~(alignment - 1);
            int padding = (int)(alignedPos - currentPos);
            for (int i = 0; i < padding; i++)
                w.Write((byte)0);

            w.Write(textureData);
        });
    }

    private static byte[] GenerateTextureHeader(uint width, uint height, uint mipCount, XTXImageFormat format, uint alignment, uint dataSize, uint blockHeightLog2)
    {
        using MemoryStream ms = new();
        using EndianBinaryWriter headerWriter = new(ms, Encoding.Default, false);

        headerWriter.Write((ulong)dataSize);
        headerWriter.Write(alignment);
        headerWriter.Write(width);
        headerWriter.Write(height);
        headerWriter.Write(1U); // Depth
        headerWriter.Write(1U); // Target (2D)
        headerWriter.Write((int)format);
        headerWriter.Write(mipCount);
        headerWriter.Write(dataSize); // SliceSize

        for (int i = 0; i < 17; i++)
            headerWriter.Write(0U); // Mip Offsets (Mip 0 is at 0)

        headerWriter.Write(blockHeightLog2); // TextureLayout1

        // IMPORTANT: 0x010007 indicates Block Linear swizzling
        headerWriter.Write(0x010007U); // TextureLayout2

        headerWriter.Write(0U); // Boolean

        return ms.ToArray();
    }

    private static void WriteBlockHeader(EndianBinaryWriter writer, BlockType type, int estimatedDataSize, Action<EndianBinaryWriter> writeDataAction)
    {
        long headerStartPos = writer.BaseStream.Position;

        writer.Write(Encoding.ASCII.GetBytes("HBvN"));
        writer.Write(36U);
        writer.Write(0UL); // DataSize holder
        writer.Write(0L);  // DataOffset holder
        writer.Write((uint)type);
        writer.Write(0U);
        writer.Write(0U);

        long dataStartPos = writer.BaseStream.Position;
        writeDataAction(writer);
        long endPos = writer.BaseStream.Position;

        long calculatedDataStart = endPos - estimatedDataSize;
        long actualDataOffset = calculatedDataStart - headerStartPos;

        writer.BaseStream.Seek(headerStartPos + 4 + 4, SeekOrigin.Begin);
        writer.Write((ulong)estimatedDataSize);
        writer.Write(actualDataOffset);

        writer.BaseStream.Seek(endPos, SeekOrigin.Begin);
    }

    private static byte[] ConvertImageDataToFormat(Image<Bgra32> image, XTXImageFormat format)
    {
        return format switch
        {
            XTXImageFormat.NVN_FORMAT_RGBA8 => PixelFormatConverter.ConvertToBGRA8(image),
            XTXImageFormat.NVN_FORMAT_RGBA8_SRGB => PixelFormatConverter.ConvertToBGRA8(image),
            XTXImageFormat.NVN_FORMAT_RGB10A2 => PixelFormatConverter.ConvertToRGB10A2(image),
            XTXImageFormat.NVN_FORMAT_RGB565 => PixelFormatConverter.ConvertToRGB565(image),
            XTXImageFormat.NVN_FORMAT_RGB5A1 => PixelFormatConverter.ConvertToRGB5A1(image),
            XTXImageFormat.NVN_FORMAT_RGBA4 => PixelFormatConverter.ConvertToRGBA4(image),
            XTXImageFormat.NVN_FORMAT_R8 => PixelFormatConverter.ConvertToR8(image),
            XTXImageFormat.NVN_FORMAT_RG8 => PixelFormatConverter.ConvertToRG8(image),
            XTXImageFormat.DXT1 => CompressBCn(image, CompressionFormat.Bc1),
            XTXImageFormat.DXT3 => CompressBCn(image, CompressionFormat.Bc2),
            XTXImageFormat.DXT5 => CompressBCn(image, CompressionFormat.Bc3),
            XTXImageFormat.BC4U => CompressBCn(image, CompressionFormat.Bc4),
            XTXImageFormat.BC5U => CompressBCn(image, CompressionFormat.Bc5),
            _ => throw new NotImplementedException($"Format {format} is not supported.")
        };
    }

    private static byte[] CompressBCn(Image<Bgra32> image, CompressionFormat format)
    {
        using Image<Rgba32> rgba = new(image.Width, image.Height);
        image.ProcessPixelRows(rgba, (srcAccessor, dstAccessor) =>
        {
            for (int y = 0; y < srcAccessor.Height; y++)
            {
                Span<Bgra32> srcRow = srcAccessor.GetRowSpan(y);
                Span<Rgba32> dstRow = dstAccessor.GetRowSpan(y);
                for (int x = 0; x < srcRow.Length; x++)
                    dstRow[x] = new Rgba32(srcRow[x].R, srcRow[x].G, srcRow[x].B, srcRow[x].A);
            }
        });

        BcEncoder encoder = new()
        {
            OutputOptions = { GenerateMipMaps = false, Quality = CompressionQuality.BestQuality, Format = format }
        };
        return encoder.EncodeToRawBytes(rgba)[0];
    }
}