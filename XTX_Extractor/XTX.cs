using System.Text;

namespace XTX_Extractor;

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

    private readonly XTXImageFormat[] BCnFormats =
    [
        XTXImageFormat.DXT1,
        XTXImageFormat.DXT3,
        XTXImageFormat.DXT5,
        XTXImageFormat.BC4U,
        XTXImageFormat.BC4S,
        XTXImageFormat.BC5U,
        XTXImageFormat.BC5S
    ];

    private static int GetBPP(XTXImageFormat format)
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

        BinaryReader reader = new(data);
        string Signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Signature != "DFvN")
            throw new Exception($"Invalid signature {Signature}! Expected DFvN.");

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
                    BinaryReader dataReader = new(stream);
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

        Console.WriteLine($"Found {ImageInfo} images and {images} data blocks.");
    }

    public void DeswizzleData()
    {

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

        public BlockHeader(BinaryReader reader)
        {
            long pos = reader.BaseStream.Position;

            string Signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (Signature != "HBvN")
                throw new Exception($"Invalid signature {Signature}! Expected HBvN.");

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

        public (int, int, int, int) GetCompSel()
        {
            return Format switch
            {
                XTXImageFormat.NVN_FORMAT_R8 => (0, 0, 0, 5),
                XTXImageFormat.NVN_FORMAT_RG8 => (0, 0, 0, 1),
                XTXImageFormat.NVN_FORMAT_RGB565 => (0, 1, 2, 5),
                _ => (0, 1, 2, 3),
            };
        }

        public TextureHeader(BinaryReader reader)
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
