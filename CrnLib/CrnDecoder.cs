using System.Buffers.Binary;

namespace CrnLib;

internal static class CrnDecoder
{
    private static readonly byte[] Dxt5FromLinear = [0, 2, 3, 4, 5, 6, 7, 1];

    public static byte[] DecodeLevel(ReadOnlySpan<byte> crnData, int level)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        if (header.Faces != 1)
            throw new NotSupportedException("Managed Crunch decoding currently supports 2D textures only.");

        if ((uint)level >= (uint)header.Levels)
            throw new ArgumentOutOfRangeException(nameof(level));

        DecoderState state = CreateDecoderState(crnData, header);
        return UnpackLevel(crnData, header, state, level);
    }

    private static DecoderState CreateDecoderState(ReadOnlySpan<byte> crnData, CrnHeader header)
    {
        DecoderTables tables = DecodeTables(crnData, header);

        uint[] colorEndpoints = header.ColorEndpoints.Count > 0
            ? DecodeColorEndpoints(crnData, header, tables)
            : [];

        uint[] colorSelectors = header.ColorSelectors.Count > 0
            ? DecodeColorSelectors(crnData, header, tables)
            : [];

        ushort[] alphaEndpoints = header.AlphaEndpoints.Count > 0
            ? DecodeAlphaEndpoints(crnData, header, tables)
            : [];

        ushort[] alphaSelectors = header.AlphaSelectors.Count > 0
            ? DecodeAlphaSelectors(crnData, header, tables)
            : [];

        return new DecoderState(tables, colorEndpoints, colorSelectors, alphaEndpoints, alphaSelectors);
    }

    private static DecoderTables DecodeTables(ReadOnlySpan<byte> crnData, CrnHeader header)
    {
        BitReader reader = CreateReader(crnData, header.TablesOffset, header.TablesSize);
        StaticHuffmanModel reference = StaticHuffmanModel.Receive(reader);
        StaticHuffmanModel? colorEndpoint = null;
        StaticHuffmanModel? colorSelector = null;
        StaticHuffmanModel? alphaEndpoint = null;
        StaticHuffmanModel? alphaSelector = null;

        if (header.ColorEndpoints.Count > 0)
        {
            colorEndpoint = StaticHuffmanModel.Receive(reader);
            colorSelector = StaticHuffmanModel.Receive(reader);
        }

        if (header.AlphaEndpoints.Count > 0)
        {
            alphaEndpoint = StaticHuffmanModel.Receive(reader);
            alphaSelector = StaticHuffmanModel.Receive(reader);
        }

        return new DecoderTables(reference, colorEndpoint, colorSelector, alphaEndpoint, alphaSelector);
    }

    private static uint[] DecodeColorEndpoints(ReadOnlySpan<byte> crnData, CrnHeader header, DecoderTables tables)
    {
        if (tables.ColorEndpoint is null)
            throw new InvalidDataException("CRN color endpoint table is missing.");

        BitReader reader = CreateReader(crnData, header.ColorEndpoints.Offset, header.ColorEndpoints.Size);
        StaticHuffmanModel redBlueModel = StaticHuffmanModel.Receive(reader);
        StaticHuffmanModel greenModel = StaticHuffmanModel.Receive(reader);
        uint[] endpoints = new uint[header.ColorEndpoints.Count];

        uint r0 = 0;
        uint g0 = 0;
        uint b0 = 0;
        uint r1 = 0;
        uint g1 = 0;
        uint b1 = 0;

        for (int i = 0; i < endpoints.Length; i++)
        {
            r0 = (r0 + (uint)redBlueModel.Decode(reader)) & 31;
            g0 = (g0 + (uint)greenModel.Decode(reader)) & 63;
            b0 = (b0 + (uint)redBlueModel.Decode(reader)) & 31;
            r1 = (r1 + (uint)redBlueModel.Decode(reader)) & 31;
            g1 = (g1 + (uint)greenModel.Decode(reader)) & 63;
            b1 = (b1 + (uint)redBlueModel.Decode(reader)) & 31;

            endpoints[i] = b0 | (g0 << 5) | (r0 << 11) | (b1 << 16) | (g1 << 21) | (r1 << 27);
        }

        return endpoints;
    }

    private static uint[] DecodeColorSelectors(ReadOnlySpan<byte> crnData, CrnHeader header, DecoderTables tables)
    {
        if (tables.ColorSelector is null)
            throw new InvalidDataException("CRN color selector table is missing.");

        BitReader reader = CreateReader(crnData, header.ColorSelectors.Offset, header.ColorSelectors.Size);
        StaticHuffmanModel model = StaticHuffmanModel.Receive(reader);
        uint[] selectors = new uint[header.ColorSelectors.Count];
        uint linearSelectors = 0;

        for (int i = 0; i < selectors.Length; i++)
        {
            for (int shift = 0; shift < 32; shift += 4)
                linearSelectors ^= (uint)model.Decode(reader) << shift;

            selectors[i] = ((linearSelectors ^ (linearSelectors << 1)) & 0xAAAAAAAAu) |
                           ((linearSelectors >> 1) & 0x55555555u);
        }

        return selectors;
    }

    private static ushort[] DecodeAlphaEndpoints(ReadOnlySpan<byte> crnData, CrnHeader header, DecoderTables tables)
    {
        if (tables.AlphaEndpoint is null)
            throw new InvalidDataException("CRN alpha endpoint table is missing.");

        BitReader reader = CreateReader(crnData, header.AlphaEndpoints.Offset, header.AlphaEndpoints.Size);
        StaticHuffmanModel model = StaticHuffmanModel.Receive(reader);
        ushort[] endpoints = new ushort[header.AlphaEndpoints.Count];

        uint a = 0;
        uint b = 0;
        for (int i = 0; i < endpoints.Length; i++)
        {
            a = (a + (uint)model.Decode(reader)) & 255;
            b = (b + (uint)model.Decode(reader)) & 255;
            endpoints[i] = (ushort)(a | (b << 8));
        }

        return endpoints;
    }

    private static ushort[] DecodeAlphaSelectors(ReadOnlySpan<byte> crnData, CrnHeader header, DecoderTables tables)
    {
        if (tables.AlphaSelector is null)
            throw new InvalidDataException("CRN alpha selector table is missing.");

        BitReader reader = CreateReader(crnData, header.AlphaSelectors.Offset, header.AlphaSelectors.Size);
        StaticHuffmanModel model = StaticHuffmanModel.Receive(reader);
        ushort[] selectors = new ushort[header.AlphaSelectors.Count * 3];

        byte[] pairFromLinear = new byte[64];
        for (int i = 0; i < pairFromLinear.Length; i++)
            pairFromLinear[i] = (byte)(Dxt5FromLinear[i & 7] | (Dxt5FromLinear[i >> 3] << 3));

        uint s0Linear = 0;
        uint s1Linear = 0;
        int destination = 0;

        for (int i = 0; i < header.AlphaSelectors.Count; i++)
        {
            uint s0 = 0;
            uint s1 = 0;

            for (int shift = 0; shift < 24; shift += 6)
            {
                s0Linear ^= (uint)model.Decode(reader) << shift;
                s0 |= (uint)pairFromLinear[(s0Linear >> shift) & 0x3F] << shift;
            }

            for (int shift = 0; shift < 24; shift += 6)
            {
                s1Linear ^= (uint)model.Decode(reader) << shift;
                s1 |= (uint)pairFromLinear[(s1Linear >> shift) & 0x3F] << shift;
            }

            selectors[destination++] = (ushort)s0;
            selectors[destination++] = (ushort)((s0 >> 16) | (s1 << 8));
            selectors[destination++] = (ushort)(s1 >> 8);
        }

        return selectors;
    }

    private static byte[] UnpackLevel(ReadOnlySpan<byte> crnData, CrnHeader header, DecoderState state, int level)
    {
        int levelWidth = Math.Max(1, header.Width >> level);
        int levelHeight = Math.Max(1, header.Height >> level);
        int blocksX = (levelWidth + 3) >> 2;
        int blocksY = (levelHeight + 3) >> 2;
        int rowPitch = blocksX * header.BytesPerBlock;
        byte[] destination = new byte[rowPitch * blocksY];

        int levelOffset = header.LevelOffsets[level];
        int nextLevelOffset = level + 1 < header.Levels ? header.LevelOffsets[level + 1] : header.DataSize;
        BitReader reader = CreateReader(crnData, levelOffset, nextLevelOffset - levelOffset);

        return header.Format switch
        {
            CrnFormat.Dxt1 => UnpackDxt1(reader, state, destination, blocksX, blocksY, rowPitch),
            CrnFormat.Dxt5 => UnpackDxt5(reader, state, destination, blocksX, blocksY, rowPitch),
            _ => throw new NotSupportedException($"CRN format '{header.Format}' is not supported."),
        };
    }

    private static byte[] UnpackDxt1(BitReader reader, DecoderState state, byte[] destination, int outputWidth, int outputHeight, int rowPitch)
    {
        if (state.Tables.ColorEndpoint is null || state.Tables.ColorSelector is null)
            throw new InvalidDataException("CRN DXT1 level is missing color Huffman tables.");

        int width = (outputWidth + 1) & ~1;
        int height = (outputHeight + 1) & ~1;
        BlockBufferElement[] blockBuffer = new BlockBufferElement[width];
        int colorEndpointIndex = 0;
        int referenceGroup = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((y & 1) == 0 && (x & 1) == 0)
                    referenceGroup = state.Tables.Reference.Decode(reader);

                int endpointReference = ConsumeEndpointReference(y, x, blockBuffer, ref referenceGroup);
                if (endpointReference == 0)
                {
                    colorEndpointIndex += state.Tables.ColorEndpoint.Decode(reader);
                    if (colorEndpointIndex >= state.ColorEndpoints.Length)
                        colorEndpointIndex -= state.ColorEndpoints.Length;
                    blockBuffer[x].ColorEndpointIndex = (ushort)colorEndpointIndex;
                }
                else if (endpointReference == 1)
                {
                    blockBuffer[x].ColorEndpointIndex = (ushort)colorEndpointIndex;
                }
                else
                {
                    colorEndpointIndex = blockBuffer[x].ColorEndpointIndex;
                }

                int colorSelectorIndex = state.Tables.ColorSelector.Decode(reader);
                if (x >= outputWidth || y >= outputHeight)
                    continue;

                int destinationOffset = (y * rowPitch) + (x * 8);
                WriteUInt32(destination, destinationOffset, CheckedRead(state.ColorEndpoints, colorEndpointIndex, "color endpoint"));
                WriteUInt32(destination, destinationOffset + 4, CheckedRead(state.ColorSelectors, colorSelectorIndex, "color selector"));
            }
        }

        return destination;
    }

    private static byte[] UnpackDxt5(BitReader reader, DecoderState state, byte[] destination, int outputWidth, int outputHeight, int rowPitch)
    {
        if (state.Tables.ColorEndpoint is null || state.Tables.ColorSelector is null ||
            state.Tables.AlphaEndpoint is null || state.Tables.AlphaSelector is null)
        {
            throw new InvalidDataException("CRN DXT5 level is missing Huffman tables.");
        }

        int width = (outputWidth + 1) & ~1;
        int height = (outputHeight + 1) & ~1;
        BlockBufferElement[] blockBuffer = new BlockBufferElement[width];
        int colorEndpointIndex = 0;
        int alphaEndpointIndex = 0;
        int referenceGroup = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((y & 1) == 0 && (x & 1) == 0)
                    referenceGroup = state.Tables.Reference.Decode(reader);

                int endpointReference = ConsumeEndpointReference(y, x, blockBuffer, ref referenceGroup);
                if (endpointReference == 0)
                {
                    colorEndpointIndex += state.Tables.ColorEndpoint.Decode(reader);
                    if (colorEndpointIndex >= state.ColorEndpoints.Length)
                        colorEndpointIndex -= state.ColorEndpoints.Length;
                    blockBuffer[x].ColorEndpointIndex = (ushort)colorEndpointIndex;

                    alphaEndpointIndex += state.Tables.AlphaEndpoint.Decode(reader);
                    if (alphaEndpointIndex >= state.AlphaEndpoints.Length)
                        alphaEndpointIndex -= state.AlphaEndpoints.Length;
                    blockBuffer[x].AlphaEndpointIndex = (ushort)alphaEndpointIndex;
                }
                else if (endpointReference == 1)
                {
                    blockBuffer[x].ColorEndpointIndex = (ushort)colorEndpointIndex;
                    blockBuffer[x].AlphaEndpointIndex = (ushort)alphaEndpointIndex;
                }
                else
                {
                    colorEndpointIndex = blockBuffer[x].ColorEndpointIndex;
                    alphaEndpointIndex = blockBuffer[x].AlphaEndpointIndex;
                }

                int colorSelectorIndex = state.Tables.ColorSelector.Decode(reader);
                int alphaSelectorIndex = state.Tables.AlphaSelector.Decode(reader);
                if (x >= outputWidth || y >= outputHeight)
                    continue;

                int destinationOffset = (y * rowPitch) + (x * 16);
                ushort alphaEndpoint = CheckedRead(state.AlphaEndpoints, alphaEndpointIndex, "alpha endpoint");
                WriteUInt16(destination, destinationOffset, alphaEndpoint);

                int alphaSelectorOffset = alphaSelectorIndex * 3;
                ushort alpha0 = CheckedRead(state.AlphaSelectors, alphaSelectorOffset, "alpha selector");
                ushort alpha1 = CheckedRead(state.AlphaSelectors, alphaSelectorOffset + 1, "alpha selector");
                ushort alpha2 = CheckedRead(state.AlphaSelectors, alphaSelectorOffset + 2, "alpha selector");
                WriteUInt16(destination, destinationOffset + 2, alpha0);
                WriteUInt16(destination, destinationOffset + 4, alpha1);
                WriteUInt16(destination, destinationOffset + 6, alpha2);

                WriteUInt32(destination, destinationOffset + 8, CheckedRead(state.ColorEndpoints, colorEndpointIndex, "color endpoint"));
                WriteUInt32(destination, destinationOffset + 12, CheckedRead(state.ColorSelectors, colorSelectorIndex, "color selector"));
            }
        }

        return destination;
    }

    private static int ConsumeEndpointReference(int y, int x, BlockBufferElement[] blockBuffer, ref int referenceGroup)
    {
        if ((y & 1) != 0)
            return blockBuffer[x].EndpointReference;

        int endpointReference = referenceGroup & 3;
        referenceGroup >>= 2;
        blockBuffer[x].EndpointReference = (ushort)(referenceGroup & 3);
        referenceGroup >>= 2;
        return endpointReference;
    }

    private static BitReader CreateReader(ReadOnlySpan<byte> data, int offset, int size)
    {
        if (offset < 0 || size <= 0 || offset + size > data.Length)
            throw new InvalidDataException("CRN chunk points outside the file.");

        return new BitReader(data.Slice(offset, size).ToArray());
    }

    private static T CheckedRead<T>(T[] data, int index, string paletteName)
    {
        if ((uint)index >= (uint)data.Length)
            throw new InvalidDataException($"CRN {paletteName} index is outside the decoded palette.");

        return data[index];
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset, 2), value);
    }

    private static void WriteUInt32(byte[] destination, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);
    }

    private readonly record struct DecoderTables(
        StaticHuffmanModel Reference,
        StaticHuffmanModel? ColorEndpoint,
        StaticHuffmanModel? ColorSelector,
        StaticHuffmanModel? AlphaEndpoint,
        StaticHuffmanModel? AlphaSelector);

    private readonly record struct DecoderState(
        DecoderTables Tables,
        uint[] ColorEndpoints,
        uint[] ColorSelectors,
        ushort[] AlphaEndpoints,
        ushort[] AlphaSelectors);

    private struct BlockBufferElement
    {
        public ushort EndpointReference;
        public ushort ColorEndpointIndex;
        public ushort AlphaEndpointIndex;
    }
}
