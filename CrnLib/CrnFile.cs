namespace CrnLib;

internal static class CrnFile
{
    public static bool Validate(ReadOnlySpan<byte> crnData)
    {
        try
        {
            CrnHeader header = CrnHeader.Read(crnData);
            if (!CrnHeader.HasValidChecksums(crnData, header))
                return false;

            ValidateLevelOffsets(header);
            ValidateChunk(header.ColorEndpoints, header.DataSize, "color endpoint palette");
            ValidateChunk(header.ColorSelectors, header.DataSize, "color selector palette");
            ValidateChunk(header.AlphaEndpoints, header.DataSize, "alpha endpoint palette");
            ValidateChunk(header.AlphaSelectors, header.DataSize, "alpha selector palette");
            ValidateChunk(header.TablesOffset, header.TablesSize, header.DataSize, "Huffman table");
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static CrnFileInfo GetFileInfo(ReadOnlySpan<byte> crnData)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        int[] levelCompressedSizes = new int[header.Levels];
        if (!header.IsSegmented)
        {
            for (int i = 0; i < header.Levels; i++)
            {
                CrnLevelDataRange range = GetLevelDataRange(crnData, i);
                levelCompressedSizes[i] = range.Size;
            }
        }

        return new CrnFileInfo(
            header.DataSize,
            header.HeaderSize,
            header.ColorEndpoints.Size + header.ColorSelectors.Size + header.AlphaEndpoints.Size + header.AlphaSelectors.Size,
            header.TablesSize,
            header.Levels,
            levelCompressedSizes,
            header.ColorEndpoints.Count,
            header.ColorSelectors.Count,
            header.AlphaEndpoints.Count,
            header.AlphaSelectors.Count,
            header.IsSegmented);
    }

    public static CrnLevelInfo GetLevelInfo(ReadOnlySpan<byte> crnData, int level)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        if ((uint)level >= (uint)header.Levels)
            throw new ArgumentOutOfRangeException(nameof(level));

        int width = Math.Max(1, header.Width >> level);
        int height = Math.Max(1, header.Height >> level);
        return new CrnLevelInfo(
            width,
            height,
            header.Faces,
            (width + 3) >> 2,
            (height + 3) >> 2,
            header.BytesPerBlock,
            header.Format);
    }

    public static CrnLevelDataRange GetLevelDataRange(ReadOnlySpan<byte> crnData, int level)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        if (header.IsSegmented)
            throw new InvalidOperationException("Segmented CRN files keep mip level payloads outside the base file.");

        if ((uint)level >= (uint)header.Levels)
            throw new ArgumentOutOfRangeException(nameof(level));

        int offset = header.LevelOffsets[level];
        int nextOffset = level + 1 < header.Levels ? header.LevelOffsets[level + 1] : header.DataSize;
        if (offset < 0 || nextOffset < offset || nextOffset > header.DataSize || nextOffset > crnData.Length)
            throw new InvalidDataException("CRN mip level data points outside the file.");

        return new CrnLevelDataRange(offset, nextOffset - offset);
    }

    public static byte[] GetLevelData(ReadOnlySpan<byte> crnData, int level)
    {
        CrnLevelDataRange range = GetLevelDataRange(crnData, level);
        return crnData.Slice(range.Offset, range.Size).ToArray();
    }

    public static int GetSegmentedFileSize(ReadOnlySpan<byte> crnData)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        int size = header.HeaderSize;
        size = Math.Max(size, CheckedEnd(header.ColorEndpoints, "color endpoint palette"));
        size = Math.Max(size, CheckedEnd(header.ColorSelectors, "color selector palette"));
        size = Math.Max(size, CheckedEnd(header.AlphaEndpoints, "alpha endpoint palette"));
        size = Math.Max(size, CheckedEnd(header.AlphaSelectors, "alpha selector palette"));
        size = Math.Max(size, CheckedEnd(header.TablesOffset, header.TablesSize, "Huffman table"));

        if (size > header.DataSize || size > crnData.Length)
            throw new InvalidDataException("CRN base data points outside the file.");

        return size;
    }

    public static byte[] CreateSegmentedFile(ReadOnlySpan<byte> crnData)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        if (header.IsSegmented)
            throw new InvalidOperationException("The CRN file is already segmented.");

        int baseDataSize = GetSegmentedFileSize(crnData);
        byte[] baseData = crnData[..baseDataSize].ToArray();
        CrnHeader.WriteFlags(baseData, (ushort)(header.Flags | CrnHeader.SegmentedFlag));
        CrnHeader.WriteDataSize(baseData, baseDataSize);
        CrnHeader.FinalizeChecksums(baseData, header.HeaderSize);
        return baseData;
    }

    private static void ValidateLevelOffsets(CrnHeader header)
    {
        if (header.LevelOffsets.Length != header.Levels)
            throw new InvalidDataException("CRN header level offset table is incomplete.");

        if (header.IsSegmented)
            return;

        int previousOffset = header.HeaderSize;
        for (int i = 0; i < header.LevelOffsets.Length; i++)
        {
            int offset = header.LevelOffsets[i];
            if (offset < previousOffset || offset > header.DataSize)
                throw new InvalidDataException("CRN mip level offsets are invalid.");

            previousOffset = offset;
        }
    }

    private static void ValidateChunk(CrnPalette palette, int dataSize, string name)
    {
        ValidateChunk(palette.Offset, palette.Size, dataSize, name);
    }

    private static void ValidateChunk(int offset, int size, int dataSize, string name)
    {
        if (size == 0)
            return;

        if (offset < 0 || size < 0 || offset > dataSize || size > dataSize - offset)
            throw new InvalidDataException($"CRN {name} points outside the file.");
    }

    private static int CheckedEnd(CrnPalette palette, string name)
    {
        return CheckedEnd(palette.Offset, palette.Size, name);
    }

    private static int CheckedEnd(int offset, int size, string name)
    {
        if (offset < 0 || size < 0)
            throw new InvalidDataException($"CRN {name} has invalid bounds.");

        return checked(offset + size);
    }
}
