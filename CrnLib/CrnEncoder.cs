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
    private const int MaxZengPaletteEntries = 2048;

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

        if (!CrnFormatHelpers.IsManagedEncodableCrnFormat(format))
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
        return EncodeFaces([image], format, options);
    }

    public static byte[] EncodeFaces(IReadOnlyList<Image<Rgba32>> faces, CrnFormat format, CrnEncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(faces);
        if (faces.Count is not 1 and not CrnHeader.MaxFaces)
            throw new ArgumentException("CRN textures must have either one face or six cubemap faces.", nameof(faces));

        ArgumentNullException.ThrowIfNull(faces[0]);
        int width = faces[0].Width;
        int height = faces[0].Height;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(faces), "CRN texture dimensions must be positive.");

        for (int i = 1; i < faces.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(faces[i]);
            if (faces[i].Width != width || faces[i].Height != height)
                throw new ArgumentException("All CRN faces must have the same dimensions.", nameof(faces));
        }

        if (!CrnFormatHelpers.IsManagedEncodableCrnFormat(format))
            throw new NotSupportedException($"CRN format '{format}' is not supported by the managed encoder.");

        int actualMipCount = Math.Clamp(options.MipCount, 1, CrnHeader.MaxLevels);
        actualMipCount = Math.Min(actualMipCount, ComputeMaxMipCount(width, height));

        using BcLevelEncoder bcnEncoder = new(format, options.Quality);
        CrnEncodingBuilder builder = new(format, width, height, actualMipCount, faces.Count, options.Quality, options.UserData0, options.UserData1);

        Image<Rgba32>?[] currentFaces = new Image<Rgba32>?[faces.Count];
        try
        {
            for (int face = 0; face < currentFaces.Length; face++)
                currentFaces[face] = CreateEncodingBaseImage(faces[face], format);

            for (int level = 0; level < actualMipCount; level++)
            {
                Image<Rgba32> currentFace0 = currentFaces[0] ?? throw new InvalidOperationException("CRN face image was not initialized.");
                byte[][] blocks = new byte[currentFaces.Length][];
                for (int face = 0; face < currentFaces.Length; face++)
                    blocks[face] = bcnEncoder.Encode(currentFaces[face] ?? throw new InvalidOperationException("CRN face image was not initialized."));

                builder.AddLevel(currentFace0.Width, currentFace0.Height, blocks);

                if (level + 1 == actualMipCount)
                    break;

                int nextWidth = Math.Max(1, currentFace0.Width >> 1);
                int nextHeight = Math.Max(1, currentFace0.Height >> 1);
                for (int face = 0; face < currentFaces.Length; face++)
                {
                    Image<Rgba32> current = currentFaces[face] ?? throw new InvalidOperationException("CRN face image was not initialized.");
                    Image<Rgba32> next = current.Clone(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(nextWidth, nextHeight),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.Lanczos3,
                    }));
                    current.Dispose();
                    currentFaces[face] = next;
                }
            }
        }
        finally
        {
            foreach (Image<Rgba32>? face in currentFaces)
                face?.Dispose();
        }

        return builder.Build();
    }

    private static Image<Rgba32> CreateEncodingBaseImage(Image<Rgba32> image, CrnFormat format)
    {
        Image<Rgba32> result = image.Clone();
        if (format is not (CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr))
            return result;

        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = ConvertPixelForDxt5Derivative(row[x], format);
            }
        });

        return result;
    }

    private static Rgba32 ConvertPixelForDxt5Derivative(Rgba32 source, CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt5CcxY => ToYcc(source),
            CrnFormat.Dxt5XGxR => new Rgba32(0, source.G, 0, source.R),
            CrnFormat.Dxt5XGbr => new Rgba32(0, source.G, source.B, source.R),
            CrnFormat.Dxt5Agbr => new Rgba32(source.A, source.G, source.B, source.R),
            _ => source,
        };
    }

    private static Rgba32 ToYcc(Rgba32 source)
    {
        const int yr = 19595;
        const int yg = 38470;
        const int yb = 7471;
        const int cbR = -11059;
        const int cbG = -21709;
        const int cbB = 32768;
        const int crR = 32768;
        const int crG = -27439;
        const int crB = -5329;
        const int cbBias = 123;
        const int crBias = 125;

        int r = source.R;
        int g = source.G;
        int b = source.B;
        byte y = ClampToByte(((r * yr) + (g * yg) + (b * yb) + 32768) >> 16);
        byte cb = ClampToByte(cbBias + (((r * cbR) + (g * cbG) + (b * cbB) + 32768) >> 16));
        byte cr = ClampToByte(crBias + (((r * crR) + (g * crG) + (b * crB) + 32768) >> 16));
        return new Rgba32(cb, cr, 0, y);
    }

    private static byte ClampToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
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
        private const int AlphaEndpointSearchRadius = 16;
        private const int ColorEndpointSearchRadius = 1;

        private readonly CrnFormat _format;
        private readonly BcEncoder _encoder;

        public BcLevelEncoder(CrnFormat format, CrnCompressionQuality quality)
        {
            _format = format;
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
                    Format = CrnFormatHelpers.ToCompressionFormat(format),
                }
            };

            if (format == CrnFormat.Dxt5A)
                _encoder.InputOptions.Bc4Component = ColorComponent.A;
            else if (format == CrnFormat.DxnYx)
            {
                _encoder.InputOptions.Bc5Component1 = ColorComponent.G;
                _encoder.InputOptions.Bc5Component2 = ColorComponent.R;
            }
        }

        public byte[] Encode(Image<Rgba32> image)
        {
            if (_format == CrnFormat.Dxt5A)
                return EncodeBc4(image, ColorComponent.A);

            if (_format == CrnFormat.DxnXy)
                return EncodeBc5(image, ColorComponent.R, ColorComponent.G);

            if (_format == CrnFormat.DxnYx)
                return EncodeBc5(image, ColorComponent.G, ColorComponent.R);

            byte[] blocks = _encoder.EncodeToRawBytes(image)[0];
            if (_format is CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr)
            {
                ReplaceDxt5AlphaBlocks(image, blocks, ColorComponent.A);
                RefineDxt5DerivativeColorBlocks(image, blocks, _format);
            }

            return blocks;
        }

        private static void ReplaceDxt5AlphaBlocks(Image<Rgba32> image, byte[] blocks, ColorComponent component)
        {
            byte[] alphaBlocks = EncodeBc4(image, component);
            for (int sourceOffset = 0, destinationOffset = 0; sourceOffset < alphaBlocks.Length; sourceOffset += 8, destinationOffset += 16)
                alphaBlocks.AsSpan(sourceOffset, 8).CopyTo(blocks.AsSpan(destinationOffset, 8));
        }

        private static byte[] EncodeBc5(Image<Rgba32> image, ColorComponent first, ColorComponent second)
        {
            byte[] firstBlocks = EncodeBc4(image, first);
            byte[] secondBlocks = EncodeBc4(image, second);
            byte[] result = new byte[firstBlocks.Length + secondBlocks.Length];
            int sourceOffset = 0;
            for (int destinationOffset = 0; destinationOffset < result.Length; destinationOffset += 16)
            {
                firstBlocks.AsSpan(sourceOffset, 8).CopyTo(result.AsSpan(destinationOffset, 8));
                secondBlocks.AsSpan(sourceOffset, 8).CopyTo(result.AsSpan(destinationOffset + 8, 8));
                sourceOffset += 8;
            }

            return result;
        }

        private static void RefineDxt5DerivativeColorBlocks(Image<Rgba32> image, byte[] blocks, CrnFormat format)
        {
            ColorWeights weights = GetDerivativeColorWeights(format);
            int blocksX = (image.Width + 3) >> 2;
            int blocksY = (image.Height + 3) >> 2;

            image.ProcessPixelRows(accessor =>
            {
                Span<Rgba32> pixels = stackalloc Rgba32[16];
                for (int blockY = 0; blockY < blocksY; blockY++)
                {
                    for (int blockX = 0; blockX < blocksX; blockX++)
                    {
                        int pixelIndex = 0;
                        for (int y = 0; y < 4; y++)
                        {
                            int sourceY = Math.Min((blockY * 4) + y, accessor.Height - 1);
                            Span<Rgba32> row = accessor.GetRowSpan(sourceY);
                            for (int x = 0; x < 4; x++)
                            {
                                int sourceX = Math.Min((blockX * 4) + x, row.Length - 1);
                                pixels[pixelIndex++] = row[sourceX];
                            }
                        }

                        int blockOffset = ((blockY * blocksX) + blockX) * 16;
                        int colorOffset = blockOffset + 8;
                        RefineDxtColorBlock(pixels, weights, blocks.AsSpan(colorOffset, 8));
                    }
                }
            });
        }

        private static ColorWeights GetDerivativeColorWeights(CrnFormat format)
        {
            return format switch
            {
                CrnFormat.Dxt5CcxY => new ColorWeights(1, 1, 0),
                CrnFormat.Dxt5XGxR => new ColorWeights(0, 2, 0),
                CrnFormat.Dxt5XGbr => new ColorWeights(0, 1, 1),
                CrnFormat.Dxt5Agbr => new ColorWeights(1, 1, 1),
                _ => new ColorWeights(1, 1, 1),
            };
        }

        private static void RefineDxtColorBlock(ReadOnlySpan<Rgba32> pixels, ColorWeights weights, Span<byte> block)
        {
            ushort current0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
            ushort current1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
            int bestError = EvaluateColorEndpointPair(pixels, weights, current0, current1, out uint bestSelectors);
            ushort best0 = current0;
            ushort best1 = current1;

            Span<ColorEndpointPair> seeds = stackalloc ColorEndpointPair[8];
            int seedCount = 0;
            AddSeedPair(ref seedCount, seeds, current0, current1);
            AddSeedPair(ref seedCount, seeds, current1, current0);
            AddProjectionSeedPair(pixels, weights, ref seedCount, seeds);
            AddBoundsSeedPair(pixels, weights, ref seedCount, seeds);

            Span<ushort> neighbors0 = stackalloc ushort[27];
            Span<ushort> neighbors1 = stackalloc ushort[27];
            for (int seedIndex = 0; seedIndex < seedCount; seedIndex++)
            {
                ColorEndpointPair seed = seeds[seedIndex];
                int count0 = BuildEndpointNeighbors(seed.Color0, neighbors0);
                int count1 = BuildEndpointNeighbors(seed.Color1, neighbors1);
                for (int i = 0; i < count0; i++)
                {
                    for (int j = 0; j < count1; j++)
                    {
                        TryColorEndpointPair(pixels, weights, neighbors0[i], neighbors1[j], ref bestError, ref best0, ref best1, ref bestSelectors);
                    }
                }
            }

            for (int i = 0; i < 3; i++)
            {
                ColorEndpointPair refined = RefineColorEndpointPair(pixels, weights, bestSelectors, best0, best1);
                if (refined.Color0 == best0 && refined.Color1 == best1)
                    break;

                TryColorEndpointPair(pixels, weights, refined.Color0, refined.Color1, ref bestError, ref best0, ref best1, ref bestSelectors);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(block, best0);
            BinaryPrimitives.WriteUInt16LittleEndian(block[2..], best1);
            BinaryPrimitives.WriteUInt32LittleEndian(block[4..], bestSelectors);
        }

        private static void TryColorEndpointPair(
            ReadOnlySpan<Rgba32> pixels,
            ColorWeights weights,
            ushort color0,
            ushort color1,
            ref int bestError,
            ref ushort best0,
            ref ushort best1,
            ref uint bestSelectors)
        {
            int error = EvaluateColorEndpointPair(pixels, weights, color0, color1, out uint selectors);
            if (error >= bestError)
                return;

            bestError = error;
            best0 = color0;
            best1 = color1;
            bestSelectors = selectors;
        }

        private static int EvaluateColorEndpointPair(ReadOnlySpan<Rgba32> pixels, ColorWeights weights, ushort color0, ushort color1, out uint selectors)
        {
            Span<Rgba32> palette = stackalloc Rgba32[4];
            palette[0] = FromRgb565(color0);
            palette[1] = FromRgb565(color1);
            palette[2] = InterpolateColor(palette[0], palette[1], 2, 1);
            palette[3] = InterpolateColor(palette[0], palette[1], 1, 2);

            int error = 0;
            selectors = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int bestSelector = 0;
                int bestPixelError = int.MaxValue;
                for (int selector = 0; selector < palette.Length; selector++)
                {
                    int candidateError = ColorDistance(pixels[i], palette[selector], weights);
                    if (candidateError >= bestPixelError)
                        continue;

                    bestPixelError = candidateError;
                    bestSelector = selector;
                    if (bestPixelError == 0)
                        break;
                }

                error += bestPixelError;
                selectors |= (uint)bestSelector << (i * 2);
            }

            return error;
        }

        private static int ColorDistance(Rgba32 a, Rgba32 b, ColorWeights weights)
        {
            return (weights.R * Squared(a.R - b.R)) +
                (weights.G * Squared(a.G - b.G)) +
                (weights.B * Squared(a.B - b.B));
        }

        private static ColorEndpointPair RefineColorEndpointPair(
            ReadOnlySpan<Rgba32> pixels,
            ColorWeights weights,
            uint selectors,
            ushort fallback0,
            ushort fallback1)
        {
            Rgba32 fallbackColor0 = FromRgb565(fallback0);
            Rgba32 fallbackColor1 = FromRgb565(fallback1);
            byte r0 = weights.R == 0 ? fallbackColor0.R : SolveEndpointComponent(pixels, selectors, component: 0, endpoint: 0);
            byte g0 = weights.G == 0 ? fallbackColor0.G : SolveEndpointComponent(pixels, selectors, component: 1, endpoint: 0);
            byte b0 = weights.B == 0 ? fallbackColor0.B : SolveEndpointComponent(pixels, selectors, component: 2, endpoint: 0);
            byte r1 = weights.R == 0 ? fallbackColor1.R : SolveEndpointComponent(pixels, selectors, component: 0, endpoint: 1);
            byte g1 = weights.G == 0 ? fallbackColor1.G : SolveEndpointComponent(pixels, selectors, component: 1, endpoint: 1);
            byte b1 = weights.B == 0 ? fallbackColor1.B : SolveEndpointComponent(pixels, selectors, component: 2, endpoint: 1);

            return new ColorEndpointPair(
                QuantizeToRgb565(new Rgba32(r0, g0, b0, 255)),
                QuantizeToRgb565(new Rgba32(r1, g1, b1, 255)));
        }

        private static byte SolveEndpointComponent(ReadOnlySpan<Rgba32> pixels, uint selectors, int component, int endpoint)
        {
            double aa = 0;
            double ab = 0;
            double bb = 0;
            double av = 0;
            double bv = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int selector = (int)((selectors >> (i * 2)) & 3);
                (double a, double b) = selector switch
                {
                    0 => (1.0, 0.0),
                    1 => (0.0, 1.0),
                    2 => (2.0 / 3.0, 1.0 / 3.0),
                    _ => (1.0 / 3.0, 2.0 / 3.0),
                };

                int value = component switch
                {
                    0 => pixels[i].R,
                    1 => pixels[i].G,
                    _ => pixels[i].B,
                };

                aa += a * a;
                ab += a * b;
                bb += b * b;
                av += a * value;
                bv += b * value;
            }

            double determinant = (aa * bb) - (ab * ab);
            if (Math.Abs(determinant) < 0.000001)
                return 0;

            double solved = endpoint == 0
                ? ((av * bb) - (bv * ab)) / determinant
                : ((aa * bv) - (ab * av)) / determinant;
            return ClampToByte((int)Math.Round(solved));
        }

        private static void AddProjectionSeedPair(ReadOnlySpan<Rgba32> pixels, ColorWeights weights, ref int seedCount, Span<ColorEndpointPair> seeds)
        {
            int minIndex = 0;
            int maxIndex = 0;
            int minScore = WeightedColorScore(pixels[0], weights);
            int maxScore = minScore;
            for (int i = 1; i < pixels.Length; i++)
            {
                int score = WeightedColorScore(pixels[i], weights);
                if (score < minScore)
                {
                    minScore = score;
                    minIndex = i;
                }
                else if (score > maxScore)
                {
                    maxScore = score;
                    maxIndex = i;
                }
            }

            ushort low = QuantizeToRgb565(pixels[minIndex]);
            ushort high = QuantizeToRgb565(pixels[maxIndex]);
            AddSeedPair(ref seedCount, seeds, low, high);
            AddSeedPair(ref seedCount, seeds, high, low);
        }

        private static void AddBoundsSeedPair(ReadOnlySpan<Rgba32> pixels, ColorWeights weights, ref int seedCount, Span<ColorEndpointPair> seeds)
        {
            byte minR = pixels[0].R;
            byte maxR = pixels[0].R;
            byte minG = pixels[0].G;
            byte maxG = pixels[0].G;
            byte minB = pixels[0].B;
            byte maxB = pixels[0].B;
            for (int i = 1; i < pixels.Length; i++)
            {
                minR = Math.Min(minR, pixels[i].R);
                maxR = Math.Max(maxR, pixels[i].R);
                minG = Math.Min(minG, pixels[i].G);
                maxG = Math.Max(maxG, pixels[i].G);
                minB = Math.Min(minB, pixels[i].B);
                maxB = Math.Max(maxB, pixels[i].B);
            }

            Rgba32 low = new(
                weights.R == 0 ? (byte)0 : minR,
                weights.G == 0 ? (byte)0 : minG,
                weights.B == 0 ? (byte)0 : minB,
                255);
            Rgba32 high = new(
                weights.R == 0 ? (byte)0 : maxR,
                weights.G == 0 ? (byte)0 : maxG,
                weights.B == 0 ? (byte)0 : maxB,
                255);

            ushort low565 = QuantizeToRgb565(low);
            ushort high565 = QuantizeToRgb565(high);
            AddSeedPair(ref seedCount, seeds, low565, high565);
            AddSeedPair(ref seedCount, seeds, high565, low565);
        }

        private static int WeightedColorScore(Rgba32 color, ColorWeights weights)
        {
            return (weights.R * color.R) + (weights.G * color.G) + (weights.B * color.B);
        }

        private static void AddSeedPair(ref int seedCount, Span<ColorEndpointPair> seeds, ushort color0, ushort color1)
        {
            ColorEndpointPair pair = new(color0, color1);
            for (int i = 0; i < seedCount; i++)
            {
                if (seeds[i] == pair)
                    return;
            }

            if (seedCount < seeds.Length)
                seeds[seedCount++] = pair;
        }

        private static int BuildEndpointNeighbors(ushort endpoint, Span<ushort> neighbors)
        {
            int r = (endpoint >> 11) & 31;
            int g = (endpoint >> 5) & 63;
            int b = endpoint & 31;
            int count = 0;
            for (int dr = -ColorEndpointSearchRadius; dr <= ColorEndpointSearchRadius; dr++)
            {
                int nr = r + dr;
                if ((uint)nr > 31)
                    continue;

                for (int dg = -ColorEndpointSearchRadius; dg <= ColorEndpointSearchRadius; dg++)
                {
                    int ng = g + dg;
                    if ((uint)ng > 63)
                        continue;

                    for (int db = -ColorEndpointSearchRadius; db <= ColorEndpointSearchRadius; db++)
                    {
                        int nb = b + db;
                        if ((uint)nb > 31)
                            continue;

                        neighbors[count++] = (ushort)((nr << 11) | (ng << 5) | nb);
                    }
                }
            }

            return count;
        }

        private static ushort QuantizeToRgb565(Rgba32 color)
        {
            int r = ((color.R * 31) + 127) / 255;
            int g = ((color.G * 63) + 127) / 255;
            int b = ((color.B * 31) + 127) / 255;
            return (ushort)((r << 11) | (g << 5) | b);
        }

        private static Rgba32 FromRgb565(ushort value)
        {
            byte r = (byte)(((((value >> 11) & 0x1F) * 527) + 23) >> 6);
            byte g = (byte)(((((value >> 5) & 0x3F) * 259) + 33) >> 6);
            byte b = (byte)((((value & 0x1F) * 527) + 23) >> 6);
            return new Rgba32(r, g, b, 255);
        }

        private static Rgba32 InterpolateColor(Rgba32 c0, Rgba32 c1, int weight0, int weight1)
        {
            int total = weight0 + weight1;
            byte r = (byte)(((c0.R * weight0) + (c1.R * weight1)) / total);
            byte g = (byte)(((c0.G * weight0) + (c1.G * weight1)) / total);
            byte b = (byte)(((c0.B * weight0) + (c1.B * weight1)) / total);
            return new Rgba32(r, g, b, 255);
        }

        private static byte[] EncodeBc4(Image<Rgba32> image, ColorComponent component)
        {
            int blocksX = (image.Width + 3) >> 2;
            int blocksY = (image.Height + 3) >> 2;
            byte[] result = new byte[blocksX * blocksY * 8];

            image.ProcessPixelRows(accessor =>
            {
                Span<byte> values = stackalloc byte[16];
                for (int blockY = 0; blockY < blocksY; blockY++)
                {
                    for (int blockX = 0; blockX < blocksX; blockX++)
                    {
                        int valueIndex = 0;
                        for (int y = 0; y < 4; y++)
                        {
                            int sourceY = Math.Min((blockY * 4) + y, accessor.Height - 1);
                            Span<Rgba32> row = accessor.GetRowSpan(sourceY);
                            for (int x = 0; x < 4; x++)
                            {
                                int sourceX = Math.Min((blockX * 4) + x, row.Length - 1);
                                values[valueIndex++] = GetComponent(row[sourceX], component);
                            }
                        }

                        int offset = ((blockY * blocksX) + blockX) * 8;
                        EncodeAlphaBlock(values, result.AsSpan(offset, 8));
                    }
                }
            });

            return result;
        }

        private static void EncodeAlphaBlock(ReadOnlySpan<byte> values, Span<byte> destination)
        {
            byte min = byte.MaxValue;
            byte max = byte.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                min = Math.Min(min, values[i]);
                max = Math.Max(max, values[i]);
            }

            if (min == max)
            {
                destination[0] = max;
                destination[1] = min;
                destination[2] = 0;
                destination[3] = 0;
                destination[4] = 0;
                destination[5] = 0;
                destination[6] = 0;
                destination[7] = 0;
                return;
            }

            int bestError = int.MaxValue;
            byte bestA0 = max;
            byte bestA1 = min;
            ulong bestSelectors = 0;

            int lowStart = Math.Max(0, min - AlphaEndpointSearchRadius);
            int lowEnd = Math.Min(255, min + AlphaEndpointSearchRadius);
            int highStart = Math.Max(0, max - AlphaEndpointSearchRadius);
            int highEnd = Math.Min(255, max + AlphaEndpointSearchRadius);

            for (int a0 = highStart; a0 <= highEnd; a0++)
            {
                for (int a1 = lowStart; a1 <= lowEnd; a1++)
                {
                    if (a0 <= a1)
                        continue;

                    TryAlphaEndpointPair(values, (byte)a0, (byte)a1, ref bestError, ref bestA0, ref bestA1, ref bestSelectors);
                }
            }

            for (int a0 = lowStart; a0 <= lowEnd; a0++)
            {
                for (int a1 = highStart; a1 <= highEnd; a1++)
                {
                    if (a0 > a1)
                        continue;

                    TryAlphaEndpointPair(values, (byte)a0, (byte)a1, ref bestError, ref bestA0, ref bestA1, ref bestSelectors);
                }
            }

            destination[0] = bestA0;
            destination[1] = bestA1;
            WriteUInt48LittleEndian(destination[2..], bestSelectors);
        }

        private static void TryAlphaEndpointPair(
            ReadOnlySpan<byte> values,
            byte a0,
            byte a1,
            ref int bestError,
            ref byte bestA0,
            ref byte bestA1,
            ref ulong bestSelectors)
        {
            int error = EvaluateAlphaEndpointPair(values, a0, a1, out ulong selectors);
            if (error >= bestError)
                return;

            bestError = error;
            bestA0 = a0;
            bestA1 = a1;
            bestSelectors = selectors;
        }

        private static int EvaluateAlphaEndpointPair(ReadOnlySpan<byte> values, byte a0, byte a1, out ulong selectors)
        {
            Span<int> table = stackalloc int[8];
            BuildAlphaEndpointTable((ushort)(a0 | (a1 << 8)), table);

            int error = 0;
            selectors = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int bestSelector = 0;
                int bestError = int.MaxValue;
                for (int selector = 0; selector < table.Length; selector++)
                {
                    int candidateError = Squared(values[i] - table[selector]);
                    if (candidateError >= bestError)
                        continue;

                    bestError = candidateError;
                    bestSelector = selector;
                    if (bestError == 0)
                        break;
                }

                error += bestError;
                if (error < 0)
                    error = int.MaxValue;

                selectors |= (ulong)bestSelector << (i * 3);
            }

            return error;
        }

        private static void BuildAlphaEndpointTable(ushort endpoint, Span<int> table)
        {
            int alpha0 = endpoint & 0xFF;
            int alpha1 = endpoint >> 8;
            table[0] = alpha0;
            table[1] = alpha1;
            if (alpha0 > alpha1)
            {
                table[2] = ((6 * alpha0) + alpha1) / 7;
                table[3] = ((5 * alpha0) + (2 * alpha1)) / 7;
                table[4] = ((4 * alpha0) + (3 * alpha1)) / 7;
                table[5] = ((3 * alpha0) + (4 * alpha1)) / 7;
                table[6] = ((2 * alpha0) + (5 * alpha1)) / 7;
                table[7] = (alpha0 + (6 * alpha1)) / 7;
            }
            else
            {
                table[2] = ((4 * alpha0) + alpha1) / 5;
                table[3] = ((3 * alpha0) + (2 * alpha1)) / 5;
                table[4] = ((2 * alpha0) + (3 * alpha1)) / 5;
                table[5] = (alpha0 + (4 * alpha1)) / 5;
                table[6] = 0;
                table[7] = 255;
            }
        }

        private static int Squared(int value)
        {
            return value * value;
        }

        private static byte GetComponent(Rgba32 pixel, ColorComponent component)
        {
            return component switch
            {
                ColorComponent.R => pixel.R,
                ColorComponent.G => pixel.G,
                ColorComponent.B => pixel.B,
                ColorComponent.A => pixel.A,
                _ => pixel.A,
            };
        }

        private static void WriteUInt48LittleEndian(Span<byte> destination, ulong value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
            destination[4] = (byte)(value >> 32);
            destination[5] = (byte)(value >> 40);
        }

        private readonly record struct ColorWeights(int R, int G, int B);

        private readonly record struct ColorEndpointPair(ushort Color0, ushort Color1);

        public void Dispose()
        {
        }
    }

    private sealed class CrnEncodingBuilder
    {
        private const int AlphaEndpointRetargetCandidates = 24;
        private const int ExhaustiveAlphaEndpointRetargetLimit = 128;
        private const int AlphaEndpointRefineRadius = 16;

        private readonly CrnFormat _format;
        private readonly int _width;
        private readonly int _height;
        private readonly int _levels;
        private readonly int _faces;
        private readonly PaletteLimits _paletteLimits;
        private readonly uint _userData0;
        private readonly uint _userData1;
        private readonly List<EncodedLevel> _encodedLevels = [];

        private readonly List<uint> _colorEndpoints = [];
        private readonly List<uint> _colorSelectors = [];
        private readonly List<ushort> _alphaEndpoints = [];
        private readonly List<ulong> _alphaSelectors = [];

        private readonly Dictionary<uint, ushort> _colorEndpointMap = [];
        private readonly Dictionary<uint, ushort> _colorSelectorMap = [];
        private readonly Dictionary<ushort, ushort> _alphaEndpointMap = [];
        private readonly Dictionary<ulong, ushort> _alphaSelectorMap = [];

        public CrnEncodingBuilder(CrnFormat format, int width, int height, int levels, int faces, CrnCompressionQuality quality, uint userData0, uint userData1)
        {
            _format = format;
            _width = width;
            _height = height;
            _levels = levels;
            _faces = faces;
            _paletteLimits = CreatePaletteLimits(format, quality, faces);
            _userData0 = userData0;
            _userData1 = userData1;
        }

        public void AddLevel(int width, int height, byte[] bcnBlocks)
        {
            AddLevel(width, height, [bcnBlocks]);
        }

        public void AddLevel(int width, int height, IReadOnlyList<byte[]> faceBlocks)
        {
            int bytesPerBlock = CrnFormatHelpers.BytesPerBlock(_format);
            bool hasColor = CrnFormatHelpers.HasColorPalette(_format);
            bool hasAlpha = CrnFormatHelpers.HasAlphaPalette(_format);
            bool dualAlpha = CrnFormatHelpers.IsDualAlpha(_format);
            int blocksX = (width + 3) >> 2;
            int blocksY = (height + 3) >> 2;
            int roundedBlocksX = (blocksX + 1) & ~1;
            int roundedBlocksY = (blocksY + 1) & ~1;

            if (faceBlocks.Count != _faces)
                throw new InvalidDataException("BCn encoder returned an unexpected face count.");

            int faceBlockSlots = roundedBlocksX * roundedBlocksY;
            int blockSlots = _faces * faceBlockSlots;
            ushort[] colorEndpointIndices = hasColor ? new ushort[blockSlots] : [];
            ushort[] colorSelectorIndices = hasColor ? new ushort[blockSlots] : [];
            ushort[] alphaEndpointIndices = hasAlpha ? new ushort[blockSlots] : [];
            ushort[] alphaSelectorIndices = hasAlpha ? new ushort[blockSlots] : [];
            ushort[] alphaEndpointIndices1 = dualAlpha ? new ushort[blockSlots] : [];
            ushort[] alphaSelectorIndices1 = dualAlpha ? new ushort[blockSlots] : [];
            Span<byte> paddingBlock = stackalloc byte[16];

            for (int face = 0; face < _faces; face++)
            {
                byte[] bcnBlocks = faceBlocks[face];
                if (bcnBlocks.Length != blocksX * blocksY * bytesPerBlock)
                    throw new InvalidDataException("BCn encoder returned an unexpected block payload length.");

                for (int y = 0; y < roundedBlocksY; y++)
                {
                    for (int x = 0; x < roundedBlocksX; x++)
                    {
                        int destinationIndex = (face * faceBlockSlots) + (y * roundedBlocksX) + x;
                        bool visible = x < blocksX && y < blocksY;
                        ReadOnlySpan<byte> block = visible
                            ? bcnBlocks.AsSpan(((y * blocksX) + x) * bytesPerBlock, bytesPerBlock)
                            : paddingBlock;

                        if (CrnFormatHelpers.IsColorOnly(_format))
                        {
                            uint endpoint = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block[..4]) : 0;
                            uint selector = visible ? BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(4, 4)) : 0;
                            colorEndpointIndices[destinationIndex] = GetColorEndpointIndex(endpoint);
                            colorSelectorIndices[destinationIndex] = GetColorSelectorIndex(selector);
                        }
                        else if (CrnFormatHelpers.IsColorAlpha(_format))
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
                        else if (CrnFormatHelpers.IsAlphaOnly(_format))
                        {
                            ushort alphaEndpoint = visible ? BinaryPrimitives.ReadUInt16LittleEndian(block[..2]) : (ushort)0;
                            ulong alphaSelector = visible ? ReadUInt48LittleEndian(block.Slice(2, 6)) : 0UL;
                            alphaEndpointIndices[destinationIndex] = GetAlphaEndpointIndex(alphaEndpoint);
                            alphaSelectorIndices[destinationIndex] = GetAlphaSelectorIndex(alphaSelector);
                        }
                        else if (CrnFormatHelpers.IsDualAlpha(_format))
                        {
                            ushort alphaEndpoint0 = visible ? BinaryPrimitives.ReadUInt16LittleEndian(block[..2]) : (ushort)0;
                            ulong alphaSelector0 = visible ? ReadUInt48LittleEndian(block.Slice(2, 6)) : 0UL;
                            ushort alphaEndpoint1 = visible ? BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(8, 2)) : (ushort)0;
                            ulong alphaSelector1 = visible ? ReadUInt48LittleEndian(block.Slice(10, 6)) : 0UL;

                            alphaEndpointIndices[destinationIndex] = GetAlphaEndpointIndex(alphaEndpoint0);
                            alphaSelectorIndices[destinationIndex] = GetAlphaSelectorIndex(alphaSelector0);
                            alphaEndpointIndices1[destinationIndex] = GetAlphaEndpointIndex(alphaEndpoint1);
                            alphaSelectorIndices1[destinationIndex] = GetAlphaSelectorIndex(alphaSelector1);
                        }
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
                alphaSelectorIndices,
                alphaEndpointIndices1,
                alphaSelectorIndices1));
        }

        public byte[] Build()
        {
            if (_encodedLevels.Count != _levels)
                throw new InvalidOperationException("Not all CRN mip levels have been encoded.");

            ApplyPaletteLimits();
            return BuildWithBestPaletteOrder();
        }

        private byte[] BuildCore()
        {
            bool hasColor = CrnFormatHelpers.HasColorPalette(_format);
            bool hasAlpha = CrnFormatHelpers.HasAlphaPalette(_format);
            List<LevelSymbolStream> levelSymbols = BuildLevelSymbols();
            byte[] colorEndpointData = hasColor ? EncodeColorEndpoints() : [];
            byte[] colorSelectorData = hasColor ? EncodeColorSelectors() : [];
            byte[] alphaEndpointData = hasAlpha ? EncodeAlphaEndpoints() : [];
            byte[] alphaSelectorData = hasAlpha ? EncodeAlphaSelectors() : [];
            byte[] tableData = EncodeTables(levelSymbols);
            byte[][] levelData = EncodeLevels(levelSymbols);

            int headerSize = CrnHeader.FixedHeaderSize + ((_levels - 1) * 4);
            List<byte> file = new(headerSize + colorEndpointData.Length + colorSelectorData.Length + alphaEndpointData.Length + alphaSelectorData.Length + tableData.Length);
            file.AddRange(new byte[headerSize]);

            CrnPalette colorEndpointPalette = hasColor ? AppendPalette(file, colorEndpointData, _colorEndpoints.Count) : default;
            CrnPalette colorSelectorPalette = hasColor ? AppendPalette(file, colorSelectorData, _colorSelectors.Count) : default;
            CrnPalette alphaEndpointPalette = hasAlpha ? AppendPalette(file, alphaEndpointData, _alphaEndpoints.Count) : default;
            CrnPalette alphaSelectorPalette = hasAlpha ? AppendPalette(file, alphaSelectorData, _alphaSelectors.Count) : default;

            int tablesOffset = file.Count;
            file.AddRange(tableData);
            int tablesSize = tableData.Length;
            if (tablesSize > ushort.MaxValue)
                throw new InvalidDataException("CRN table data exceeds the 16-bit Unity Crunch table size field.");

            int[] levelOffsets = new int[_levels];
            for (int i = 0; i < levelData.Length; i++)
            {
                levelOffsets[i] = file.Count;
                file.AddRange(levelData[i]);
            }

            byte[] result = file.ToArray();
            CrnHeader.Write(
                result,
                headerSize,
                result.Length,
                _width,
                _height,
                _levels,
                _format,
                _userData0,
                _userData1,
                colorEndpointPalette,
                colorSelectorPalette,
                alphaEndpointPalette,
                alphaSelectorPalette,
                tablesOffset,
                tablesSize,
                levelOffsets,
                _faces);
            CrnHeader.FinalizeChecksums(result, headerSize);
            return result;
        }

        private byte[] BuildWithBestPaletteOrder()
        {
            byte[] best = BuildCore();

            bool improved;
            do
            {
                improved = false;
                improved |= TryImprovePaletteOrder(ReorderColorEndpointsByFrequency, ref best);
                improved |= TryImprovePaletteOrder(ReorderColorSelectorsByFrequency, ref best);
                improved |= TryImprovePaletteOrder(ReorderAlphaEndpointsByFrequency, ref best);
                improved |= TryImprovePaletteOrder(ReorderAlphaSelectorsByFrequency, ref best);

                improved |= TryImprovePaletteOrder(ReorderColorEndpoints, ref best);
                improved |= TryImprovePaletteOrder(ReorderColorSelectors, ref best);
                improved |= TryImprovePaletteOrder(ReorderAlphaEndpoints, ref best);
                improved |= TryImprovePaletteOrder(ReorderAlphaSelectors, ref best);

                foreach (float weight in new[] { 0.0f, 0.5f, 1.0f })
                {
                    float zengWeight = weight;
                    improved |= TryImprovePaletteOrder(() => ReorderColorEndpointsByZeng(zengWeight), ref best);
                    improved |= TryImprovePaletteOrder(() => ReorderColorSelectorsByZeng(zengWeight), ref best);
                    improved |= TryImprovePaletteOrder(() => ReorderAlphaEndpointsByZeng(zengWeight), ref best);
                    improved |= TryImprovePaletteOrder(() => ReorderAlphaSelectorsByZeng(zengWeight), ref best);
                }
            }
            while (improved);

            return best;
        }

        private bool TryImprovePaletteOrder(Func<bool> reorder, ref byte[] best)
        {
            PaletteSnapshot snapshot = CapturePaletteState();
            if (!reorder())
                return false;

            byte[] candidate = BuildCore();
            if (candidate.Length < best.Length)
            {
                best = candidate;
                return true;
            }

            RestorePaletteState(snapshot);
            return false;
        }

        private List<LevelSymbolStream> BuildLevelSymbols()
        {
            List<LevelSymbolStream> levels = new(_encodedLevels.Count);
            foreach (EncodedLevel level in _encodedLevels)
            {
                LevelSymbolStream stream = new();
                int currentColorEndpoint = 0;
                int currentAlphaEndpoint = 0;
                int currentAlphaEndpoint1 = 0;
                bool hasColor = CrnFormatHelpers.HasColorPalette(_format);
                bool hasAlpha = CrnFormatHelpers.HasAlphaPalette(_format);
                bool dualAlpha = CrnFormatHelpers.IsDualAlpha(_format);

                int faceBlockSlots = level.RoundedBlocksX * level.RoundedBlocksY;
                int chunksX = (level.RoundedBlocksX + 1) >> 1;
                int chunksY = (level.RoundedBlocksY + 1) >> 1;
                int[] referenceGroups = new int[_faces * chunksX * chunksY];
                EndpointState currentEndpoint = default;
                for (int face = 0; face < _faces; face++)
                {
                    EndpointState[] blockBuffer = new EndpointState[level.RoundedBlocksX];
                    for (int y = 0; y < level.RoundedBlocksY; y++)
                    {
                        for (int x = 0; x < level.RoundedBlocksX; x++)
                        {
                            int index = (face * faceBlockSlots) + (y * level.RoundedBlocksX) + x;
                            EndpointState endpoint = ReadEndpointState(level, index, hasColor, hasAlpha, dualAlpha);
                            int endpointReference = SelectEndpointReference(endpoint, currentEndpoint, blockBuffer[x], y);
                            SetReferenceGroup(referenceGroups, face, chunksX, chunksY, y, x, endpointReference);

                            if (endpointReference == 0)
                            {
                                if (hasColor)
                                {
                                    int colorDelta = PositiveModulo(endpoint.Color - currentColorEndpoint, _colorEndpoints.Count);
                                    stream.ColorEndpointDeltas.Add(colorDelta);
                                    currentColorEndpoint = endpoint.Color;
                                }

                                if (hasAlpha)
                                {
                                    int alphaDelta = PositiveModulo(endpoint.Alpha0 - currentAlphaEndpoint, _alphaEndpoints.Count);
                                    stream.AlphaEndpointDeltas.Add(alphaDelta);
                                    currentAlphaEndpoint = endpoint.Alpha0;

                                    if (dualAlpha)
                                    {
                                        int alphaDelta1 = PositiveModulo(endpoint.Alpha1 - currentAlphaEndpoint1, _alphaEndpoints.Count);
                                        stream.AlphaEndpointDeltas1.Add(alphaDelta1);
                                        currentAlphaEndpoint1 = endpoint.Alpha1;
                                    }
                                }

                                currentEndpoint = endpoint;
                                blockBuffer[x] = endpoint;
                            }
                            else if (endpointReference == 1)
                            {
                                blockBuffer[x] = currentEndpoint;
                            }
                            else
                            {
                                currentEndpoint = blockBuffer[x];
                                currentColorEndpoint = currentEndpoint.Color;
                                currentAlphaEndpoint = currentEndpoint.Alpha0;
                                currentAlphaEndpoint1 = currentEndpoint.Alpha1;
                            }

                            if (hasColor)
                                stream.ColorSelectorIndices.Add(level.ColorSelectorIndices[index]);

                            if (hasAlpha)
                            {
                                stream.AlphaSelectorIndices.Add(level.AlphaSelectorIndices[index]);

                                if (dualAlpha)
                                    stream.AlphaSelectorIndices1.Add(level.AlphaSelectorIndices1[index]);
                            }
                        }
                    }
                }

                stream.ReferenceGroups.AddRange(referenceGroups);
                levels.Add(stream);
            }

            return levels;
        }

        private byte[] EncodeTables(List<LevelSymbolStream> levels)
        {
            bool hasColor = CrnFormatHelpers.HasColorPalette(_format);
            bool hasAlpha = CrnFormatHelpers.HasAlphaPalette(_format);
            bool dualAlpha = CrnFormatHelpers.IsDualAlpha(_format);
            StaticHuffmanModel referenceModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ReferenceGroups));
            StaticHuffmanModel? colorEndpointModel = hasColor ? StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorEndpointDeltas)) : null;
            StaticHuffmanModel? colorSelectorModel = hasColor ? StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorSelectorIndices)) : null;
            StaticHuffmanModel? alphaEndpointModel = null;
            StaticHuffmanModel? alphaSelectorModel = null;

            if (hasAlpha)
            {
                alphaEndpointModel = StaticHuffmanModel.CreateForSymbols(dualAlpha
                    ? levels.SelectMany(x => x.AlphaEndpointDeltas.Concat(x.AlphaEndpointDeltas1))
                    : levels.SelectMany(x => x.AlphaEndpointDeltas));
                alphaSelectorModel = StaticHuffmanModel.CreateForSymbols(dualAlpha
                    ? levels.SelectMany(x => x.AlphaSelectorIndices.Concat(x.AlphaSelectorIndices1))
                    : levels.SelectMany(x => x.AlphaSelectorIndices));
            }

            BitWriter writer = new();
            referenceModel.Transmit(writer);
            colorEndpointModel?.Transmit(writer);
            colorSelectorModel?.Transmit(writer);
            alphaEndpointModel?.Transmit(writer);
            alphaSelectorModel?.Transmit(writer);
            return writer.Finish();
        }

        private byte[][] EncodeLevels(List<LevelSymbolStream> levels)
        {
            bool hasColor = CrnFormatHelpers.HasColorPalette(_format);
            bool hasAlpha = CrnFormatHelpers.HasAlphaPalette(_format);
            bool dualAlpha = CrnFormatHelpers.IsDualAlpha(_format);
            StaticHuffmanModel referenceModel = StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ReferenceGroups));
            StaticHuffmanModel? colorEndpointModel = hasColor ? StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorEndpointDeltas)) : null;
            StaticHuffmanModel? colorSelectorModel = hasColor ? StaticHuffmanModel.CreateForSymbols(levels.SelectMany(x => x.ColorSelectorIndices)) : null;
            StaticHuffmanModel? alphaEndpointModel = null;
            StaticHuffmanModel? alphaSelectorModel = null;

            if (hasAlpha)
            {
                alphaEndpointModel = StaticHuffmanModel.CreateForSymbols(dualAlpha
                    ? levels.SelectMany(x => x.AlphaEndpointDeltas.Concat(x.AlphaEndpointDeltas1))
                    : levels.SelectMany(x => x.AlphaEndpointDeltas));
                alphaSelectorModel = StaticHuffmanModel.CreateForSymbols(dualAlpha
                    ? levels.SelectMany(x => x.AlphaSelectorIndices.Concat(x.AlphaSelectorIndices1))
                    : levels.SelectMany(x => x.AlphaSelectorIndices));
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
                int chunksX = (encodedLevel.RoundedBlocksX + 1) >> 1;
                int chunksY = (encodedLevel.RoundedBlocksY + 1) >> 1;
                for (int face = 0; face < _faces; face++)
                {
                    for (int y = 0; y < encodedLevel.RoundedBlocksY; y++)
                    {
                        for (int x = 0; x < encodedLevel.RoundedBlocksX; x++)
                        {
                            if ((y & 1) == 0 && (x & 1) == 0)
                                referenceModel.Encode(writer, level.ReferenceGroups[referenceIndex++]);

                            int endpointReference = GetEndpointReference(level.ReferenceGroups, face, chunksX, chunksY, y, x);
                            if (endpointReference == 0)
                            {
                                if (hasColor)
                                    colorEndpointModel!.Encode(writer, level.ColorEndpointDeltas[endpointIndex]);

                                if (hasAlpha)
                                {
                                    alphaEndpointModel!.Encode(writer, level.AlphaEndpointDeltas[endpointIndex]);
                                    if (dualAlpha)
                                        alphaEndpointModel.Encode(writer, level.AlphaEndpointDeltas1[endpointIndex]);
                                }

                                endpointIndex++;
                            }

                            if (hasColor)
                                colorSelectorModel!.Encode(writer, level.ColorSelectorIndices[selectorIndex]);

                            if (hasAlpha)
                            {
                                alphaSelectorModel!.Encode(writer, level.AlphaSelectorIndices[selectorIndex]);
                                if (dualAlpha)
                                    alphaSelectorModel.Encode(writer, level.AlphaSelectorIndices1[selectorIndex]);
                            }

                            selectorIndex++;
                        }
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
            return GetPaletteIndex(value, _colorEndpoints, _colorEndpointMap, _paletteLimits.ColorEndpoints, ColorEndpointDistance);
        }

        private ushort GetColorSelectorIndex(uint value)
        {
            return GetPaletteIndex(value, _colorSelectors, _colorSelectorMap, _paletteLimits.ColorSelectors, ColorSelectorDistance);
        }

        private ushort GetAlphaEndpointIndex(ushort value)
        {
            return GetPaletteIndex(value, _alphaEndpoints, _alphaEndpointMap, _paletteLimits.AlphaEndpoints, AlphaEndpointDistance);
        }

        private ushort GetAlphaSelectorIndex(ulong value)
        {
            return GetPaletteIndex(value, _alphaSelectors, _alphaSelectorMap, _paletteLimits.AlphaSelectors, AlphaSelectorDistance);
        }

        private static ushort GetPaletteIndex<T>(T value, List<T> palette, Dictionary<T, ushort> map, int maxEntries, Func<T, T, int> distance)
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

        private void ApplyPaletteLimits()
        {
            bool preserveRange = _format is CrnFormat.Dxt5CcxY or
                CrnFormat.Dxt5XGxR or
                CrnFormat.Dxt5XGbr or
                CrnFormat.Dxt5Agbr or
                CrnFormat.DxnXy or
                CrnFormat.DxnYx or
                CrnFormat.Dxt5A;
            bool retargetAlphaBlocks = CrnFormatHelpers.HasAlphaPalette(_format);

            uint[] sourceColorEndpoints = _colorEndpoints.ToArray();
            uint[] sourceColorSelectors = _colorSelectors.ToArray();
            ushort[][] sourceColorEndpointIndices = _encodedLevels.Select(x => x.ColorEndpointIndices.ToArray()).ToArray();
            ushort[][] sourceColorSelectorIndices = _encodedLevels.Select(x => x.ColorSelectorIndices.ToArray()).ToArray();
            ushort[] sourceAlphaEndpoints = _alphaEndpoints.ToArray();
            ulong[] sourceAlphaSelectors = _alphaSelectors.ToArray();
            ushort[][] sourceAlphaEndpointIndices = _encodedLevels.Select(x => x.AlphaEndpointIndices.ToArray()).ToArray();
            ushort[][] sourceAlphaEndpointIndices1 = _encodedLevels.Select(x => x.AlphaEndpointIndices1.ToArray()).ToArray();
            ushort[][] sourceAlphaSelectorIndices = _encodedLevels.Select(x => x.AlphaSelectorIndices.ToArray()).ToArray();
            ushort[][] sourceAlphaSelectorIndices1 = _encodedLevels.Select(x => x.AlphaSelectorIndices1.ToArray()).ToArray();

            PaletteRefit<uint> colorEndpointRefit = RefitPalette(
                _colorEndpoints,
                _paletteLimits.ColorEndpoints,
                ColorEndpointDistance,
                _encodedLevels.Select(x => x.ColorEndpointIndices),
                preserveRange);
            if (colorEndpointRefit.Changed)
                RetargetColorSelectors(sourceColorEndpointIndices, colorEndpointRefit);

            ushort[][] oldColorSelectorIndices = _encodedLevels.Select(x => x.ColorSelectorIndices.ToArray()).ToArray();
            PaletteRefit<uint> colorSelectorRefit = RefitPalette(
                _colorSelectors,
                _paletteLimits.ColorSelectors,
                ColorSelectorDistance,
                _encodedLevels.Select(x => x.ColorSelectorIndices),
                preserveRange);
            if (colorSelectorRefit.Changed)
                RetargetReducedColorSelectors(oldColorSelectorIndices, colorSelectorRefit);

            if (preserveRange && CrnFormatHelpers.HasColorPalette(_format))
            {
                RetargetReducedColorBlocks(sourceColorEndpointIndices, sourceColorSelectorIndices, sourceColorEndpoints, sourceColorSelectors);
                RefineReducedColorSelectors(sourceColorEndpointIndices, sourceColorSelectorIndices, sourceColorEndpoints, sourceColorSelectors);
                RetargetReducedColorBlocks(sourceColorEndpointIndices, sourceColorSelectorIndices, sourceColorEndpoints, sourceColorSelectors);
            }

            PaletteRefit<ushort> alphaEndpointRefit = RefitPalette(
                _alphaEndpoints,
                _paletteLimits.AlphaEndpoints,
                AlphaEndpointDistance,
                _encodedLevels.SelectMany(x => new[] { x.AlphaEndpointIndices, x.AlphaEndpointIndices1 }),
                preserveRange);
            if (preserveRange && alphaEndpointRefit.Changed)
                RetargetAlphaSelectors(sourceAlphaEndpointIndices, sourceAlphaEndpointIndices1, alphaEndpointRefit);

            ushort[][] oldAlphaSelectorIndices = _encodedLevels.Select(x => x.AlphaSelectorIndices.ToArray()).ToArray();
            ushort[][] oldAlphaSelectorIndices1 = _encodedLevels.Select(x => x.AlphaSelectorIndices1.ToArray()).ToArray();
            PaletteRefit<ulong> alphaSelectorRefit = RefitPalette(
                _alphaSelectors,
                _paletteLimits.AlphaSelectors,
                AlphaSelectorDistance,
                _encodedLevels.SelectMany(x => new[] { x.AlphaSelectorIndices, x.AlphaSelectorIndices1 }),
                preserveRange);
            if (alphaSelectorRefit.Changed)
                RetargetReducedAlphaSelectors(oldAlphaSelectorIndices, oldAlphaSelectorIndices1, alphaSelectorRefit);

            if (retargetAlphaBlocks)
            {
                RetargetReducedAlphaBlocks(
                    sourceAlphaEndpointIndices,
                    sourceAlphaSelectorIndices,
                    sourceAlphaEndpointIndices1,
                    sourceAlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors);
                RefineReducedAlphaSelectors(
                    sourceAlphaEndpointIndices,
                    sourceAlphaSelectorIndices,
                    sourceAlphaEndpointIndices1,
                    sourceAlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors);
                RetargetReducedAlphaBlocks(
                    sourceAlphaEndpointIndices,
                    sourceAlphaSelectorIndices,
                    sourceAlphaEndpointIndices1,
                    sourceAlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors);
                RefineReducedAlphaEndpoints(
                    sourceAlphaEndpointIndices,
                    sourceAlphaSelectorIndices,
                    sourceAlphaEndpointIndices1,
                    sourceAlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors);
                RetargetReducedAlphaBlocks(
                    sourceAlphaEndpointIndices,
                    sourceAlphaSelectorIndices,
                    sourceAlphaEndpointIndices1,
                    sourceAlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors);
            }
        }

        private void RefineReducedAlphaEndpoints(
            ushort[][] sourceAlphaEndpointIndices,
            ushort[][] sourceAlphaSelectorIndices,
            ushort[][] sourceAlphaEndpointIndices1,
            ushort[][] sourceAlphaSelectorIndices1,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors)
        {
            if (_alphaEndpoints.Count == 0 || _alphaSelectors.Count == 0 || sourceAlphaEndpoints.Length == 0 || sourceAlphaSelectors.Length == 0)
                return;

            List<AlphaEndpointTrainingBlock>[] training = new List<AlphaEndpointTrainingBlock>[_alphaEndpoints.Count];
            for (int i = 0; i < training.Length; i++)
                training[i] = [];

            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                AddAlphaEndpointTrainingBlocks(
                    sourceAlphaEndpointIndices[i],
                    sourceAlphaSelectorIndices[i],
                    level.AlphaEndpointIndices,
                    level.AlphaSelectorIndices,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    training);

                AddAlphaEndpointTrainingBlocks(
                    sourceAlphaEndpointIndices1[i],
                    sourceAlphaSelectorIndices1[i],
                    level.AlphaEndpointIndices1,
                    level.AlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    training);
            }

            for (int endpointIndex = 0; endpointIndex < _alphaEndpoints.Count; endpointIndex++)
            {
                List<AlphaEndpointTrainingBlock> blocks = training[endpointIndex];
                if (blocks.Count == 0)
                    continue;

                ushort current = _alphaEndpoints[endpointIndex];
                int currentA0 = current & 0xFF;
                int currentA1 = current >> 8;
                long bestError = AlphaEndpointTrainingError(current, blocks, long.MaxValue);
                ushort bestEndpoint = current;

                int a0Start = Math.Max(0, currentA0 - AlphaEndpointRefineRadius);
                int a0End = Math.Min(255, currentA0 + AlphaEndpointRefineRadius);
                int a1Start = Math.Max(0, currentA1 - AlphaEndpointRefineRadius);
                int a1End = Math.Min(255, currentA1 + AlphaEndpointRefineRadius);
                for (int a0 = a0Start; a0 <= a0End; a0++)
                {
                    for (int a1 = a1Start; a1 <= a1End; a1++)
                    {
                        ushort candidate = (ushort)(a0 | (a1 << 8));
                        long error = AlphaEndpointTrainingError(candidate, blocks, bestError);
                        if (error >= bestError)
                            continue;

                        bestError = error;
                        bestEndpoint = candidate;
                    }
                }

                _alphaEndpoints[endpointIndex] = bestEndpoint;
            }
        }

        private void AddAlphaEndpointTrainingBlocks(
            ushort[] sourceEndpointIndices,
            ushort[] sourceSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors,
            List<AlphaEndpointTrainingBlock>[] training)
        {
            if (sourceEndpointIndices.Length == 0 || sourceSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            for (int i = 0; i < endpointIndices.Length; i++)
            {
                training[endpointIndices[i]].Add(new AlphaEndpointTrainingBlock(
                    sourceAlphaEndpoints[sourceEndpointIndices[i]],
                    sourceAlphaSelectors[sourceSelectorIndices[i]],
                    _alphaSelectors[selectorIndices[i]]));
            }
        }

        private static long AlphaEndpointTrainingError(
            ushort candidateEndpoint,
            List<AlphaEndpointTrainingBlock> blocks,
            long limit)
        {
            Span<int> targetTable = stackalloc int[8];
            Span<int> candidateTable = stackalloc int[8];
            BuildAlphaEndpointTable(candidateEndpoint, candidateTable);

            long error = 0;
            foreach (AlphaEndpointTrainingBlock block in blocks)
            {
                BuildAlphaEndpointTable(block.TargetEndpoint, targetTable);
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int targetValue = targetTable[(int)((block.TargetSelector >> (pixel * 3)) & 7)];
                    int candidateValue = candidateTable[(int)((block.Selector >> (pixel * 3)) & 7)];
                    error += Squared(targetValue - candidateValue);
                    if (error >= limit)
                        return error;
                }
            }

            return error;
        }

        private void RefineReducedAlphaSelectors(
            ushort[][] sourceAlphaEndpointIndices,
            ushort[][] sourceAlphaSelectorIndices,
            ushort[][] sourceAlphaEndpointIndices1,
            ushort[][] sourceAlphaSelectorIndices1,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors)
        {
            if (_alphaEndpoints.Count == 0 || _alphaSelectors.Count == 0 || sourceAlphaEndpoints.Length == 0 || sourceAlphaSelectors.Length == 0)
                return;

            long[] errors = new long[_alphaSelectors.Count * 16 * 8];
            bool[] used = new bool[_alphaSelectors.Count];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                AccumulateAlphaSelectorErrors(
                    sourceAlphaEndpointIndices[i],
                    sourceAlphaSelectorIndices[i],
                    level.AlphaEndpointIndices,
                    level.AlphaSelectorIndices,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    errors,
                    used);

                AccumulateAlphaSelectorErrors(
                    sourceAlphaEndpointIndices1[i],
                    sourceAlphaSelectorIndices1[i],
                    level.AlphaEndpointIndices1,
                    level.AlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    errors,
                    used);
            }

            for (int selectorIndex = 0; selectorIndex < _alphaSelectors.Count; selectorIndex++)
            {
                if (!used[selectorIndex])
                    continue;

                ulong refined = 0;
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int bestSelector = 0;
                    long bestError = long.MaxValue;
                    int baseOffset = ((selectorIndex * 16) + pixel) * 8;
                    for (int selector = 0; selector < 8; selector++)
                    {
                        long error = errors[baseOffset + selector];
                        if (error >= bestError)
                            continue;

                        bestError = error;
                        bestSelector = selector;
                    }

                    refined |= (ulong)bestSelector << (pixel * 3);
                }

                _alphaSelectors[selectorIndex] = refined;
            }
        }

        private void AccumulateAlphaSelectorErrors(
            ushort[] sourceEndpointIndices,
            ushort[] sourceSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors,
            long[] errors,
            bool[] used)
        {
            if (sourceEndpointIndices.Length == 0 || sourceSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            Span<int> targetTable = stackalloc int[8];
            Span<int> candidateTable = stackalloc int[8];
            for (int i = 0; i < selectorIndices.Length; i++)
            {
                ushort selectorIndex = selectorIndices[i];
                used[selectorIndex] = true;
                ushort targetEndpoint = sourceAlphaEndpoints[sourceEndpointIndices[i]];
                ulong targetSelector = sourceAlphaSelectors[sourceSelectorIndices[i]];
                ushort candidateEndpoint = _alphaEndpoints[endpointIndices[i]];
                BuildAlphaEndpointTable(targetEndpoint, targetTable);
                BuildAlphaEndpointTable(candidateEndpoint, candidateTable);

                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int targetValue = targetTable[(int)((targetSelector >> (pixel * 3)) & 7)];
                    int baseOffset = ((selectorIndex * 16) + pixel) * 8;
                    for (int selector = 0; selector < 8; selector++)
                        errors[baseOffset + selector] += Squared(targetValue - candidateTable[selector]);
                }
            }
        }

        private void RetargetReducedAlphaBlocks(
            ushort[][] sourceAlphaEndpointIndices,
            ushort[][] sourceAlphaSelectorIndices,
            ushort[][] sourceAlphaEndpointIndices1,
            ushort[][] sourceAlphaSelectorIndices1,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors)
        {
            if (_alphaEndpoints.Count == 0 || _alphaSelectors.Count == 0 || sourceAlphaEndpoints.Length == 0 || sourceAlphaSelectors.Length == 0)
                return;

            Dictionary<AlphaEndpointCandidateKey, ushort[]> endpointCandidateCache = [];
            Dictionary<AlphaBlockRemapKey, AlphaBlockChoice> selectorCache = [];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetReducedAlphaBlocks(
                    sourceAlphaEndpointIndices[i],
                    sourceAlphaSelectorIndices[i],
                    level.AlphaEndpointIndices,
                    level.AlphaSelectorIndices,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    endpointCandidateCache,
                    selectorCache);

                RetargetReducedAlphaBlocks(
                    sourceAlphaEndpointIndices1[i],
                    sourceAlphaSelectorIndices1[i],
                    level.AlphaEndpointIndices1,
                    level.AlphaSelectorIndices1,
                    sourceAlphaEndpoints,
                    sourceAlphaSelectors,
                    endpointCandidateCache,
                    selectorCache);
            }
        }

        private void RetargetReducedAlphaBlocks(
            ushort[] sourceEndpointIndices,
            ushort[] sourceSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            ushort[] sourceAlphaEndpoints,
            ulong[] sourceAlphaSelectors,
            Dictionary<AlphaEndpointCandidateKey, ushort[]> endpointCandidateCache,
            Dictionary<AlphaBlockRemapKey, AlphaBlockChoice> selectorCache)
        {
            if (sourceEndpointIndices.Length == 0 || sourceSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            for (int i = 0; i < endpointIndices.Length; i++)
            {
                ushort targetEndpoint = sourceAlphaEndpoints[sourceEndpointIndices[i]];
                ulong targetSelector = sourceAlphaSelectors[sourceSelectorIndices[i]];
                ushort currentEndpointIndex = endpointIndices[i];
                ushort currentSelectorIndex = selectorIndices[i];
                int bestError = AlphaBlockDistance(
                    targetEndpoint,
                    targetSelector,
                    _alphaEndpoints[currentEndpointIndex],
                    _alphaSelectors[currentSelectorIndex]);
                ushort bestEndpointIndex = currentEndpointIndex;
                ushort bestSelectorIndex = currentSelectorIndex;

                AlphaEndpointCandidateKey candidateKey = new(targetEndpoint, currentEndpointIndex);
                if (!endpointCandidateCache.TryGetValue(candidateKey, out ushort[]? candidateEndpointIndices))
                {
                    candidateEndpointIndices = FindNearestAlphaEndpointCandidates(targetEndpoint, currentEndpointIndex);
                    endpointCandidateCache[candidateKey] = candidateEndpointIndices;
                }

                foreach (ushort candidateEndpointIndex in candidateEndpointIndices)
                {
                    ushort candidateEndpoint = _alphaEndpoints[candidateEndpointIndex];
                    AlphaBlockRemapKey remapKey = new(targetEndpoint, targetSelector, candidateEndpoint);
                    if (!selectorCache.TryGetValue(remapKey, out AlphaBlockChoice choice))
                    {
                        ulong retargetedSelector = RetargetAlphaSelector(targetEndpoint, targetSelector, candidateEndpoint);
                        ushort selectorIndex = FindNearestAlphaSelectorForEndpoint(candidateEndpoint, retargetedSelector, currentSelectorIndex);
                        int error = AlphaBlockDistance(targetEndpoint, targetSelector, candidateEndpoint, _alphaSelectors[selectorIndex]);
                        choice = new AlphaBlockChoice(selectorIndex, error);
                        selectorCache[remapKey] = choice;
                    }

                    if (choice.Error >= bestError)
                        continue;

                    bestError = choice.Error;
                    bestEndpointIndex = candidateEndpointIndex;
                    bestSelectorIndex = choice.SelectorIndex;
                    if (bestError == 0)
                        break;
                }

                endpointIndices[i] = bestEndpointIndex;
                selectorIndices[i] = bestSelectorIndex;
            }
        }

        private ushort[] FindNearestAlphaEndpointCandidates(ushort targetEndpoint, ushort currentEndpointIndex)
        {
            int maxCandidates = Math.Min(
                _alphaEndpoints.Count <= ExhaustiveAlphaEndpointRetargetLimit
                    ? _alphaEndpoints.Count
                    : AlphaEndpointRetargetCandidates,
                _alphaEndpoints.Count);
            ushort[] bestIndices = new ushort[maxCandidates];
            int[] bestDistances = new int[maxCandidates];
            Array.Fill(bestDistances, int.MaxValue);
            int count = 0;

            AddCandidate(currentEndpointIndex, AlphaEndpointDistance(targetEndpoint, _alphaEndpoints[currentEndpointIndex]));
            for (int i = 0; i < _alphaEndpoints.Count; i++)
                AddCandidate((ushort)i, AlphaEndpointDistance(targetEndpoint, _alphaEndpoints[i]));

            if (count == bestIndices.Length)
                return bestIndices;

            ushort[] result = new ushort[count];
            Array.Copy(bestIndices, result, count);
            return result;

            void AddCandidate(ushort index, int distance)
            {
                for (int i = 0; i < count; i++)
                {
                    if (bestIndices[i] == index)
                        return;
                }

                int insert = count;
                for (int i = 0; i < count; i++)
                {
                    if (distance < bestDistances[i])
                    {
                        insert = i;
                        break;
                    }
                }

                if (insert >= maxCandidates)
                    return;

                int moveCount = Math.Min(count, maxCandidates - 1) - insert;
                if (moveCount > 0)
                {
                    Array.Copy(bestIndices, insert, bestIndices, insert + 1, moveCount);
                    Array.Copy(bestDistances, insert, bestDistances, insert + 1, moveCount);
                }

                bestIndices[insert] = index;
                bestDistances[insert] = distance;
                if (count < maxCandidates)
                    count++;
            }
        }

        private void RetargetColorSelectors(
            ushort[][] oldColorEndpointIndices,
            PaletteRefit<uint> colorEndpointRefit)
        {
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetColorSelectors(
                    oldColorEndpointIndices[i],
                    level.ColorEndpointIndices,
                    level.ColorSelectorIndices,
                    colorEndpointRefit);
            }
        }

        private void RetargetColorSelectors(
            ushort[] oldEndpointIndices,
            ushort[] newEndpointIndices,
            ushort[] selectorIndices,
            PaletteRefit<uint> colorEndpointRefit)
        {
            if (oldEndpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            ColorSelectorWeights weights = GetColorSelectorWeights();
            for (int i = 0; i < selectorIndices.Length; i++)
            {
                uint oldEndpoint = colorEndpointRefit.OldValues[oldEndpointIndices[i]];
                uint newEndpoint = _colorEndpoints[newEndpointIndices[i]];
                if (oldEndpoint == newEndpoint)
                    continue;

                uint oldSelector = _colorSelectors[selectorIndices[i]];
                uint newSelector = RetargetColorSelector(oldEndpoint, oldSelector, newEndpoint, weights);
                selectorIndices[i] = GetColorSelectorIndex(newSelector);
            }
        }

        private static uint RetargetColorSelector(uint oldEndpoint, uint oldSelector, uint newEndpoint, ColorSelectorWeights weights)
        {
            Span<Rgba32> oldTable = stackalloc Rgba32[4];
            Span<Rgba32> newTable = stackalloc Rgba32[4];
            BuildColorEndpointTable(oldEndpoint, oldTable);
            BuildColorEndpointTable(newEndpoint, newTable);

            uint result = 0;
            for (int i = 0; i < 16; i++)
            {
                Rgba32 value = oldTable[(int)((oldSelector >> (i * 2)) & 3)];
                int bestSelector = 0;
                int bestError = int.MaxValue;
                for (int selector = 0; selector < newTable.Length; selector++)
                {
                    int error = ColorDistance(value, newTable[selector], weights);
                    if (error >= bestError)
                        continue;

                    bestError = error;
                    bestSelector = selector;
                    if (bestError == 0)
                        break;
                }

                result |= (uint)bestSelector << (i * 2);
            }

            return result;
        }

        private void RetargetReducedColorSelectors(
            ushort[][] oldColorSelectorIndices,
            PaletteRefit<uint> colorSelectorRefit)
        {
            Dictionary<ColorSelectorRemapKey, ushort> cache = [];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetReducedColorSelectors(
                    oldColorSelectorIndices[i],
                    level.ColorEndpointIndices,
                    level.ColorSelectorIndices,
                    colorSelectorRefit,
                    cache);
            }
        }

        private void RetargetReducedColorSelectors(
            ushort[] oldSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            PaletteRefit<uint> colorSelectorRefit,
            Dictionary<ColorSelectorRemapKey, ushort> cache)
        {
            if (oldSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            ColorSelectorWeights weights = GetColorSelectorWeights();
            for (int i = 0; i < selectorIndices.Length; i++)
            {
                uint endpoint = _colorEndpoints[endpointIndices[i]];
                uint oldSelector = colorSelectorRefit.OldValues[oldSelectorIndices[i]];
                ColorSelectorRemapKey key = new(endpoint, oldSelector);
                if (!cache.TryGetValue(key, out ushort bestIndex))
                {
                    bestIndex = FindNearestColorSelectorForEndpoint(endpoint, oldSelector, selectorIndices[i], weights);
                    cache[key] = bestIndex;
                }

                selectorIndices[i] = bestIndex;
            }
        }

        private ushort FindNearestColorSelectorForEndpoint(uint endpoint, uint targetSelector, ushort initialIndex, ColorSelectorWeights weights)
        {
            int bestError = ColorSelectorValueDistance(endpoint, targetSelector, _colorSelectors[initialIndex], weights);
            ushort bestIndex = initialIndex;

            for (int i = 0; i < _colorSelectors.Count; i++)
            {
                int error = ColorSelectorValueDistance(endpoint, targetSelector, _colorSelectors[i], weights);
                if (error >= bestError)
                    continue;

                bestError = error;
                bestIndex = (ushort)i;
                if (bestError == 0)
                    break;
            }

            return bestIndex;
        }

        private static int ColorSelectorValueDistance(uint endpoint, uint a, uint b, ColorSelectorWeights weights)
        {
            Span<Rgba32> table = stackalloc Rgba32[4];
            BuildColorEndpointTable(endpoint, table);

            int distance = 0;
            for (int i = 0; i < 16; i++)
            {
                Rgba32 colorA = table[(int)((a >> (i * 2)) & 3)];
                Rgba32 colorB = table[(int)((b >> (i * 2)) & 3)];
                distance += ColorDistance(colorA, colorB, weights);
            }

            return distance;
        }

        private void RetargetReducedColorBlocks(
            ushort[][] sourceColorEndpointIndices,
            ushort[][] sourceColorSelectorIndices,
            uint[] sourceColorEndpoints,
            uint[] sourceColorSelectors)
        {
            if (_colorEndpoints.Count == 0 || _colorSelectors.Count == 0 || sourceColorEndpoints.Length == 0 || sourceColorSelectors.Length == 0)
                return;

            Dictionary<ColorSelectorRemapKey, ushort> cache = [];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetReducedColorBlocks(
                    sourceColorEndpointIndices[i],
                    sourceColorSelectorIndices[i],
                    level.ColorEndpointIndices,
                    level.ColorSelectorIndices,
                    sourceColorEndpoints,
                    sourceColorSelectors,
                    cache);
            }
        }

        private void RetargetReducedColorBlocks(
            ushort[] sourceEndpointIndices,
            ushort[] sourceSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            uint[] sourceColorEndpoints,
            uint[] sourceColorSelectors,
            Dictionary<ColorSelectorRemapKey, ushort> cache)
        {
            if (sourceEndpointIndices.Length == 0 || sourceSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            ColorSelectorWeights weights = GetColorSelectorWeights();
            for (int i = 0; i < selectorIndices.Length; i++)
            {
                uint targetEndpoint = sourceColorEndpoints[sourceEndpointIndices[i]];
                uint targetSelector = sourceColorSelectors[sourceSelectorIndices[i]];
                uint endpoint = _colorEndpoints[endpointIndices[i]];
                uint retargetedSelector = RetargetColorSelector(targetEndpoint, targetSelector, endpoint, weights);
                ColorSelectorRemapKey key = new(endpoint, retargetedSelector);
                if (!cache.TryGetValue(key, out ushort bestIndex))
                {
                    bestIndex = FindNearestColorSelectorForEndpoint(endpoint, retargetedSelector, selectorIndices[i], weights);
                    cache[key] = bestIndex;
                }

                selectorIndices[i] = bestIndex;
            }
        }

        private void RefineReducedColorSelectors(
            ushort[][] sourceColorEndpointIndices,
            ushort[][] sourceColorSelectorIndices,
            uint[] sourceColorEndpoints,
            uint[] sourceColorSelectors)
        {
            if (_colorEndpoints.Count == 0 || _colorSelectors.Count == 0 || sourceColorEndpoints.Length == 0 || sourceColorSelectors.Length == 0)
                return;

            long[] errors = new long[_colorSelectors.Count * 16 * 4];
            bool[] used = new bool[_colorSelectors.Count];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                AccumulateColorSelectorErrors(
                    sourceColorEndpointIndices[i],
                    sourceColorSelectorIndices[i],
                    level.ColorEndpointIndices,
                    level.ColorSelectorIndices,
                    sourceColorEndpoints,
                    sourceColorSelectors,
                    errors,
                    used);
            }

            for (int selectorIndex = 0; selectorIndex < _colorSelectors.Count; selectorIndex++)
            {
                if (!used[selectorIndex])
                    continue;

                uint refined = 0;
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int bestSelector = 0;
                    long bestError = long.MaxValue;
                    int baseOffset = ((selectorIndex * 16) + pixel) * 4;
                    for (int selector = 0; selector < 4; selector++)
                    {
                        long error = errors[baseOffset + selector];
                        if (error >= bestError)
                            continue;

                        bestError = error;
                        bestSelector = selector;
                    }

                    refined |= (uint)bestSelector << (pixel * 2);
                }

                _colorSelectors[selectorIndex] = refined;
            }
        }

        private void AccumulateColorSelectorErrors(
            ushort[] sourceEndpointIndices,
            ushort[] sourceSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            uint[] sourceColorEndpoints,
            uint[] sourceColorSelectors,
            long[] errors,
            bool[] used)
        {
            if (sourceEndpointIndices.Length == 0 || sourceSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            ColorSelectorWeights weights = GetColorSelectorWeights();
            Span<Rgba32> targetTable = stackalloc Rgba32[4];
            Span<Rgba32> candidateTable = stackalloc Rgba32[4];
            for (int i = 0; i < selectorIndices.Length; i++)
            {
                ushort selectorIndex = selectorIndices[i];
                used[selectorIndex] = true;
                uint targetEndpoint = sourceColorEndpoints[sourceEndpointIndices[i]];
                uint targetSelector = sourceColorSelectors[sourceSelectorIndices[i]];
                uint candidateEndpoint = _colorEndpoints[endpointIndices[i]];
                BuildColorEndpointTable(targetEndpoint, targetTable);
                BuildColorEndpointTable(candidateEndpoint, candidateTable);

                for (int pixel = 0; pixel < 16; pixel++)
                {
                    Rgba32 targetValue = targetTable[(int)((targetSelector >> (pixel * 2)) & 3)];
                    int baseOffset = ((selectorIndex * 16) + pixel) * 4;
                    for (int selector = 0; selector < 4; selector++)
                        errors[baseOffset + selector] += ColorDistance(targetValue, candidateTable[selector], weights);
                }
            }
        }

        private void RetargetAlphaSelectors(
            ushort[][] oldAlphaEndpointIndices,
            ushort[][] oldAlphaEndpointIndices1,
            PaletteRefit<ushort> alphaEndpointRefit)
        {
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetAlphaSelectors(
                    oldAlphaEndpointIndices[i],
                    level.AlphaEndpointIndices,
                    level.AlphaSelectorIndices,
                    alphaEndpointRefit);
                RetargetAlphaSelectors(
                    oldAlphaEndpointIndices1[i],
                    level.AlphaEndpointIndices1,
                    level.AlphaSelectorIndices1,
                    alphaEndpointRefit);
            }
        }

        private void RetargetAlphaSelectors(
            ushort[] oldEndpointIndices,
            ushort[] newEndpointIndices,
            ushort[] selectorIndices,
            PaletteRefit<ushort> alphaEndpointRefit)
        {
            if (oldEndpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            for (int i = 0; i < selectorIndices.Length; i++)
            {
                ushort oldEndpoint = alphaEndpointRefit.OldValues[oldEndpointIndices[i]];
                ushort newEndpoint = _alphaEndpoints[newEndpointIndices[i]];
                if (oldEndpoint == newEndpoint)
                    continue;

                ulong oldSelector = _alphaSelectors[selectorIndices[i]];
                ulong newSelector = RetargetAlphaSelector(oldEndpoint, oldSelector, newEndpoint);
                selectorIndices[i] = GetAlphaSelectorIndex(newSelector);
            }
        }

        private static ulong RetargetAlphaSelector(ushort oldEndpoint, ulong oldSelector, ushort newEndpoint)
        {
            Span<int> oldTable = stackalloc int[8];
            Span<int> newTable = stackalloc int[8];
            BuildAlphaEndpointTable(oldEndpoint, oldTable);
            BuildAlphaEndpointTable(newEndpoint, newTable);

            ulong result = 0;
            for (int i = 0; i < 16; i++)
            {
                int value = oldTable[(int)((oldSelector >> (i * 3)) & 7)];
                int bestSelector = 0;
                int bestError = int.MaxValue;
                for (int selector = 0; selector < newTable.Length; selector++)
                {
                    int error = Math.Abs(newTable[selector] - value);
                    if (error >= bestError)
                        continue;

                    bestError = error;
                    bestSelector = selector;
                    if (bestError == 0)
                        break;
                }

                result |= (ulong)bestSelector << (i * 3);
            }

            return result;
        }

        private void RetargetReducedAlphaSelectors(
            ushort[][] oldAlphaSelectorIndices,
            ushort[][] oldAlphaSelectorIndices1,
            PaletteRefit<ulong> alphaSelectorRefit)
        {
            Dictionary<AlphaSelectorRemapKey, ushort> cache = [];
            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                RetargetReducedAlphaSelectors(
                    oldAlphaSelectorIndices[i],
                    level.AlphaEndpointIndices,
                    level.AlphaSelectorIndices,
                    alphaSelectorRefit,
                    cache);
                RetargetReducedAlphaSelectors(
                    oldAlphaSelectorIndices1[i],
                    level.AlphaEndpointIndices1,
                    level.AlphaSelectorIndices1,
                    alphaSelectorRefit,
                    cache);
            }
        }

        private void RetargetReducedAlphaSelectors(
            ushort[] oldSelectorIndices,
            ushort[] endpointIndices,
            ushort[] selectorIndices,
            PaletteRefit<ulong> alphaSelectorRefit,
            Dictionary<AlphaSelectorRemapKey, ushort> cache)
        {
            if (oldSelectorIndices.Length == 0 || endpointIndices.Length == 0 || selectorIndices.Length == 0)
                return;

            for (int i = 0; i < selectorIndices.Length; i++)
            {
                ushort endpoint = _alphaEndpoints[endpointIndices[i]];
                ulong oldSelector = alphaSelectorRefit.OldValues[oldSelectorIndices[i]];
                AlphaSelectorRemapKey key = new(endpoint, oldSelector);
                if (!cache.TryGetValue(key, out ushort bestIndex))
                {
                    bestIndex = FindNearestAlphaSelectorForEndpoint(endpoint, oldSelector, selectorIndices[i]);
                    cache[key] = bestIndex;
                }

                selectorIndices[i] = bestIndex;
            }
        }

        private ushort FindNearestAlphaSelectorForEndpoint(ushort endpoint, ulong targetSelector, ushort initialIndex)
        {
            int bestError = AlphaSelectorValueDistance(endpoint, targetSelector, _alphaSelectors[initialIndex]);
            ushort bestIndex = initialIndex;

            for (int i = 0; i < _alphaSelectors.Count; i++)
            {
                int error = AlphaSelectorValueDistance(endpoint, targetSelector, _alphaSelectors[i]);
                if (error >= bestError)
                    continue;

                bestError = error;
                bestIndex = (ushort)i;
                if (bestError == 0)
                    break;
            }

            return bestIndex;
        }

        private static int AlphaSelectorValueDistance(ushort endpoint, ulong a, ulong b)
        {
            Span<int> table = stackalloc int[8];
            BuildAlphaEndpointTable(endpoint, table);

            int distance = 0;
            for (int i = 0; i < 16; i++)
            {
                int selectorA = (int)((a >> (i * 3)) & 7);
                int selectorB = (int)((b >> (i * 3)) & 7);
                distance += Squared(table[selectorA] - table[selectorB]);
            }

            return distance;
        }

        private static int AlphaBlockDistance(ushort targetEndpoint, ulong targetSelector, ushort candidateEndpoint, ulong candidateSelector)
        {
            Span<int> targetTable = stackalloc int[8];
            Span<int> candidateTable = stackalloc int[8];
            BuildAlphaEndpointTable(targetEndpoint, targetTable);
            BuildAlphaEndpointTable(candidateEndpoint, candidateTable);

            int distance = 0;
            for (int i = 0; i < 16; i++)
            {
                int targetValue = targetTable[(int)((targetSelector >> (i * 3)) & 7)];
                int candidateValue = candidateTable[(int)((candidateSelector >> (i * 3)) & 7)];
                distance += Squared(targetValue - candidateValue);
            }

            return distance;
        }

        private static PaletteRefit<T> RefitPalette<T>(
            List<T> palette,
            int maxEntries,
            Func<T, T, int> distance,
            IEnumerable<ushort[]> indexArrays,
            bool preserveRange)
            where T : notnull
        {
            if (maxEntries <= 0 || palette.Count <= maxEntries)
                return new PaletteRefit<T>(Array.Empty<T>(), Array.Empty<int>(), false);

            ushort[][] arrays = indexArrays.Where(x => x.Length > 0).ToArray();
            if (arrays.Length == 0)
                return new PaletteRefit<T>(Array.Empty<T>(), Array.Empty<int>(), false);

            T[] oldValues = palette.ToArray();
            long[] frequencies = new long[palette.Count];
            foreach (ushort[] indices in arrays)
            {
                foreach (ushort index in indices)
                    frequencies[index]++;
            }

            int targetCount = Math.Min(maxEntries, palette.Count);
            bool[] selected = new bool[palette.Count];
            int seedIndex = FindMostFrequentIndex(frequencies);
            selected[seedIndex] = true;
            int selectedCount = 1;

            if (preserveRange)
            {
                SelectWeightedRangePalette(palette, distance, frequencies, selected, ref selectedCount, targetCount, seedIndex);
                RefineWeightedPalette(palette, distance, frequencies, selected);
            }
            else
            {
                SelectFrequentPalette(frequencies, selected, ref selectedCount, targetCount);
            }

            int[] oldToNew = new int[palette.Count];
            List<T> compact = new(targetCount);
            for (int i = 0; i < palette.Count; i++)
            {
                if (!selected[i])
                    continue;

                oldToNew[i] = compact.Count;
                compact.Add(oldValues[i]);
            }

            for (int i = 0; i < palette.Count; i++)
            {
                if (selected[i])
                    continue;

                oldToNew[i] = FindNearestPaletteIndex(palette[i], compact, distance);
            }

            palette.Clear();
            palette.AddRange(compact);

            foreach (ushort[] indices in arrays)
                RemapIndices(indices, oldToNew);

            return new PaletteRefit<T>(oldValues, oldToNew, true);
        }

        private static void SelectFrequentPalette(long[] frequencies, bool[] selected, ref int selectedCount, int targetCount)
        {
            while (selectedCount < targetCount)
            {
                int bestIndex = -1;
                long bestFrequency = -1;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (selected[i] || frequencies[i] < bestFrequency)
                        continue;

                    bestFrequency = frequencies[i];
                    bestIndex = i;
                }

                if (bestIndex < 0 || bestFrequency <= 0)
                    break;

                selected[bestIndex] = true;
                selectedCount++;
            }
        }

        private static int FindMostFrequentIndex(long[] frequencies)
        {
            int bestIndex = 0;
            long bestFrequency = frequencies[0];
            for (int i = 1; i < frequencies.Length; i++)
            {
                if (frequencies[i] <= bestFrequency)
                    continue;

                bestFrequency = frequencies[i];
                bestIndex = i;
            }

            return bestIndex;
        }

        private static void SelectWeightedRangePalette<T>(
            List<T> palette,
            Func<T, T, int> distance,
            long[] frequencies,
            bool[] selected,
            ref int selectedCount,
            int targetCount,
            int seedIndex)
        {
            int[] nearestDistances = new int[palette.Count];
            for (int i = 0; i < nearestDistances.Length; i++)
                nearestDistances[i] = selected[i] ? 0 : distance(palette[i], palette[seedIndex]);

            while (selectedCount < targetCount)
            {
                int bestIndex = -1;
                long bestScore = -1;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (selected[i] || frequencies[i] == 0)
                        continue;

                    long score = frequencies[i] * nearestDistances[i];
                    if (score < bestScore)
                        continue;

                    bestScore = score;
                    bestIndex = i;
                }

                if (bestIndex < 0 || bestScore <= 0)
                    break;

                selected[bestIndex] = true;
                selectedCount++;

                for (int i = 0; i < nearestDistances.Length; i++)
                {
                    if (selected[i])
                        continue;

                    int candidateDistance = distance(palette[i], palette[bestIndex]);
                    if (candidateDistance < nearestDistances[i])
                        nearestDistances[i] = candidateDistance;
                }
            }
        }

        private static void RefineWeightedPalette<T>(
            List<T> palette,
            Func<T, T, int> distance,
            long[] frequencies,
            bool[] selected)
        {
            int[] selectedIndices = Enumerable
                .Range(0, selected.Length)
                .Where(x => selected[x])
                .ToArray();
            if (selectedIndices.Length <= 1)
                return;

            int[] assignments = new int[palette.Count];
            for (int iteration = 0; iteration < 2; iteration++)
            {
                AssignPaletteEntries();
                bool changed = false;
                for (int cluster = 0; cluster < selectedIndices.Length; cluster++)
                {
                    int current = selectedIndices[cluster];
                    int best = current;
                    long bestError = ClusterError(cluster, current);
                    for (int candidate = 0; candidate < palette.Count; candidate++)
                    {
                        if (frequencies[candidate] == 0 || assignments[candidate] != cluster)
                            continue;

                        long error = ClusterError(cluster, candidate);
                        if (error >= bestError)
                            continue;

                        bestError = error;
                        best = candidate;
                    }

                    if (best == current)
                        continue;

                    selectedIndices[cluster] = best;
                    changed = true;
                }

                if (!changed)
                    break;
            }

            Array.Clear(selected);
            foreach (int index in selectedIndices)
                selected[index] = true;

            void AssignPaletteEntries()
            {
                for (int i = 0; i < palette.Count; i++)
                {
                    int bestCluster = 0;
                    int bestDistance = int.MaxValue;
                    for (int cluster = 0; cluster < selectedIndices.Length; cluster++)
                    {
                        int candidateDistance = distance(palette[i], palette[selectedIndices[cluster]]);
                        if (candidateDistance >= bestDistance)
                            continue;

                        bestDistance = candidateDistance;
                        bestCluster = cluster;
                        if (bestDistance == 0)
                            break;
                    }

                    assignments[i] = bestCluster;
                }
            }

            long ClusterError(int cluster, int candidate)
            {
                long error = 0;
                T candidateValue = palette[candidate];
                for (int i = 0; i < palette.Count; i++)
                {
                    if (frequencies[i] == 0 || assignments[i] != cluster)
                        continue;

                    error += frequencies[i] * distance(candidateValue, palette[i]);
                }

                return error;
            }
        }

        private PaletteSnapshot CapturePaletteState()
        {
            return new PaletteSnapshot(
                _colorEndpoints.ToArray(),
                _colorSelectors.ToArray(),
                _alphaEndpoints.ToArray(),
                _alphaSelectors.ToArray(),
                _encodedLevels
                    .Select(level => new EncodedLevelIndexSnapshot(
                        level.ColorEndpointIndices.ToArray(),
                        level.ColorSelectorIndices.ToArray(),
                        level.AlphaEndpointIndices.ToArray(),
                        level.AlphaSelectorIndices.ToArray(),
                        level.AlphaEndpointIndices1.ToArray(),
                        level.AlphaSelectorIndices1.ToArray()))
                    .ToArray());
        }

        private void RestorePaletteState(PaletteSnapshot snapshot)
        {
            RestorePalette(_colorEndpoints, snapshot.ColorEndpoints);
            RestorePalette(_colorSelectors, snapshot.ColorSelectors);
            RestorePalette(_alphaEndpoints, snapshot.AlphaEndpoints);
            RestorePalette(_alphaSelectors, snapshot.AlphaSelectors);

            for (int i = 0; i < _encodedLevels.Count; i++)
            {
                EncodedLevel level = _encodedLevels[i];
                EncodedLevelIndexSnapshot indices = snapshot.Levels[i];
                indices.ColorEndpointIndices.CopyTo(level.ColorEndpointIndices, 0);
                indices.ColorSelectorIndices.CopyTo(level.ColorSelectorIndices, 0);
                indices.AlphaEndpointIndices.CopyTo(level.AlphaEndpointIndices, 0);
                indices.AlphaSelectorIndices.CopyTo(level.AlphaSelectorIndices, 0);
                indices.AlphaEndpointIndices1.CopyTo(level.AlphaEndpointIndices1, 0);
                indices.AlphaSelectorIndices1.CopyTo(level.AlphaSelectorIndices1, 0);
            }
        }

        private static void RestorePalette<T>(List<T> palette, T[] values)
        {
            palette.Clear();
            palette.AddRange(values);
        }

        private static PaletteLimits CreatePaletteLimits(CrnFormat format, CrnCompressionQuality quality, int faces)
        {
            PaletteLimits best = faces == CrnHeader.MaxFaces
                ? CreateCubemapPaletteLimits(format)
                : CreateTexturePaletteLimits(format);

            return quality switch
            {
                CrnCompressionQuality.Fast => best.Scale(0.5f),
                CrnCompressionQuality.Balanced => best.Scale(0.75f),
                _ => best,
            };
        }

        private static PaletteLimits CreateTexturePaletteLimits(CrnFormat format)
        {
            return format switch
            {
                CrnFormat.Dxt1 => new PaletteLimits(384, 512, 0, 0),
                CrnFormat.Dxt5 => new PaletteLimits(384, 512, 256, 256),
                CrnFormat.Dxt5CcxY => new PaletteLimits(72, 112, 320, 384),
                CrnFormat.Dxt5XGxR => new PaletteLimits(96, 160, 176, 192),
                CrnFormat.Dxt5XGbr => new PaletteLimits(384, 384, 128, 16),
                CrnFormat.Dxt5Agbr => new PaletteLimits(512, 384, 128, 16),
                CrnFormat.DxnXy or CrnFormat.DxnYx => new PaletteLimits(0, 0, 192, 256),
                CrnFormat.Dxt5A => new PaletteLimits(0, 0, CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries),
                _ => new PaletteLimits(CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries),
            };
        }

        private static PaletteLimits CreateCubemapPaletteLimits(CrnFormat format)
        {
            return format switch
            {
                CrnFormat.Dxt1 => new PaletteLimits(160, 192, 0, 0),
                CrnFormat.Dxt5 => new PaletteLimits(160, 192, 128, 128),
                CrnFormat.Dxt5CcxY => new PaletteLimits(57, 98, 132, 148),
                CrnFormat.Dxt5XGxR => new PaletteLimits(57, 114, 74, 98),
                CrnFormat.Dxt5XGbr => new PaletteLimits(224, 224, 128, 128),
                CrnFormat.Dxt5Agbr => new PaletteLimits(256, 256, 128, 128),
                CrnFormat.DxnXy or CrnFormat.DxnYx => new PaletteLimits(0, 0, 80, 112),
                CrnFormat.Dxt5A => new PaletteLimits(0, 0, 88, 112),
                _ => new PaletteLimits(CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries, CrnHeader.MaxPaletteEntries),
            };
        }

        private bool ReorderColorEndpointsByFrequency()
        {
            if (_colorEndpoints.Count <= 1)
                return false;

            int[] oldToNew = ReorderPaletteByFrequency(_colorEndpoints, _encodedLevels.Select(x => x.ColorEndpointIndices));
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorEndpointIndices, oldToNew);

            return true;
        }

        private bool ReorderColorSelectorsByFrequency()
        {
            if (_colorSelectors.Count <= 1)
                return false;

            int[] oldToNew = ReorderPaletteByFrequency(_colorSelectors, _encodedLevels.Select(x => x.ColorSelectorIndices));
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorSelectorIndices, oldToNew);

            return true;
        }

        private bool ReorderAlphaEndpointsByFrequency()
        {
            if (_alphaEndpoints.Count <= 1)
                return false;

            int[] oldToNew = ReorderPaletteByFrequency(
                _alphaEndpoints,
                _encodedLevels.SelectMany(x => new[] { x.AlphaEndpointIndices, x.AlphaEndpointIndices1 }));
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaEndpointIndices, oldToNew);
                RemapIndices(level.AlphaEndpointIndices1, oldToNew);
            }

            return true;
        }

        private bool ReorderAlphaSelectorsByFrequency()
        {
            if (_alphaSelectors.Count <= 1)
                return false;

            int[] oldToNew = ReorderPaletteByFrequency(
                _alphaSelectors,
                _encodedLevels.SelectMany(x => new[] { x.AlphaSelectorIndices, x.AlphaSelectorIndices1 }));
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaSelectorIndices, oldToNew);
                RemapIndices(level.AlphaSelectorIndices1, oldToNew);
            }

            return true;
        }

        private bool ReorderColorEndpoints()
        {
            if (_colorEndpoints.Count <= 1)
                return false;

            int[] oldToNew = ReorderPalette(_colorEndpoints, ColorEndpointDistance);
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorEndpointIndices, oldToNew);

            return true;
        }

        private bool ReorderColorSelectors()
        {
            if (_colorSelectors.Count <= 1)
                return false;

            int[] oldToNew = ReorderPalette(_colorSelectors, ColorSelectorDistance);
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorSelectorIndices, oldToNew);

            return true;
        }

        private bool ReorderAlphaEndpoints()
        {
            if (_alphaEndpoints.Count <= 1)
                return false;

            int[] oldToNew = ReorderPalette(_alphaEndpoints, AlphaEndpointDistance);
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaEndpointIndices, oldToNew);
                RemapIndices(level.AlphaEndpointIndices1, oldToNew);
            }

            return true;
        }

        private bool ReorderAlphaSelectors()
        {
            if (_alphaSelectors.Count <= 1)
                return false;

            int[] oldToNew = ReorderPalette(_alphaSelectors, AlphaSelectorDistance);
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaSelectorIndices, oldToNew);
                RemapIndices(level.AlphaSelectorIndices1, oldToNew);
            }

            return true;
        }

        private bool ReorderColorEndpointsByZeng(float similarityWeight)
        {
            if (_colorEndpoints.Count <= 1 || _colorEndpoints.Count > MaxZengPaletteEntries)
                return false;

            int[] oldToNew = ReorderPaletteByZeng(
                _colorEndpoints,
                _encodedLevels.Select(x => x.ColorEndpointIndices),
                ColorEndpointSimilarity,
                similarityWeight);
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorEndpointIndices, oldToNew);

            return true;
        }

        private bool ReorderColorSelectorsByZeng(float similarityWeight)
        {
            if (_colorSelectors.Count <= 1 || _colorSelectors.Count > MaxZengPaletteEntries)
                return false;

            int[] oldToNew = ReorderPaletteByZeng(
                _colorSelectors,
                _encodedLevels.Select(x => x.ColorSelectorIndices),
                ColorSelectorSimilarity,
                similarityWeight);
            foreach (EncodedLevel level in _encodedLevels)
                RemapIndices(level.ColorSelectorIndices, oldToNew);

            return true;
        }

        private bool ReorderAlphaEndpointsByZeng(float similarityWeight)
        {
            if (_alphaEndpoints.Count <= 1 || _alphaEndpoints.Count > MaxZengPaletteEntries)
                return false;

            int[] oldToNew = ReorderPaletteByZeng(
                _alphaEndpoints,
                _encodedLevels.SelectMany(x => new[] { x.AlphaEndpointIndices, x.AlphaEndpointIndices1 }),
                AlphaEndpointSimilarity,
                similarityWeight);
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaEndpointIndices, oldToNew);
                RemapIndices(level.AlphaEndpointIndices1, oldToNew);
            }

            return true;
        }

        private bool ReorderAlphaSelectorsByZeng(float similarityWeight)
        {
            if (_alphaSelectors.Count <= 1 || _alphaSelectors.Count > MaxZengPaletteEntries)
                return false;

            int[] oldToNew = ReorderPaletteByZeng(
                _alphaSelectors,
                _encodedLevels.SelectMany(x => new[] { x.AlphaSelectorIndices, x.AlphaSelectorIndices1 }),
                AlphaSelectorSimilarity,
                similarityWeight);
            foreach (EncodedLevel level in _encodedLevels)
            {
                RemapIndices(level.AlphaSelectorIndices, oldToNew);
                RemapIndices(level.AlphaSelectorIndices1, oldToNew);
            }

            return true;
        }

        private static int[] ReorderPaletteByFrequency<T>(List<T> palette, IEnumerable<ushort[]> indexArrays)
        {
            long[] frequencies = new long[palette.Count];
            foreach (ushort[] indices in indexArrays)
            {
                foreach (ushort index in indices)
                    frequencies[index]++;
            }

            int[] order = Enumerable
                .Range(0, palette.Count)
                .OrderByDescending(x => frequencies[x])
                .ThenBy(x => x)
                .ToArray();
            int[] oldToNew = InvertOrder(order);
            ApplyPaletteOrder(palette, order);
            return oldToNew;
        }

        private static int[] ReorderPaletteByZeng<T>(
            List<T> palette,
            IEnumerable<ushort[]> indexArrays,
            Func<T, T, float> similarity,
            float similarityWeight)
        {
            ushort[][] arrays = indexArrays.Where(x => x.Length > 0).ToArray();
            if (arrays.Length == 0)
                return ReorderPalette(palette, (_, _) => 0);

            int count = palette.Count;
            Dictionary<long, int> histogram = [];
            int maxFrequency = 0;
            long maxKey = 0;

            foreach (ushort[] indices in arrays)
            {
                for (int i = 1; i < indices.Length; i++)
                    AddTransition(indices[i - 1], indices[i], 2);
            }

            if (maxFrequency == 0)
                return ReorderPaletteByFrequency(palette, arrays);

            int first = (int)(maxKey >> 32);
            int second = (int)maxKey;
            List<int> chosen = [first, second];
            List<int> remaining = new(count - 2);
            for (int i = 0; i < count; i++)
            {
                if (i != first && i != second)
                    remaining.Add(i);
            }

            int[] totalFrequencyToChosen = new int[count];
            foreach (int value in remaining)
                totalFrequencyToChosen[value] = ReadTotalFrequencyToChosen(value);

            while (remaining.Count > 0)
            {
                double bestFrequency = -1.0;
                int bestRemainingIndex = 0;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int value = remaining[i];
                    double frequency = totalFrequencyToChosen[value];
                    if (similarityWeight > 0.0f)
                    {
                        float weight = MathF.Max(
                            similarity(palette[value], palette[chosen[0]]),
                            similarity(palette[value], palette[chosen[^1]]));
                        frequency = (frequency + 1.0) * Lerp(1.0f - similarityWeight, 1.0f + similarityWeight, weight);
                    }

                    if (frequency > bestFrequency)
                    {
                        bestFrequency = frequency;
                        bestRemainingIndex = i;
                    }
                }

                int selected = remaining[bestRemainingIndex];
                float side = 0.0f;
                int leftFrequency = 0;
                int rightFrequency = 0;
                for (int i = 0; i < chosen.Count; i++)
                {
                    int frequency = ReadTransition(selected, chosen[i]);
                    int scale = chosen.Count + 1 - (2 * (i + 1));
                    side += scale * frequency;
                    if (scale < 0)
                        rightFrequency += -scale * frequency;
                    else
                        leftFrequency += scale * frequency;
                }

                if (similarityWeight > 0.0f)
                {
                    float weightLeft = Lerp(
                        1.0f - similarityWeight,
                        1.0f + similarityWeight,
                        similarity(palette[selected], palette[chosen[0]]));
                    float weightRight = Lerp(
                        1.0f - similarityWeight,
                        1.0f + similarityWeight,
                        similarity(palette[selected], palette[chosen[^1]]));
                    side = (weightLeft * leftFrequency) - (weightRight * rightFrequency);
                }

                if (side > 0.0f)
                    chosen.Insert(0, selected);
                else
                    chosen.Add(selected);

                remaining.RemoveAt(bestRemainingIndex);
                foreach (int value in remaining)
                    totalFrequencyToChosen[value] += ReadTransition(value, selected);
            }

            int[] order = chosen.ToArray();
            int[] oldToNew = InvertOrder(order);
            ApplyPaletteOrder(palette, order);
            return oldToNew;

            void AddTransition(int a, int b, int amount)
            {
                if (a == b)
                    return;

                long key = CreateTransitionKey(a, b);
                histogram.TryGetValue(key, out int frequency);
                frequency += amount;
                histogram[key] = frequency;
                if (frequency > maxFrequency)
                {
                    maxFrequency = frequency;
                    maxKey = key;
                }
            }

            int ReadTotalFrequencyToChosen(int value)
            {
                int total = 0;
                foreach (int selected in chosen)
                    total += ReadTransition(value, selected);

                return total;
            }

            int ReadTransition(int a, int b)
            {
                if (a == b)
                    return 0;

                histogram.TryGetValue(CreateTransitionKey(a, b), out int frequency);
                return frequency;
            }
        }

        private static long CreateTransitionKey(int a, int b)
        {
            if (a > b)
                (a, b) = (b, a);

            return ((long)a << 32) | (uint)b;
        }

        private static int[] InvertOrder(int[] order)
        {
            int[] oldToNew = new int[order.Length];
            for (int i = 0; i < order.Length; i++)
                oldToNew[order[i]] = i;

            return oldToNew;
        }

        private static void ApplyPaletteOrder<T>(List<T> palette, int[] order)
        {
            T[] original = palette.ToArray();
            for (int i = 0; i < order.Length; i++)
                palette[i] = original[order[i]];
        }

        private static int[] ReorderPalette<T>(List<T> palette, Func<T, T, int> distance)
        {
            T[] original = palette.ToArray();
            int[] oldToNew = new int[original.Length];
            int[] order = new int[original.Length];
            bool[] chosen = new bool[original.Length];
            int current = 0;

            for (int newIndex = 0; newIndex < original.Length; newIndex++)
            {
                order[newIndex] = current;
                oldToNew[current] = newIndex;
                chosen[current] = true;

                if (newIndex + 1 == original.Length)
                    break;

                int bestIndex = -1;
                int bestDistance = int.MaxValue;
                for (int candidate = 0; candidate < original.Length; candidate++)
                {
                    if (chosen[candidate])
                        continue;

                    int candidateDistance = distance(original[current], original[candidate]);
                    if (candidateDistance >= bestDistance)
                        continue;

                    bestDistance = candidateDistance;
                    bestIndex = candidate;
                    if (bestDistance == 0)
                        break;
                }

                current = bestIndex;
            }

            for (int newIndex = 0; newIndex < original.Length; newIndex++)
                palette[newIndex] = original[order[newIndex]];

            return oldToNew;
        }

        private static void RemapIndices(ushort[] indices, int[] oldToNew)
        {
            for (int i = 0; i < indices.Length; i++)
                indices[i] = (ushort)oldToNew[indices[i]];
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

        private static float ColorEndpointSimilarity(uint a, uint b)
        {
            return 1.0f - Math.Clamp(ColorEndpointDistance(a, b) / 8000.0f, 0.0f, 1.0f);
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

        private static float ColorSelectorSimilarity(uint a, uint b)
        {
            return 1.0f - Math.Clamp(ColorSelectorDistance(a, b) / 20.0f, 0.0f, 1.0f);
        }

        private ColorSelectorWeights GetColorSelectorWeights()
        {
            return _format switch
            {
                CrnFormat.Dxt5CcxY => new ColorSelectorWeights(1, 1, 0),
                CrnFormat.Dxt5XGxR => new ColorSelectorWeights(0, 2, 0),
                CrnFormat.Dxt5XGbr => new ColorSelectorWeights(0, 1, 1),
                _ => new ColorSelectorWeights(1, 1, 1),
            };
        }

        private static int ColorDistance(Rgba32 a, Rgba32 b, ColorSelectorWeights weights)
        {
            return (weights.R * Squared(a.R - b.R)) +
                (weights.G * Squared(a.G - b.G)) +
                (weights.B * Squared(a.B - b.B));
        }

        private static void BuildColorEndpointTable(uint endpoint, Span<Rgba32> table)
        {
            Rgba32 color0 = FromRgb565((ushort)endpoint);
            Rgba32 color1 = FromRgb565((ushort)(endpoint >> 16));
            table[0] = color0;
            table[1] = color1;
            table[2] = InterpolateColor(color0, color1, 2, 1);
            table[3] = InterpolateColor(color0, color1, 1, 2);
        }

        private static Rgba32 FromRgb565(ushort value)
        {
            byte r = (byte)Expand5((value >> 11) & 31);
            byte g = (byte)Expand6((value >> 5) & 63);
            byte b = (byte)Expand5(value & 31);
            return new Rgba32(r, g, b, 255);
        }

        private static Rgba32 InterpolateColor(Rgba32 c0, Rgba32 c1, int weight0, int weight1)
        {
            int total = weight0 + weight1;
            byte r = (byte)(((c0.R * weight0) + (c1.R * weight1)) / total);
            byte g = (byte)(((c0.G * weight0) + (c1.G * weight1)) / total);
            byte b = (byte)(((c0.B * weight0) + (c1.B * weight1)) / total);
            return new Rgba32(r, g, b, 255);
        }

        private static int AlphaEndpointDistance(ushort a, ushort b)
        {
            Span<int> tableA = stackalloc int[8];
            Span<int> tableB = stackalloc int[8];
            BuildAlphaEndpointTable(a, tableA);
            BuildAlphaEndpointTable(b, tableB);

            int distance = 0;
            for (int i = 0; i < tableA.Length; i++)
                distance += Squared(tableA[i] - tableB[i]);

            return distance;
        }

        private static float AlphaEndpointSimilarity(ushort a, ushort b)
        {
            return 1.0f - Math.Clamp(AlphaEndpointPairDistance(a, b) / 256.0f, 0.0f, 1.0f);
        }

        private static int AlphaEndpointPairDistance(ushort a, ushort b)
        {
            int a0 = a & 0xFF;
            int a1 = a >> 8;
            int b0 = b & 0xFF;
            int b1 = b >> 8;
            return Squared(a0 - b0) + Squared(a1 - b1);
        }

        private static void BuildAlphaEndpointTable(ushort endpoint, Span<int> table)
        {
            int alpha0 = endpoint & 0xFF;
            int alpha1 = endpoint >> 8;
            table[0] = alpha0;
            table[1] = alpha1;
            if (alpha0 > alpha1)
            {
                table[2] = ((6 * alpha0) + alpha1) / 7;
                table[3] = ((5 * alpha0) + (2 * alpha1)) / 7;
                table[4] = ((4 * alpha0) + (3 * alpha1)) / 7;
                table[5] = ((3 * alpha0) + (4 * alpha1)) / 7;
                table[6] = ((2 * alpha0) + (5 * alpha1)) / 7;
                table[7] = (alpha0 + (6 * alpha1)) / 7;
            }
            else
            {
                table[2] = ((4 * alpha0) + alpha1) / 5;
                table[3] = ((3 * alpha0) + (2 * alpha1)) / 5;
                table[4] = ((2 * alpha0) + (3 * alpha1)) / 5;
                table[5] = (alpha0 + (4 * alpha1)) / 5;
                table[6] = 0;
                table[7] = 255;
            }
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

        private static float AlphaSelectorSimilarity(ulong a, ulong b)
        {
            return 1.0f - Math.Clamp(AlphaSelectorDistance(a, b) / 100.0f, 0.0f, 1.0f);
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

        private static float Lerp(float a, float b, float amount)
        {
            return a + ((b - a) * amount);
        }

        private static byte ClampToByte(int value)
        {
            return (byte)Math.Clamp(value, 0, 255);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static EndpointState ReadEndpointState(EncodedLevel level, int index, bool hasColor, bool hasAlpha, bool dualAlpha)
        {
            return new EndpointState(
                hasColor ? level.ColorEndpointIndices[index] : 0,
                hasAlpha ? level.AlphaEndpointIndices[index] : 0,
                dualAlpha ? level.AlphaEndpointIndices1[index] : 0);
        }

        private static int SelectEndpointReference(EndpointState endpoint, EndpointState currentEndpoint, EndpointState topEndpoint, int y)
        {
            if (endpoint == currentEndpoint)
                return 1;

            return y > 0 && endpoint == topEndpoint ? 2 : 0;
        }

        private static void SetReferenceGroup(int[] referenceGroups, int face, int chunksX, int chunksY, int y, int x, int endpointReference)
        {
            int groupIndex = (face * chunksY * chunksX) + ((y >> 1) * chunksX) + (x >> 1);
            int shift = ((x & 1) * 4) + ((y & 1) * 2);
            referenceGroups[groupIndex] |= (endpointReference & 3) << shift;
        }

        private static int GetEndpointReference(IReadOnlyList<int> referenceGroups, int face, int chunksX, int chunksY, int y, int x)
        {
            int groupIndex = (face * chunksY * chunksX) + ((y >> 1) * chunksX) + (x >> 1);
            int shift = ((x & 1) * 4) + ((y & 1) * 2);
            return (referenceGroups[groupIndex] >> shift) & 3;
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
        ushort[] AlphaSelectorIndices,
        ushort[] AlphaEndpointIndices1,
        ushort[] AlphaSelectorIndices1);

    private sealed record PaletteSnapshot(
        uint[] ColorEndpoints,
        uint[] ColorSelectors,
        ushort[] AlphaEndpoints,
        ulong[] AlphaSelectors,
        EncodedLevelIndexSnapshot[] Levels);

    private sealed record EncodedLevelIndexSnapshot(
        ushort[] ColorEndpointIndices,
        ushort[] ColorSelectorIndices,
        ushort[] AlphaEndpointIndices,
        ushort[] AlphaSelectorIndices,
        ushort[] AlphaEndpointIndices1,
        ushort[] AlphaSelectorIndices1);

    private sealed record PaletteRefit<T>(T[] OldValues, int[] OldToNew, bool Changed);

    private readonly record struct AlphaSelectorRemapKey(ushort Endpoint, ulong Selector);

    private readonly record struct AlphaEndpointCandidateKey(ushort TargetEndpoint, ushort CurrentEndpointIndex);

    private readonly record struct AlphaBlockRemapKey(ushort TargetEndpoint, ulong TargetSelector, ushort CandidateEndpoint);

    private readonly record struct AlphaBlockChoice(ushort SelectorIndex, int Error);

    private readonly record struct AlphaEndpointTrainingBlock(ushort TargetEndpoint, ulong TargetSelector, ulong Selector);

    private readonly record struct ColorSelectorRemapKey(uint Endpoint, uint Selector);

    private readonly record struct ColorSelectorWeights(int R, int G, int B);

    private readonly record struct EndpointState(int Color, int Alpha0, int Alpha1);

    private readonly record struct PaletteLimits(int ColorEndpoints, int ColorSelectors, int AlphaEndpoints, int AlphaSelectors)
    {
        public PaletteLimits Scale(float factor)
        {
            return new PaletteLimits(
                ScaleLimit(ColorEndpoints, factor),
                ScaleLimit(ColorSelectors, factor),
                ScaleLimit(AlphaEndpoints, factor),
                ScaleLimit(AlphaSelectors, factor));
        }

        private static int ScaleLimit(int value, float factor)
        {
            if (value <= 0)
                return 0;

            return Math.Clamp((int)MathF.Round(value * factor), 1, CrnHeader.MaxPaletteEntries);
        }
    }

    private sealed class LevelSymbolStream
    {
        public List<int> ReferenceGroups { get; } = [];
        public List<int> ColorEndpointDeltas { get; } = [];
        public List<int> ColorSelectorIndices { get; } = [];
        public List<int> AlphaEndpointDeltas { get; } = [];
        public List<int> AlphaSelectorIndices { get; } = [];
        public List<int> AlphaEndpointDeltas1 { get; } = [];
        public List<int> AlphaSelectorIndices1 { get; } = [];
    }
}
