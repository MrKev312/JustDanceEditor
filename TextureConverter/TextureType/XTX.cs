using Pfim;

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

using System.Text;
using static TextureConverter.TextureType.DDS;
using TextureConverter.TextureConverterHelpers;

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

    public uint HeaderSize { get; set; }
    public uint MajorVersion { get; set; }
    public uint MinorVersion { get; set; }
    public List<BlockHeader> Blocks { get; set; } = [];
    public List<TextureHeader> TextureInfos { get; set; } = [];
    public List<byte[]> TextureBlocks { get; set; } = [];

    public void LoadFile(Stream data)
    {
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

        reader.BaseStream.Seek(HeaderSize, SeekOrigin.Begin);

        bool blockB = false;
        bool blockC = false;

        uint ImageInfo = 0;
        uint images = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            BlockHeader blockHeader = new(reader);
            Blocks.Add(blockHeader);

            switch (blockHeader.BlockType)
            {
                case BlockType.Texture:
                    ImageInfo += 1;
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

        // Using pfim, we can convert from DDS to PNG
        using IImage image = Pfimage.FromStream(memoryStream);
        if (image.Format != ImageFormat.Rgba32)
            throw new Exception("Image is not in Rgba32 format!");

        Image<Bgra32> newImage = Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height);

        return newImage;
    }

    public static Image<Bgra32> GetImage(string inputPath)
    {
        XTX xtx = new();
        using FileStream fileStream = new(inputPath, FileMode.Open, FileAccess.Read);
        xtx.LoadFile(fileStream);

        return xtx.ConvertToImage();
    }

    public (byte[][] data, byte[] hdr) DeswizzleData(int i)
    {
        TextureHeader texInfo = TextureInfos[i];
        byte[] data = TextureBlocks[i];

        if (texInfo.Depth != 1)
            throw new Exception("Deswizzling only supported for 2D textures!");

        int bpp = GetBPP(texInfo.Format);

        // Convert xtx format to dds format
        DDSFormat ddsFormat = texInfo.Format switch
        {
            XTXImageFormat.NVN_FORMAT_RGBA8 => DDSFormat.RGBA8,
            XTXImageFormat.NVN_FORMAT_RGBA8_SRGB => DDSFormat.RGBA_SRGB,
            XTXImageFormat.NVN_FORMAT_RGB10A2 => DDSFormat.RGB10A2,
            XTXImageFormat.NVN_FORMAT_RGB565 => DDSFormat.RGB565,
            XTXImageFormat.NVN_FORMAT_RGB5A1 => DDSFormat.RGB5A1,
            XTXImageFormat.NVN_FORMAT_RGBA4 => DDSFormat.RGBA4,
            XTXImageFormat.NVN_FORMAT_R8 => DDSFormat.L8,
            XTXImageFormat.NVN_FORMAT_RG8 => DDSFormat.LA8,
            // BCn formats
            XTXImageFormat.DXT1 => DDSFormat.BC1,
            XTXImageFormat.DXT3 => DDSFormat.BC2,
            XTXImageFormat.DXT5 => DDSFormat.BC3,
            XTXImageFormat.BC4U => DDSFormat.BC4U,
            XTXImageFormat.BC4S => DDSFormat.BC4S,
            XTXImageFormat.BC5U => DDSFormat.BC5U,
            XTXImageFormat.BC5S => DDSFormat.BC5S,

            _ => throw new Exception("Invalid format!")
        };

        byte[][] result = new byte[texInfo.MipCount][];
        for (int level = 0; level < texInfo.MipCount; level++)
        {
            int size = BCnFormats.Contains(ddsFormat)
                ? (int)(((Math.Max(1, texInfo.Width >> level) + 3) >> 2) * ((Math.Max(1, texInfo.Height >> level) + 3) >> 2) * bpp)
                : (int)(Math.Max(1, texInfo.Width >> level) * Math.Max(1, texInfo.Height >> level) * bpp);
            int mipOffset = (int)texInfo.MipOffsets[level];

            byte[] mipData = data.Skip(mipOffset).Take(size).ToArray();
            byte[] deswizzled = Swizzle.Deswizzle(Math.Max(1, texInfo.Width >> level), Math.Max(1, texInfo.Height >> level), texInfo.Format, mipData);
            result[level] = deswizzled.Take(size).ToArray();
        }

        byte[] hdr = GenerateHeader(texInfo.MipCount, texInfo.Width, texInfo.Height, ddsFormat, texInfo.GetCompSel(), (uint)texInfo.DataSize);

        return (result, hdr);
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
            long pos = reader.BaseStream.Position;

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "HBvN")
                throw new Exception($"Invalid signature {signature}! Expected HBvN.");

            BlockSize = reader.ReadUInt32();
            DataSize = reader.ReadUInt64();
            DataOffset = reader.ReadInt64();
            BlockType = (BlockType)reader.ReadUInt32();
            GlobalBlockIndex = reader.ReadUInt32();
            IncBlockTypeIndex = reader.ReadUInt32();

            reader.BaseStream.Seek(pos + DataOffset, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);
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
}
