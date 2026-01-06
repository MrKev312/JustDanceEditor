namespace TextureConverter.TextureType;

internal sealed record GtxParseResult(GTX.GTXHeader Header, List<GTX.GX2Surface> Surfaces, List<byte[]> ImageDatas, Dictionary<uint, byte[]> MipDatas, List<GTX.GTXDataBlock> Blocks);

internal static class GtxReader
{
    public static GtxParseResult Parse(EndianBinaryReader reader)
    {
        GTX.GTXHeader header = new(reader);

        bool shiftedType = header.MajorVersion switch
        {
            6 when header.MinorVersion == 0 => false,
            6 or 7 => true,
            _ => throw new Exception($"Unsupported GTX version {header.MajorVersion}"),
        };

        if (header.GpuVersion != 2)
            throw new Exception($"Unsupported GPU version {header.GpuVersion}");

        bool blockB = false;
        bool blockC = false;

        uint imageInfo = 0;
        uint images = 0;

        List<GTX.GX2Surface> surfaces = [];
        List<byte[]> imagesData = [];
        Dictionary<uint, byte[]> mipDatas = [];
        List<GTX.GTXDataBlock> blocks = [];

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            GTX.GTXDataBlock block = new(reader, shiftedType);
            blocks.Add(block);

            if (block.BlockType == GTX.BlockType.EndOfFile)
                break;

            switch (block.BlockType)
            {
                case GTX.BlockType.SurfaceInfo:
                    imageInfo++;
                    blockB = true;

                    MemoryStream stream = new(block.Data);
                    EndianBinaryReader dataReader = new(stream, true);
                    GTX.GX2Surface surface = new(dataReader);

                    if (surface.TileMode is 0 or > 16)
                        throw new Exception($"Invalid tileMode {surface.TileMode}!");

                    if (surface.MipCount > 14)
                        throw new Exception($"Invalid number of mip maps {surface.MipCount}!");

                    surfaces.Add(surface);
                    break;
                case GTX.BlockType.SurfaceData:
                    images++;
                    blockC = true;

                    imagesData.Add(block.Data);
                    break;
                case GTX.BlockType.MipData2:
                    if (!blockC)
                        throw new Exception("MipData2 block without SurfaceData block!");

                    mipDatas.Add(images - 1, block.Data);
                    break;
                default:
                    break;
            }
        }

        if (imageInfo != images)
            throw new Exception("Number of imageInfo blocks does not match number of image blocks!");

        if (!blockB || !blockC)
            throw new Exception("Missing SurfaceInfo or SurfaceData block!");

        return new GtxParseResult(header, surfaces, imagesData, mipDatas, blocks);
    }
}