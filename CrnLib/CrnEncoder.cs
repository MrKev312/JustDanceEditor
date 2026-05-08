using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Buffers.Binary;

namespace CrnLib;

internal static class CrnEncoder
{
    private static readonly byte[] Dxt1ToLinear = [0, 3, 1, 2];
    private static readonly byte[] Dxt5ToLinear = [0, 7, 1, 2, 3, 4, 5, 6];

    public static byte[] Encode(
        ReadOnlySpan<byte> rgba32,
        int width,
        int height,
        CrnFormat format,
        int quality,
        int mipCount,
        uint userData0,
        uint userData1)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "CRN texture dimensions must be positive.");

        if (rgba32.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA32 payload length does not match the supplied dimensions.", nameof(rgba32));

        if (format is not CrnFormat.Dxt1 and not CrnFormat.Dxt5)
            throw new NotSupportedException($"CRN format '{format}' is not supported by the managed encoder.");

        using Image<Rgba32> baseImage = Image.LoadPixelData<Rgba32>(rgba32.ToArray(), width, height);
        return Encode(baseImage, format, new CrnEncodeOptions
        {
            MipCount = mipCount,
            Quality = quality < 0 ? CrnCompressionQuality.Fast : CrnCompressionQuality.BestQuality,
            UserData0 = userData0,
            UserData1 = userData1,
        });
    }

    public static byte[] Encode(Image<Rgba32> image, CrnFormat format, CrnEncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);

        int width = image.Width;
        int height = image.Height;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(image), "CRN texture dimensions must be positive.");

        if (format is not CrnFormat.Dxt1 and not CrnFormat.Dxt5)
            throw new NotSupportedException($"CRN format '{format}' is not supported by the managed encoder.");

        int actualMipCount = Math.Clamp(options.MipCount, 1, CrnHeader.MaxLevels);
        actualMipCount = Math.Min(actualMipCount, ComputeMaxMipCount(width, height));

        using BcLevelEncoder bcnEncoder = new(format, options.Quality);
        CrnEncodingBuilder builder = new(format, width, height, actualMipCount, options.UserData0, options.UserData1);

        Image<Rgba32>? current = image.Clone();
        try
        {
            for (int level = 0; level < actualMipCount; level++)
            {
                byte[] blocks = bcnEncoder.Encode(current);
                builder.AddLevel(current.Width, current.Height, blocks);

                if (level + 1 == actualMipCount)
                    break;

                int nextWidth = Math.Max(1, current.Width >> 1);
                int nextHeight = Math.Max(1, current.Height >> 1);
                Image<Rgba32> next = current.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(nextWidth, nextHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.Lanczos3,
                }));
                current.Dispose();
                current = next;
            }
        }
        finally
        {
            current?.Dispose();
        }

        return builder.Build();
    }

    private static int ComputeMaxMipCount(int width, int height)
    {
        int levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width >> 1);
            height = Math.Max(1, height >> 1);
            levels++;
        }

        return levels;
    }

    private sealed class BcLevelEncoder : IDisposable
    {
        private readonly BcEncoder _encoder;

        public BcLevelEncoder(CrnFormat format, CrnCompressionQuality quality)
        {
            _encoder = new BcEncoder
            {
                OutputOptions =
                {
                    GenerateMipMaps = false,
                    Quality = quality switch
                    {
                        CrnCompressionQuality.Fast => CompressionQuality.Fast,
                        CrnCompressionQuality.Balanced => CompressionQuality.Balanced,
                        _ => CompressionQuality.BestQuality,
                    },
                    Format = format == CrnFormat.Dxt1 ? CompressionFormat.Bc1 : CompressionFormat.Bc3,
                }
            };
        }

        public byte[] Encode(Image<Rgba32> image)
        {
            return _encoder.EncodeToRawBytes(image)[0];
        }

        public void Dispose()
        {
        }
    }

    private sealed class CrnEncodingBuilder(CrnFormat format, int width, int height, int levels, uint userData0, uint userData1)
    {
        private readonly List<EncodedLevel> _encodedLevels = [];

        private readonly List<uint> _colorEndpoints = [];
        private readonly List<uint> _colorSelectors = [];
        private readonly List<ushort> _alphaEndpoints = [];
        private readonly List<ulong> _alphaSelectors = [];

        private readonly Dictionary<uint, ushort> _colorEndpointMap = [];
        private readonly Dictionary<uint, ushort> _colorSelectorMap = [];
        private readonly Dictionary<ushort, ushort> _alphaEndpointMap = [];
        private readonly Dictionary<ulong, ushort> _alphaSelectorMap = [];

        public void AddLevel(int width, int height, byte[] bcnBlocks)
        {
            int bytesPerBlock = format == CrnFormat.Dxt1 ? 8 : 16;
            int blocksX = (width + 3) >> 2;
            int blocksY = (height + 3) >> 2;
            int roundedBlocksX = (blocksX + 1) & ~1;
            int roundedBlocksY = (blocksY + 1) & ~1;

            if (bcnBlocks.Length != blocksX * blocksY * bytesPerBlock)
                throw new InvalidDataException("BCn encoder returned an unexpected block payload length.");

            ushort[] colorEndpointIndices = new ushort[roundedBlocksX * roundedBlocksY];
            ushort[] colorSelectorIndices = new ushort[colorEndpointIndices.Length];
            ushort[] alphaEndpointIndices = format == CrnFormat.Dxt5 ? new ushort[colorEndpointIndices.Length] : [];
            ushort[] alphaSelectorIndices = format == CrnFormat.Dxt5 ? new ushort[colorEndpointIndices.Length] : [];
            Span<byte> paddingBlock = stackalloc byte[16];

            for (int y = 0; y < roundedBlocksY; y++)
            {
                for (int x = 0; x < roundedBlocksX; x++)
                {
                    int destinationIndex = (y * roundedBlocksX) + x;
                    bool visible = x < blocksX && y < blocksY;
                    ReadOnlySpan<byte> block = visible
                        ? bcnBlocks.AsSpan(((y * blocksX) + x) * bytesPerBlock, bytesPerBlock)
                        : paddingBlock;

                    if (format == CrnFormat.Dxt1)
                    {
                        uint endpoint = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block[..4]) : 0;
                        uint selector = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(4, 4)) : 0;
                        colorEndpointIndices[destinationIndex] = GetColorEndpointIndex(endpoint);
                        colorSelectorIndices[destinationIndex] = GetColorSelectorIndex(selector);
                    }
                    else
                    {
                        ushort alphaEndpoint = visible ? BinaryPrimitives.ReadUInt16LittleEndian(block[..2]) : (ushort)0;
                        ulong alphaSelector = visible ? ReadUInt48LittleEndian(block.Slice(2, 6)) : 0UL;
                        uint colorEndpoint = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(8, 4)) : 0;
                        uint colorSelector = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(12, 4)) : 0;

                        alphaEndpointIndices[destinationIndex] = GetAlphaEndpointIndex(alphaEndpoint);
                        alphaSelectorIndices[destinationIndex] = GetAlphaSelectorIndex(alphaSelector);
                        colorEndpointIndices[destinationIndex] = GetColorEndpointIndex(colorEndpoint);
                        colorSelectorIndices[destinationIndex] = GetColorSelectorIndex(colorSelector);
                    }
                }
            }

            _encodedLevels.Add(new EncodedLevel(
                width,
                height,
                blocksX,
                blocksY,
                roundedBlocksX,
                roundedBlocksY,
                colorEndpointIndices,
                colorSelectorIndices,
                alphaEndpointIndices,
                alphaSelectorIndices));
        }

        public byte[] Build()
        {
            if (_encodedLevels.Count != levels)
                throw new InvalidOperationException("Not all CRN mip levels have been encoded.");

            List<LevelSymbolStream> levelSymbols = BuildLevelSymbols();
            byte[] colorEndpointData = EncodeColorEndpoints();
            byte[] colorSelectorData = EncodeColorSelectors();
            byte[] alphaEndpointData = format == CrnFormat.Dxt5 ? EncodeAlphaEndpoints() : [];
            byte[] alphaSelectorData = format == CrnFormat.Dxt5 ? EncodeAlphaSelectors() : [];
            byte[] tableData = EncodeTables(levelSymbols);
            byte[][] levelData = EncodeLevels(levelSymbols);

            int headerSize = CrnHeader.FixedHeaderSize + ((levels - 1) * 4);
            List<byte> file = new(headerSize + colorEndpointData.Length + colorSelectorData.Length + alphaEndpointData.Length + alphaSelectorData.Length + tableData.Length);
            file.AddRange(new byte[headerSize]);

            CrnPalette colorEndpointPalette = AppendPalette(file, colorEndpointData, _colorEndpoints.Count);
            CrnPalette colorSelectorPalette = AppendPalette(file, colorSelectorData, _colorSelectors.Count);
            CrnPalette alphaEndpointPalette = format == CrnFormat.Dxt5 ? AppendPalette(file, alphaEndpointData, _alphaEndpoints.Count) : default;
            CrnPalette alphaSelectorPalette = format == CrnFormat.Dxt5 ? AppendPalette(file, alphaSelectorData, _alphaSelectors.Count) : default;

            int tablesOffset = file.Count;
            file.AddRange(tableData);
            int tablesSize = tableData.Length;
            if (tablesSize > ushort.MaxValue)
                throw new InvalidDataException("CRN table data exceeds the 16-bit Unity Crunch table size field.");

            int[] levelOffsets = new int[levels];
            for (int i = 0; i < levelData.Length; i++)
            {
                levelOffsets[i] = file.Count;
                file.AddRange(levelData[i]);
            }

            byte[] result = [.. file];
            CrnHeader.Write(
                result,
                headerSize,
                result.Length,
                width,
                height,
                levels,
                format,
                userData0,
                userData1,
                colorEndpointPalette,
                colorSelectorPalette,
                alphaEndpointPalette,
                alphaSelectorPalette,
                tablesOffset,
                tablesSize,
                levelOffsets);
            CrnHeader.FinalizeChecksums(result, headerSize);
            return result;
        }

        private List<LevelSymbolStream> BuildLevelSymbols()
        {
            List<LevelSymbolStream> levels = new(_encodedLevels.Count);
            foreach (EncodedLevel level in _encodedLevels)
            {
                LevelSymbolStream stream = new();
                int currentColorEndpoint = 0;
                int currentAlphaEndpoint = 0;

                for (int y = 0; y < level.RoundedBlocksY; y++)
                {
                    for (int x = 0; x < level.RoundedBlocksX; x++)
                    {
                        if ((y & 1) == 0 && (x & 1) == 0)
                            stream.ReferenceGroups.Add(0);

                        int index = (y * level.RoundedBlocksX) + x;
                        int colorEndpoint = level.ColorEndpointIndices[index];
                        int colorDelta = PositiveModulo(colorEndpoint - currentColorEndpoint, _colorEndpoints.Count);
                        stream.ColorEndpointDeltas.Add(colorDelta);
                        currentColorEndpoint = colorEndpoint;
                        stream.ColorSelectorIndices.Add(level.ColorSelectorIndices[index]);

                        if (format == CrnFormat.Dxt5)
                        {
                            int alphaEndpoint = level.AlphaEndpointIndices[index];
                            int alphaDelta = PositiveModulo(alphaEndpoint - currentAlphaEndpoint, _alphaEndpoints.Count);
                            stream.AlphaEndpointDeltas.Add(alphaDelta);
                            currentAlphaEndpoint = alphaEndpoint;
                            stream.AlphaSelectorIndices.Add(level.AlphaSelectorIndices[index]);
                        }
                    }
                }

                levels.Add(stream);
            }

            return levels;
        }

        private byte[] EncodeTables(List<LevelSymbolStream> levels)
        {
            StaticHuffmanModel referenceModel = StaticHuffmanModel.CreateFixed(0);
            StaticHuffmanModel colorEndpointModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorEndpointDeltas));
            StaticHuffmanModel colorSelectorModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorSelectorIndices));
            StaticHuffmanModel? alphaEndpointModel = null;
            StaticHuffmanModel? alphaSelectorModel = null;

            if (format == CrnFormat.Dxt5)
            {
                alphaEndpointModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.AlphaEndpointDeltas));
                alphaSelectorModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.AlphaSelectorIndices));
            }

            BitWriter writer = new();
            referenceModel.Transmit(writer);
            colorEndpointModel.Transmit(writer);
            colorSelectorModel.Transmit(writer);
            alphaEndpointModel?.Transmit(writer);
            alphaSelectorModel?.Transmit(writer);
            return writer.Finish();
        }

        private byte[][] EncodeLevels(List<LevelSymbolStream> levels)
        {
            StaticHuffmanModel referenceModel = StaticHuffmanModel.CreateFixed(0);
            StaticHuffmanModel colorEndpointModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorEndpointDeltas));
            StaticHuffmanModel colorSelectorModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorSelectorIndices));
            StaticHuffmanModel? alphaEndpointModel = null;
            StaticHuffmanModel? alphaSelectorModel = null;

            if (format == CrnFormat.Dxt5)
            {
                alphaEndpointModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.AlphaEndpointDeltas));
                alphaSelectorModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.AlphaSelectorIndices));
            }

            byte[][] result = new byte[levels.Count][];
            for (int i = 0; i < levels.Count; i++)
            {
                LevelSymbolStream level = levels[i];
                BitWriter writer = new();
                int referenceIndex = 0;
                int endpointIndex = 0;
                int selectorIndex = 0;

                EncodedLevel encodedLevel = _encodedLevels[i];
                for (int y = 0; y < encodedLevel.RoundedBlocksY; y++)
                {
                    for (int x = 0; x < encodedLevel.RoundedBlocksX; x++)
                    {
                        if ((y & 1) == 0 && (x & 1) == 0)
                            referenceModel.Encode(writer, level.ReferenceGroups[referenceIndex++]);

                        colorEndpointModel.Encode(writer, level.ColorEndpointDeltas[endpointIndex]);
                        if (format == CrnFormat.Dxt5)
                            alphaEndpointModel!.Encode(writer, level.AlphaEndpointDeltas[endpointIndex]);

                        colorSelectorModel.Encode(writer, level.ColorSelectorIndices[selectorIndex]);
                        if (format == CrnFormat.Dxt5)
                            alphaSelectorModel!.Encode(writer, level.AlphaSelectorIndices[selectorIndex]);

                        endpointIndex++;
                        selectorIndex++;
                    }
                }

                result[i] = writer.Finish();
            }

            return result;
        }

        private byte[] EncodeColorEndpoints()
        {
            List<int> redBlueSymbols = [];
            List<int> greenSymbols = [];
            List<(int Symbol, bool Green)> sequence = [];

            int previousR0 = 0;
            int previousG0 = 0;
            int previousB0 = 0;
            int previousR1 = 0;
            int previousG1 = 0;
            int previousB1 = 0;

            foreach (uint endpoint in _colorEndpoints)
            {
                ushort color0 = (ushort)endpoint;
                ushort color1 = (ushort)(endpoint >> 16);
                int r0 = (color0 >> 11) & 31;
                int g0 = (color0 >> 5) & 63;
                int b0 = color0 & 31;
                int r1 = (color1 >> 11) & 31;
                int g1 = (color1 >> 5) & 63;
                int b1 = color1 & 31;

                AddColorEndpointSymbol(r0 - previousR0, 31, false);
                AddColorEndpointSymbol(g0 - previousG0, 63, true);
                AddColorEndpointSymbol(b0 - previousB0, 31, false);
                AddColorEndpointSymbol(r1 - previousR1, 31, false);
                AddColorEndpointSymbol(g1 - previousG1, 63, true);
                AddColorEndpointSymbol(b1 - previousB1, 31, false);

                previousR0 = r0;
                previousG0 = g0;
                previousB0 = b0;
                previousR1 = r1;
                previousG1 = g1;
                previousB1 = b1;
            }

            StaticHuffmanModel redBlueModel = StaticHuffmanModel.CreateForSymbols(redBlueSymbols);
            StaticHuffmanModel greenModel = StaticHuffmanModel.CreateForSymbols(greenSymbols);
            BitWriter writer = new();
            redBlueModel.Transmit(writer);
            greenModel.Transmit(writer);

            foreach ((int symbol, bool green) in sequence)
            {
                if (green)
                    greenModel.Encode(writer, symbol);
                else
                    redBlueModel.Encode(writer, symbol);
            }

            return writer.Finish();

            void AddColorEndpointSymbol(int delta, int mask, bool green)
            {
                int symbol = delta & mask;
                sequence.Add((symbol, green));
                if (green)
                    greenSymbols.Add(symbol);
                else
                    redBlueSymbols.Add(symbol);
            }
        }

        private byte[] EncodeColorSelectors()
        {
            List<int> symbols = [];
            uint previousLinear = 0;

            foreach (uint selector in _colorSelectors)
            {
                uint linear = Dxt1SelectorToLinear(selector);
                for (int shift = 0; shift < 32; shift += 4)
                    symbols.Add((int)(((linear >> shift) ^ (previousLinear >> shift)) & 0xF));

                previousLinear = linear;
            }

            StaticHuffmanModel model = StaticHuffmanModel.CreateForSymbols(symbols);
            BitWriter writer = new();
            model.Transmit(writer);
            foreach (int symbol in symbols)
                model.Encode(writer, symbol);

            return writer.Finish();
        }

        private byte[] EncodeAlphaEndpoints()
        {
            List<int> symbols = [];
            int previousA = 0;
            int previousB = 0;

            foreach (ushort endpoint in _alphaEndpoints)
            {
                int a = endpoint & 0xFF;
                int b = endpoint >> 8;
                symbols.Add((a - previousA) & 255);
                symbols.Add((b - previousB) & 255);
                previousA = a;
                previousB = b;
            }

            StaticHuffmanModel model = StaticHuffmanModel.CreateForSymbols(symbols);
            BitWriter writer = new();
            model.Transmit(writer);
            foreach (int symbol in symbols)
                model.Encode(writer, symbol);

            return writer.Finish();
        }

        private byte[] EncodeAlphaSelectors()
        {
            List<int> symbols = [];
            uint previousS0 = 0;
            uint previousS1 = 0;

            foreach (ulong selector in _alphaSelectors)
            {
                (uint s0, uint s1) = Dxt5SelectorToLinear(selector);
                for (int shift = 0; shift < 24; shift += 6)
                    symbols.Add((int)(((s0 >> shift) ^ (previousS0 >> shift)) & 0x3F));

                for (int shift = 0; shift < 24; shift += 6)
                    symbols.Add((int)(((s1 >> shift) ^ (previousS1 >> shift)) & 0x3F));

                previousS0 = s0;
                previousS1 = s1;
            }

            StaticHuffmanModel model = StaticHuffmanModel.CreateForSymbols(symbols);
            BitWriter writer = new();
            model.Transmit(writer);
            foreach (int symbol in symbols)
                model.Encode(writer, symbol);

            return writer.Finish();
        }

        private ushort GetColorEndpointIndex(uint value)
        {
            return GetPaletteIndex(value, _colorEndpoints, _colorEndpointMap, ColorEndpointDistance);
        }

        private ushort GetColorSelectorIndex(uint value)
        {
            return GetPaletteIndex(value, _colorSelectors, _colorSelectorMap, ColorSelectorDistance);
        }

        private ushort GetAlphaEndpointIndex(ushort value)
        {
            return GetPaletteIndex(value, _alphaEndpoints, _alphaEndpointMap, AlphaEndpointDistance);
        }

        private ushort GetAlphaSelectorIndex(ulong value)
        {
            return GetPaletteIndex(value, _alphaSelectors, _alphaSelectorMap, AlphaSelectorDistance);
        }

        private static ushort GetPaletteIndex<T>(T value, List<T> palette, Dictionary<T, ushort> map, Func<T, T, int> distance)
            where T : notnull
        {
            if (map.TryGetValue(value, out ushort existingIndex))
                return existingIndex;

            ushort index;
            if (palette.Count < CrnHeader.MaxPaletteEntries)
            {
                index = (ushort)palette.Count;
                palette.Add(value);
            }
            else
            {
                index = FindNearestPaletteIndex(value, palette, distance);
            }

            map[value] = index;
            return index;
        }

        private static ushort FindNearestPaletteIndex<T>(T value, List<T> palette, Func<T, T, int> distance)
        {
            int bestDistance = int.MaxValue;
            ushort bestIndex = 0;

            for (int i = 0; i < palette.Count; i++)
            {
                int candidateDistance = distance(value, palette[i]);
                if (candidateDistance >= bestDistance)
                    continue;

                bestDistance = candidateDistance;
                bestIndex = (ushort)i;
                if (bestDistance == 0)
                    break;
            }

            return bestIndex;
        }

        private static int ColorEndpointDistance(uint a, uint b)
        {
            return Rgb565Distance((ushort)a, (ushort)b) +
                   Rgb565Distance((ushort)(a >> 16), (ushort)(b >> 16));
        }

        private static int Rgb565Distance(ushort a, ushort b)
        {
            int ar = Expand5((a >> 11) & 31);
            int ag = Expand6((a >> 5) & 63);
            int ab = Expand5(a & 31);
            int br = Expand5((b >> 11) & 31);
            int bg = Expand6((b >> 5) & 63);
            int bb = Expand5(b & 31);

            return Squared(ar - br) + Squared(ag - bg) + Squared(ab - bb);
        }

        private static int ColorSelectorDistance(uint a, uint b)
        {
            int distance = 0;
            for (int i = 0; i < 16; i++)
            {
                int selectorA = Dxt1ToLinear[(int)((a >> (i * 2)) & 3)];
                int selectorB = Dxt1ToLinear[(int)((b >> (i * 2)) & 3)];
                distance += Squared(selectorA - selectorB);
            }

            return distance;
        }

        private static int AlphaEndpointDistance(ushort a, ushort b)
        {
            return Squared((a & 0xFF) - (b & 0xFF)) +
                   Squared((a >> 8) - (b >> 8));
        }

        private static int AlphaSelectorDistance(ulong a, ulong b)
        {
            int distance = 0;
            for (int i = 0; i < 16; i++)
            {
                int selectorA = Dxt5ToLinear[(int)((a >> (i * 3)) & 7)];
                int selectorB = Dxt5ToLinear[(int)((b >> (i * 3)) & 7)];
                distance += Squared(selectorA - selectorB);
            }

            return distance;
        }

        private static int Expand5(int value)
        {
            return ((value * 527) + 23) >> 6;
        }

        private static int Expand6(int value)
        {
            return ((value * 259) + 33) >> 6;
        }

        private static int Squared(int value)
        {
            return value * value;
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static CrnPalette AppendPalette(List<byte> file, byte[] data, int count)
        {
            int offset = file.Count;
            file.AddRange(data);
            return new CrnPalette(offset, data.Length, count);
        }

        private static uint Dxt1SelectorToLinear(uint selector)
        {
            uint linear = 0;
            for (int i = 0; i < 16; i++)
            {
                int dxtSelector = (int)((selector >> (i * 2)) & 3);
                linear |= (uint)Dxt1ToLinear[dxtSelector] << (i * 2);
            }

            return linear;
        }

        private static (uint S0, uint S1) Dxt5SelectorToLinear(ulong selector)
        {
            uint s0 = 0;
            uint s1 = 0;

            for (int i = 0; i < 16; i++)
            {
                int dxtSelector = (int)((selector >> (i * 3)) & 7);
                uint linear = Dxt5ToLinear[dxtSelector];
                if (i < 8)
                    s0 |= linear << (i * 3);
                else
                    s1 |= linear << ((i - 8) * 3);
            }

            return (s0, s1);
        }

        private static ulong ReadUInt48LittleEndian(ReadOnlySpan<byte> data)
        {
            return (ulong)data[0] |
                   ((ulong)data[1] << 8) |
                   ((ulong)data[2] << 16) |
                   ((ulong)data[3] << 24) |
                   ((ulong)data[4] << 32) |
                   ((ulong)data[5] << 40);
        }
    }

    private sealed record EncodedLevel(
        int Width,
        int Height,
        int BlocksX,
        int BlocksY,
        int RoundedBlocksX,
        int RoundedBlocksY,
        ushort[] ColorEndpointIndices,
        ushort[] ColorSelectorIndices,
        ushort[] AlphaEndpointIndices,
        ushort[] AlphaSelectorIndices);

    private sealed class LevelSymbolStream
    {
        public List<int> ReferenceGroups { get; } = [];
        public List<int> ColorEndpointDeltas { get; } = [];
        public List<int> ColorSelectorIndices { get; } = [];
        public List<int> AlphaEndpointDeltas { get; } = [];
        public List<int> AlphaSelectorIndices { get; } = [];
    }
}
