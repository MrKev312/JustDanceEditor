using System.Buffers.Binary;

namespace CrnLib;

internal sealed class CrnHeader
{
    public const int FixedHeaderSize = 74;
    public const int MaxLevels = 16;
    public const int MaxPaletteEntries = 8192;

    private const ushort Signature = 0x4878;

    public int HeaderSize { get; init; }
    public int DataSize { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Levels { get; init; }
    public int Faces { get; init; }
    public CrnFormat Format { get; init; }
    public uint UserData0 { get; init; }
    public uint UserData1 { get; init; }
    public CrnPalette ColorEndpoints { get; init; }
    public CrnPalette ColorSelectors { get; init; }
    public CrnPalette AlphaEndpoints { get; init; }
    public CrnPalette AlphaSelectors { get; init; }
    public int TablesOffset { get; init; }
    public int TablesSize { get; init; }
    public int[] LevelOffsets { get; init; } = [];

    public int BytesPerBlock => Format == CrnFormat.Dxt1 ? 8 : 16;

    public static CrnHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedHeaderSize)
            throw new InvalidDataException("CRN data is too small to contain a Unity Crunch header.");

        if (ReadUInt16(data, 0) != Signature)
            throw new InvalidDataException("CRN signature is invalid.");

        int headerSize = ReadUInt16(data, 2);
        int dataSize = (int)ReadUInt32(data, 6);
        if (headerSize < FixedHeaderSize || headerSize > data.Length || dataSize > data.Length)
            throw new InvalidDataException("CRN header contains invalid size fields.");

        int levels = data[16];
        if (levels is < 1 or > MaxLevels)
            throw new InvalidDataException("CRN mip level count is invalid.");

        CrnFormat format = (CrnFormat)data[18];
        if (format is not CrnFormat.Dxt1 and not CrnFormat.Dxt5)
            throw new NotSupportedException($"CRN format '{format}' is not supported by the managed DXT path.");

        int[] levelOffsets = new int[levels];
        for (int i = 0; i < levels; i++)
        {
            int offset = 70 + (i * 4);
            if (offset + 4 > headerSize)
                throw new InvalidDataException("CRN header does not contain all mip level offsets.");

            levelOffsets[i] = (int)ReadUInt32(data, offset);
        }

        return new CrnHeader
        {
            HeaderSize = headerSize,
            DataSize = dataSize,
            Width = ReadUInt16(data, 12),
            Height = ReadUInt16(data, 14),
            Levels = levels,
            Faces = data[17],
            Format = format,
            UserData0 = ReadUInt32(data, 25),
            UserData1 = ReadUInt32(data, 29),
            ColorEndpoints = ReadPalette(data, 33),
            ColorSelectors = ReadPalette(data, 41),
            AlphaEndpoints = ReadPalette(data, 49),
            AlphaSelectors = ReadPalette(data, 57),
            TablesSize = ReadUInt16(data, 65),
            TablesOffset = ReadUInt24(data, 67),
            LevelOffsets = levelOffsets,
        };
    }

    public static void Write(
        Span<byte> destination,
        int headerSize,
        int dataSize,
        int width,
        int height,
        int levels,
        CrnFormat format,
        uint userData0,
        uint userData1,
        CrnPalette colorEndpoints,
        CrnPalette colorSelectors,
        CrnPalette alphaEndpoints,
        CrnPalette alphaSelectors,
        int tablesOffset,
        int tablesSize,
        ReadOnlySpan<int> levelOffsets)
    {
        if (destination.Length < headerSize)
            throw new ArgumentException("Destination is smaller than the CRN header.", nameof(destination));

        destination[..headerSize].Clear();
        WriteUInt16(destination, 0, Signature);
        WriteUInt16(destination, 2, (ushort)headerSize);
        WriteUInt32(destination, 6, (uint)dataSize);
        WriteUInt16(destination, 12, (ushort)width);
        WriteUInt16(destination, 14, (ushort)height);
        destination[16] = (byte)levels;
        destination[17] = 1;
        destination[18] = (byte)format;
        WriteUInt32(destination, 25, userData0);
        WriteUInt32(destination, 29, userData1);
        WritePalette(destination, 33, colorEndpoints);
        WritePalette(destination, 41, colorSelectors);
        WritePalette(destination, 49, alphaEndpoints);
        WritePalette(destination, 57, alphaSelectors);
        WriteUInt16(destination, 65, (ushort)tablesSize);
        WriteUInt24(destination, 67, tablesOffset);

        for (int i = 0; i < levelOffsets.Length; i++)
            WriteUInt32(destination, 70 + (i * 4), (uint)levelOffsets[i]);
    }

    public static void FinalizeChecksums(Span<byte> file, int headerSize)
    {
        ushort dataCrc = Crc16.Compute(file[headerSize..]);
        WriteUInt16(file, 10, dataCrc);

        ushort headerCrc = Crc16.Compute(file[6..headerSize]);
        WriteUInt16(file, 4, headerCrc);
    }

    private static CrnPalette ReadPalette(ReadOnlySpan<byte> data, int offset)
    {
        return new CrnPalette(
            ReadUInt24(data, offset),
            ReadUInt24(data, offset + 3),
            ReadUInt16(data, offset + 6));
    }

    private static void WritePalette(Span<byte> data, int offset, CrnPalette palette)
    {
        WriteUInt24(data, offset, palette.Offset);
        WriteUInt24(data, offset + 3, palette.Size);
        WriteUInt16(data, offset + 6, (ushort)palette.Count);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data, int offset)
    {
        return (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    }

    private static void WriteUInt16(Span<byte> data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);
    }

    private static void WriteUInt32(Span<byte> data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);
    }

    private static void WriteUInt24(Span<byte> data, int offset, int value)
    {
        if ((uint)value > 0xFFFFFF)
            throw new InvalidDataException("CRN 24-bit field overflow.");

        data[offset] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }
}

internal readonly record struct CrnPalette(int Offset, int Size, int Count);
