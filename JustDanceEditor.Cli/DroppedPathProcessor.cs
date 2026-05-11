using KevInc.Audio.Cafe.NAudio;
using KevInc.Audio.NAudio;
using KevInc.Audio.Nx.NAudio;
using KevInc.Audio.Xma2.NAudio;
using KevInc.Raki.NAudio;
using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.IPK;

using KevInc.Texture.ImageSharp;
using KevInc.Texture.Nintendo.ImageSharp;
using KevInc.Texture.Xbox;
using KevInc.Texture.Xbox.ImageSharp;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

namespace JustDanceEditor.Cli;

internal sealed class DroppedPathProcessor(ILogger<DroppedPathProcessor> logger)
{
    private static readonly object ConsoleLock = new();
    private static readonly object TextureRegistrationLock = new();
    private static readonly AudioTargetEncoding[] AudioTargetEncodings =
    [
        new("opus", "Ogg Opus (.opus)", ".opus", ["ogg-opus"]),
        new("wav", "PCM Wave (.wav)", ".wav", ["wave", "pcm", "pcm-wav"]),
        new("nx-opus", "Switch Opus container (.lopus)", ".lopus", ["switch-opus", "nx"]),
        new("cafe-adpcm", "Cafe/Wii DSP ADPCM chunks (.adpcm)", ".adpcm", ["wiiu-adpcm", "wii-adpcm"]),
        new("cafe-adpcm-split", "Cafe/Wii split DSP ADPCM chunks (.adpcm)", ".adpcm", ["wiiu-adpcm-split", "wii-adpcm-split"]),
        new("xma2", "Xbox 360 XMA2 packet stream (.xma2)", ".xma2", ["xbox360", "durango"]),
        new("raki-nx-opus", "UbiArt RAKI-wrapped Switch Opus (.wav.ckd)", ".wav.ckd", ["ubiart-nx-opus", "raki-switch-opus"]),
        new("raki-cafe-adpcm", "UbiArt RAKI-wrapped Cafe/Wii DSP ADPCM (.wav.ckd)", ".wav.ckd", ["ubiart-cafe-adpcm", "raki-wiiu-adpcm", "raki-wii-adpcm"]),
        new("raki-cafe-adpcm-split", "UbiArt RAKI-wrapped Cafe/Wii split DSP ADPCM (.wav.ckd)", ".wav.ckd", ["ubiart-cafe-adpcm-split", "raki-wiiu-adpcm-split", "raki-wii-adpcm-split"]),
        new("raki-xma2", "UbiArt RAKI-wrapped Xbox 360 XMA2 (.wav.ckd)", ".wav.ckd", ["ubiart-xma2", "raki-xbox360", "raki-durango"]),
        new("raki-pcm", "UbiArt RAKI-wrapped PCM (.wav.ckd)", ".wav.ckd", ["ubiart-pcm", "raki-pcm", "win-pcm"]),
    ];

    private static readonly TextureTargetEncoding[] TextureTargetEncodings =
    [
        new("png", "PNG image (.png)", ".png"),
        new("jpg", "JPEG image (.jpg)", ".jpg", ["jpeg"]),
        new("webp", "WebP image (.webp)", ".webp"),
        new("dds-dxt1", "DDS DXT1 (.dds)", ".dds", ["dds-bc1"]),
        new("dds-dxt5", "DDS DXT5 (.dds)", ".dds", ["dds-bc3", "dds"]),
        new("dds-rgba8", "DDS RGBA8 (.dds)", ".dds"),
        new("gtx-bc3", "Wii U GTX BC3/DXT5 (.gtx)", ".gtx", ["gtx", "gtx-dxt5"]),
        new("gtx-rgba8", "Wii U GTX RGBA8 (.gtx)", ".gtx"),
        new("ckd-gtx-bc3", "UbiArt TEX-wrapped Wii U GTX BC3/DXT5 (.gtx.ckd)", ".gtx.ckd", ["gtx-ckd", "gtx-dxt5-ckd", "ubiart-gtx"]),
        new("ckd-gtx-rgba8", "UbiArt TEX-wrapped Wii U GTX RGBA8 (.gtx.ckd)", ".gtx.ckd", ["gtx-rgba8-ckd", "ubiart-gtx-rgba8"]),
        new("xtx-dxt5", "Switch XTX DXT5 (.xtx)", ".xtx", ["xtx", "xtx-bc3"]),
        new("xtx-rgba8", "Switch XTX RGBA8 (.xtx)", ".xtx"),
        new("ckd-xtx-dxt5", "UbiArt TEX-wrapped Switch XTX DXT5 (.xtx.ckd)", ".xtx.ckd", ["xtx-ckd", "xtx-bc3-ckd", "ubiart-xtx"]),
        new("ckd-xtx-rgba8", "UbiArt TEX-wrapped Switch XTX RGBA8 (.xtx.ckd)", ".xtx.ckd", ["xtx-rgba8-ckd", "ubiart-xtx-rgba8"]),
        new("ssd", "Wii SSD (.ssd)", ".ssd", ["wii-ssd"]),
        new("ckd-ssd", "UbiArt TEX-wrapped Wii SSD (.ssd.ckd)", ".ssd.ckd", ["ssd-ckd", "wii-ssd-ckd", "ubiart-ssd"]),
        new("xbox360-dxt1", "Xbox 360 texture DXT1 (.x360tex)", ".x360tex", ["x360-dxt1"]),
        new("xbox360-dxt5", "Xbox 360 texture DXT5 (.x360tex)", ".x360tex", ["x360-dxt5", "xbox360"]),
        new("xbox360-rgba8", "Xbox 360 texture A8R8G8B8 (.x360tex)", ".x360tex", ["x360-rgba8"]),
        new("ckd-xbox360-dxt1", "UbiArt TEX-wrapped Xbox 360 texture DXT1 (.x360tex.ckd)", ".x360tex.ckd", ["x360-dxt1-ckd"]),
        new("ckd-xbox360-dxt5", "UbiArt TEX-wrapped Xbox 360 texture DXT5 (.x360tex.ckd)", ".x360tex.ckd", ["x360-dxt5-ckd", "xbox360-ckd", "ubiart-xbox360"]),
        new("ckd-xbox360-rgba8", "UbiArt TEX-wrapped Xbox 360 texture A8R8G8B8 (.x360tex.ckd)", ".x360tex.ckd", ["x360-rgba8-ckd"]),
    ];

    private static bool textureFormatsRegistered;
    private readonly ILogger<DroppedPathProcessor> _logger = logger;

    public int ProcessDroppedPaths(IReadOnlyList<string> paths, DroppedPathOptions options)
    {
        if (paths.Count == 0)
        {
            Console.WriteLine("No files or folders were provided.");
            return 1;
        }

        int failed = 0;
        bool batch = paths.Count > 1;
        List<string> audioPaths = [];
        List<string> texturePaths = [];
        foreach (string path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (!PackIpk(path, ResolveBatchOutputPath(path, options.OutputPath, ".ipk", batch), options.Force))
                        failed++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    Console.WriteLine($"Path not found: {path}");
                    failed++;
                    continue;
                }

                if (Path.GetExtension(path).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ExtractIpk(path, ResolveBatchOutputPath(path, options.OutputPath, string.Empty, batch), options.Force))
                        failed++;
                    continue;
                }

                if (IsAudioInputFile(path))
                {
                    audioPaths.Add(path);
                    continue;
                }

                if (IsTextureInputFile(path))
                {
                    texturePaths.Add(path);
                    continue;
                }

                Console.WriteLine($"Unsupported dropped file: {path}");
                failed++;
            }
            catch (Exception ex)
            {
                failed++;
                WriteProcessingError(path, ex);
            }
        }

        failed += ProcessDroppedAudioFiles(audioPaths, options, batch);
        failed += ProcessDroppedTextureFiles(texturePaths, options, batch);

        if (options.WaitForKey)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(intercept: true);
        }

        return failed == 0 ? 0 : 1;
    }

    public bool PackIpk(string inputPath, string? outputPath, bool force)
    {
        if (!Directory.Exists(inputPath))
            throw new DirectoryNotFoundException($"Folder not found: {inputPath}");

        string resolvedOutput = string.IsNullOrWhiteSpace(outputPath)
            ? GetDefaultIpkOutputPath(inputPath)
            : outputPath;

        if (File.Exists(resolvedOutput) && !force)
        {
            Console.WriteLine($"Output file already exists, skipping: {resolvedOutput}");
            return true;
        }

        Console.WriteLine($"Packing IPK: {inputPath}");
        JustDanceIPKWriter writer = new(inputPath, resolvedOutput);
        writer.Pack();
        Console.WriteLine($"Packed to: {resolvedOutput}");
        return true;
    }

    public bool ExtractIpk(string inputPath, string? outputPath, bool force)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("IPK file not found.", inputPath);

        string resolvedOutput = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(inputPath))
            : outputPath;

        if (Directory.Exists(resolvedOutput) && Directory.EnumerateFileSystemEntries(resolvedOutput).Any() && !force)
        {
            Console.WriteLine($"Output folder already exists and is not empty, skipping: {resolvedOutput}");
            return true;
        }

        Directory.CreateDirectory(resolvedOutput);
        Console.WriteLine($"Extracting IPK: {inputPath}");
        JustDanceIPKParser parser = new(inputPath, resolvedOutput);
        parser.Parse(ShowInfo: true);
        Console.WriteLine($"Extracted to: {resolvedOutput}");
        return true;
    }

    public bool ConvertAudio(string inputPath, string? outputPath, bool force, string? targetEncoding, bool headless)
    {
        AudioTargetEncoding target = ResolveAudioTarget(targetEncoding, headless);
        string[] inputs = ExpandInputFiles(inputPath, IsAudioInputFile);
        if (inputs.Length == 0)
        {
            Console.WriteLine($"No supported audio files found in: {inputPath}");
            return false;
        }

        bool batch = Directory.Exists(inputPath) || inputs.Length > 1;
        return ProcessMediaFiles(inputs, input => ConvertAudioFile(input, ResolveBatchOutputPath(input, outputPath, target.Extension, batch), force, target)) == 0;
    }

    public bool ConvertTexture(string inputPath, string? outputPath, bool force, string? targetEncoding, bool headless)
    {
        TextureTargetEncoding target = ResolveTextureTarget(targetEncoding, headless);
        string[] inputs = ExpandInputFiles(inputPath, IsTextureInputFile);
        if (inputs.Length == 0)
        {
            Console.WriteLine($"No supported texture or image files found in: {inputPath}");
            return false;
        }

        bool batch = Directory.Exists(inputPath) || inputs.Length > 1;
        return ProcessMediaFiles(inputs, input => ConvertTextureFile(input, ResolveBatchOutputPath(input, outputPath, target.Extension, batch), force, target)) == 0;
    }

    private int ProcessDroppedAudioFiles(IReadOnlyList<string> paths, DroppedPathOptions options, bool batch)
    {
        if (paths.Count == 0)
            return 0;

        AudioTargetEncoding target = ResolveAudioTarget(options.AudioEncoding, options.Headless);
        return ProcessMediaFiles(paths, path => ConvertAudioFile(path, ResolveBatchOutputPath(path, options.OutputPath, target.Extension, batch), options.Force, target));
    }

    private int ProcessDroppedTextureFiles(IReadOnlyList<string> paths, DroppedPathOptions options, bool batch)
    {
        if (paths.Count == 0)
            return 0;

        TextureTargetEncoding target = ResolveTextureTarget(options.TextureEncoding, options.Headless);
        return ProcessMediaFiles(paths, path => ConvertTextureFile(path, ResolveBatchOutputPath(path, options.OutputPath, target.Extension, batch), options.Force, target));
    }

    private static int ProcessMediaFiles(IReadOnlyList<string> paths, Func<string, bool> convert)
    {
        if (paths.Count == 0)
            return 0;

        if (paths.Count == 1)
        {
            try
            {
                return convert(paths[0]) ? 0 : 1;
            }
            catch (Exception ex)
            {
                WriteProcessingError(paths[0], ex);
                return 1;
            }
        }

        int failed = 0;
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(paths.Count, Environment.ProcessorCount))
        };

        Parallel.ForEach(paths, parallelOptions, path =>
        {
            try
            {
                if (!convert(path))
                    Interlocked.Increment(ref failed);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                WriteProcessingError(path, ex);
            }
        });

        return failed;
    }

    private static void WriteProcessingError(string path, Exception ex)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error processing {path}: {ex.Message}");
            Console.ResetColor();
        }
    }

    private bool ConvertAudioFile(string inputPath, string? outputPath, bool force, AudioTargetEncoding target)
    {
        string resolvedOutput = outputPath ?? Path.ChangeExtension(inputPath, target.Extension);
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(resolvedOutput), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Input already uses target encoding, skipping: {inputPath}");
            return true;
        }

        if (File.Exists(resolvedOutput) && !force)
        {
            Console.WriteLine($"Output file already exists, skipping: {resolvedOutput}");
            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutput) ?? Environment.CurrentDirectory);
        Console.WriteLine($"Converting audio: {inputPath}");

        using IDisposable? sourceLifetime = OpenWaveStream(inputPath, out WaveStream waveStream);
        using (waveStream)
        {
            WriteAudioTarget(waveStream, resolvedOutput, target);
        }

        Console.WriteLine($"Converted to: {resolvedOutput}");
        return true;
    }

    private bool ConvertTextureFile(string inputPath, string? outputPath, bool force, TextureTargetEncoding target)
    {
        string resolvedOutput = outputPath ?? Path.ChangeExtension(inputPath, target.Extension);
        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(resolvedOutput), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Input already uses target encoding, skipping: {inputPath}");
            return true;
        }

        if (File.Exists(resolvedOutput) && !force)
        {
            Console.WriteLine($"Output file already exists, skipping: {resolvedOutput}");
            return true;
        }

        RegisterTextureFormats();
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutput) ?? Environment.CurrentDirectory);
        Console.WriteLine($"Converting texture/image: {inputPath}");

        using Image image = Image.Load(inputPath);
        WriteTextureTarget(image, resolvedOutput, target);

        Console.WriteLine($"Converted to: {resolvedOutput}");
        return true;
    }

    private static void WriteAudioTarget(WaveStream waveStream, string outputPath, AudioTargetEncoding target)
    {
        if (target.Code.Equals("wav", StringComparison.OrdinalIgnoreCase))
        {
            WaveFileWriter.CreateWaveFile16(outputPath, waveStream.ToSampleProvider());
            return;
        }

        using FileStream outputStream = new(outputPath, FileMode.Create, FileAccess.Write);
        switch (target.Code)
        {
            case "opus":
                OpusEncoderHelper.EncodeToOpus(waveStream.ToSampleProvider(), outputStream);
                break;
            case "nx-opus":
                NintendoSwitchOpusAudioEncoder.Encode(waveStream, outputStream);
                break;
            case "cafe-adpcm":
                CafeDspAdpcmAudioEncoder.Encode(waveStream, outputStream);
                break;
            case "cafe-adpcm-split":
                CafeDspAdpcmAudioEncoder.Encode(waveStream, outputStream, splitChannels: true);
                break;
            case "xma2":
                Xma2AudioEncoder.Encode(waveStream, outputStream);
                break;
            case "raki-nx-opus":
                RakiNintendoSwitchOpusAudioEncoder.Encode(waveStream, outputStream);
                break;
            case "raki-cafe-adpcm":
                RakiCafeDspAdpcmAudioEncoder.Encode(waveStream, outputStream);
                break;
            case "raki-cafe-adpcm-split":
                RakiCafeDspAdpcmAudioEncoder.Encode(waveStream, outputStream, splitChannels: true);
                break;
            case "raki-xma2":
                RakiXma2AudioEncoder.Encode(waveStream, outputStream);
                break;
            case "raki-pcm":
                RakiPcmAudioEncoder.Encode(waveStream, outputStream);
                break;
            default:
                throw new NotSupportedException($"Audio target encoding '{target.Code}' is not supported.");
        }
    }

    private static void WriteTextureTarget(Image image, string outputPath, TextureTargetEncoding target)
    {
        switch (target.Code)
        {
            case "png":
                image.SaveAsPng(outputPath);
                return;
            case "jpg":
                image.SaveAsJpeg(outputPath);
                return;
            case "webp":
                image.SaveAsWebp(outputPath);
                return;
        }

        using FileStream outputStream = new(outputPath, FileMode.Create, FileAccess.Write);
        if (target.Code.StartsWith("ckd-", StringComparison.OrdinalIgnoreCase))
        {
            WriteCkdTextureTarget(image, outputStream, target.Code["ckd-".Length..]);
            return;
        }

        WriteNativeTextureTarget(image, outputStream, target.Code);
    }

    private static void WriteNativeTextureTarget(Image image, Stream outputStream, string code)
    {
        switch (code)
        {
            case "dds-dxt1":
                using (Image<Bgra32> ddsDxt1 = image.CloneAs<Bgra32>())
                    DDS.ConvertToFile(ddsDxt1, DDS.DDSFormat.DXT1, outputStream);
                break;
            case "dds-dxt5":
                using (Image<Bgra32> ddsDxt5 = image.CloneAs<Bgra32>())
                    DDS.ConvertToFile(ddsDxt5, DDS.DDSFormat.DXT5, outputStream);
                break;
            case "dds-rgba8":
                using (Image<Bgra32> ddsRgba = image.CloneAs<Bgra32>())
                    DDS.ConvertToFile(ddsRgba, DDS.DDSFormat.RGBA8, outputStream);
                break;
            case "gtx-bc3":
                using (Image<Bgra32> gtxBc3 = image.CloneAs<Bgra32>())
                    GTX.ConvertToFile(gtxBc3, GTX.GX2SurfaceFormat.T_BC3_UNORM, outputStream);
                break;
            case "gtx-rgba8":
                using (Image<Bgra32> gtxRgba = image.CloneAs<Bgra32>())
                    GTX.ConvertToFile(gtxRgba, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, outputStream);
                break;
            case "xtx-dxt5":
                using (Image<Bgra32> xtxDxt5 = image.CloneAs<Bgra32>())
                    XTX.ConvertToFile(xtxDxt5, XTX.XTXImageFormat.DXT5, outputStream);
                break;
            case "xtx-rgba8":
                using (Image<Bgra32> xtxRgba = image.CloneAs<Bgra32>())
                    XTX.ConvertToFile(xtxRgba, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, outputStream);
                break;
            case "ssd":
                using (Image<Bgra32> ssd = image.CloneAs<Bgra32>())
                    SSD.ConvertToFile(ssd, outputStream);
                break;
            case "xbox360-dxt1":
                using (Image<Rgba32> xboxDxt1 = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxDxt1, Xbox360TextureFormat.DXT1, outputStream);
                break;
            case "xbox360-dxt5":
                using (Image<Rgba32> xboxDxt5 = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxDxt5, Xbox360TextureFormat.DXT5, outputStream);
                break;
            case "xbox360-rgba8":
                using (Image<Rgba32> xboxRgba = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxRgba, Xbox360TextureFormat.A8R8G8B8, outputStream);
                break;
            default:
                throw new NotSupportedException($"Texture target encoding '{code}' is not supported.");
        }
    }

    private static void WriteCkdTextureTarget(Image image, Stream outputStream, string nativeCode)
    {
        switch (nativeCode)
        {
            case "gtx-bc3":
            case "gtx-rgba8":
            {
                using MemoryStream payload = new();
                WriteNativeTextureTarget(image, payload, nativeCode);
                using BinaryWriter writer = new(outputStream, Encoding.ASCII, leaveOpen: true);
                WriteWiiUTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, checked((uint)payload.Length));
                outputStream.Write(payload.ToArray());
                break;
            }
            case "xtx-dxt5":
            case "xtx-rgba8":
            {
                using MemoryStream payload = new();
                WriteNativeTextureTarget(image, payload, nativeCode);
                using BinaryWriter writer = new(outputStream, Encoding.ASCII, leaveOpen: true);
                WriteNxTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height);
                outputStream.Write(payload.ToArray());
                break;
            }
            case "ssd":
            {
                using MemoryStream payload = new();
                WriteNativeTextureTarget(image, payload, nativeCode);
                using BinaryWriter writer = new(outputStream, Encoding.ASCII, leaveOpen: true);
                WriteWiiTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, checked((uint)payload.Length));
                outputStream.Write(payload.ToArray());
                break;
            }
            case "xbox360-dxt1":
                using (Image<Rgba32> xboxDxt1 = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxDxt1, Xbox360TextureFormat.DXT1, outputStream, ckdWrapped: true);
                break;
            case "xbox360-dxt5":
                using (Image<Rgba32> xboxDxt5 = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxDxt5, Xbox360TextureFormat.DXT5, outputStream, ckdWrapped: true);
                break;
            case "xbox360-rgba8":
                using (Image<Rgba32> xboxRgba = image.CloneAs<Rgba32>())
                    Xbox360ImageSharpTextureCodec.Encode(xboxRgba, Xbox360TextureFormat.A8R8G8B8, outputStream, ckdWrapped: true);
                break;
            default:
                throw new NotSupportedException($"CKD-wrapped texture target encoding '{nativeCode}' is not supported.");
        }
    }

    private static void WriteNxTexWrapperHeader(BinaryWriter writer, ushort width, ushort height)
    {
        writer.Write([0, 0, 0, 9]);
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44);
        uint widthInfo = ((uint)width << 8) | 0x0080;
        writer.Write(widthInfo);
        writer.Write(width);
        writer.Write(height);
        writer.Write(0x00012000);
        writer.Write(widthInfo);
        writer.Write(0u);
        writer.Write(0x4E4E0004);
        uint crc = (uint)(((width * height) ^ 0xA3E908) & 0xFFFFFFFF);
        writer.Write(crc);
        writer.Write(0u);
    }

    private static void WriteWiiUTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint textureSize)
    {
        WriteBigEndian32(writer, 9);
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44);
        WriteBigEndian32(writer, width);
        WriteBigEndian32(writer, height);
        WriteBigEndian32(writer, 1);
        WriteBigEndian32(writer, 0x00000009);
        WriteBigEndian32(writer, 0);
        WriteBigEndian32(writer, 0);
        WriteBigEndian32(writer, 0);
        WriteBigEndian32(writer, textureSize);
    }

    private static void WriteWiiTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint textureSize)
    {
        WriteBigEndian32(writer, 9);
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44);
        WriteBigEndian32(writer, 0);
        WriteBigEndian32(writer, width);
        WriteBigEndian32(writer, height);
        WriteBigEndian32(writer, 1);
        WriteBigEndian32(writer, 0x00000009);
        WriteBigEndian32(writer, textureSize);
        WriteBigEndian32(writer, (uint)(width * height / 2));
        WriteBigEndian32(writer, 0);
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static IDisposable? OpenWaveStream(string inputPath, out WaveStream waveStream)
    {
        if (HasRakiMagic(inputPath))
        {
            FileStream inputStream = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                waveStream = new RakiAudioConverter().ConvertAsync(inputStream, Path.GetFileName(inputPath)).GetAwaiter().GetResult();
                return inputStream;
            }
            catch
            {
                inputStream.Dispose();
                throw;
            }
        }

        if (Path.GetExtension(inputPath).Equals(".opus", StringComparison.OrdinalIgnoreCase))
        {
            waveStream = new OpusWaveStream(inputPath);
            return null;
        }

        waveStream = new AudioFileReader(inputPath);
        return null;
    }

    private static AudioTargetEncoding ResolveAudioTarget(string? targetEncoding, bool headless)
    {
        if (!string.IsNullOrWhiteSpace(targetEncoding))
            return AudioTargetEncodings.FirstOrDefault(target => target.Matches(targetEncoding))
                   ?? throw new ArgumentException($"Unknown audio encoding '{targetEncoding}'. Options: {string.Join(", ", AudioTargetEncodings.Select(target => target.Code))}.");

        if (headless)
            throw new ArgumentException($"Missing required option '--encoding'. Options: {string.Join(", ", AudioTargetEncodings.Select(target => target.Code))}.");

        int selection = Question.Ask([.. AudioTargetEncodings.Select(target => target.Label)], 0, "Select target audio encoding:");
        return AudioTargetEncodings[selection];
    }

    private static TextureTargetEncoding ResolveTextureTarget(string? targetEncoding, bool headless)
    {
        if (!string.IsNullOrWhiteSpace(targetEncoding))
            return TextureTargetEncodings.FirstOrDefault(target => target.Matches(targetEncoding))
                   ?? throw new ArgumentException($"Unknown image/texture encoding '{targetEncoding}'. Options: {string.Join(", ", TextureTargetEncodings.Select(target => target.Code))}.");

        if (headless)
            throw new ArgumentException($"Missing required option '--encoding'. Options: {string.Join(", ", TextureTargetEncodings.Select(target => target.Code))}.");

        int selection = Question.Ask([.. TextureTargetEncodings.Select(target => target.Label)], 0, "Select target image/texture encoding:");
        return TextureTargetEncodings[selection];
    }

    private static void RegisterTextureFormats()
    {
        if (textureFormatsRegistered)
            return;

        lock (TextureRegistrationLock)
        {
            if (textureFormatsRegistered)
                return;

            TextureImageSharpConfiguration.RegisterDdsFormat();
            NintendoImageSharpConfiguration.RegisterTextureFormats();
            Xbox360ImageSharpConfiguration.RegisterTextureFormat();
            textureFormatsRegistered = true;
        }
    }

    private static string[] ExpandInputFiles(string inputPath, Func<string, bool> predicate)
    {
        if (File.Exists(inputPath))
            return predicate(inputPath) ? [inputPath] : [inputPath];

        if (!Directory.Exists(inputPath))
            throw new FileNotFoundException("Input path was not found.", inputPath);

        return [.. Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories)
            .Where(predicate)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }

    private static string? ResolveBatchOutputPath(string inputPath, string? outputPath, string extension, bool batch)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return null;

        bool outputIsDirectory = batch ||
                                 Directory.Exists(outputPath) ||
                                 string.IsNullOrEmpty(Path.GetExtension(outputPath)) ||
                                 outputPath.EndsWith(Path.DirectorySeparatorChar) ||
                                 outputPath.EndsWith(Path.AltDirectorySeparatorChar);

        if (!outputIsDirectory)
            return outputPath;

        string fileName = string.IsNullOrEmpty(extension)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : Path.ChangeExtension(Path.GetFileName(inputPath), extension);

        return Path.Combine(outputPath, fileName);
    }

    private static string GetDefaultIpkOutputPath(string folderPath)
    {
        string trimmedPath = Path.TrimEndingDirectorySeparator(folderPath);
        string parentDirectory = Path.GetDirectoryName(trimmedPath)
                                 ?? Path.GetPathRoot(trimmedPath)
                                 ?? Environment.CurrentDirectory;
        string folderName = Path.GetFileName(trimmedPath);
        return Path.Combine(parentDirectory, folderName + ".ipk");
    }

    private static bool IsAudioInputFile(string path)
    {
        return HasRakiMagic(path) || IsLikelyAudioPath(path);
    }

    private static bool IsLikelyAudioPath(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(fileName);
        if (extension.Equals(".ckd", StringComparison.OrdinalIgnoreCase))
            return fileName.EndsWith(".wav.ckd", StringComparison.OrdinalIgnoreCase);

        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wave", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wma", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextureInputFile(string path)
    {
        return IsLikelyTexturePath(path) || IsLikelyImagePath(path);
    }

    private static bool IsLikelyTexturePath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ssd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xtx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gtx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tex", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ckd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyImagePath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRakiMagic(string path)
    {
        if (!File.Exists(path))
            return false;

        Span<byte> header = stackalloc byte[8];
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = stream.Read(header);
        return read >= 4 && header[..4].SequenceEqual("RAKI"u8) ||
               read >= 8 && header.Slice(4, 4).SequenceEqual("RAKI"u8);
    }

    private sealed record AudioTargetEncoding(string Code, string Label, string Extension, IReadOnlyList<string>? AlternativeCodes = null)
    {
        private IReadOnlyList<string> Aliases { get; } = AlternativeCodes ?? [];

        public bool Matches(string value)
        {
            return Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                   Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record TextureTargetEncoding(string Code, string Label, string Extension, IReadOnlyList<string>? AlternativeCodes = null)
    {
        private IReadOnlyList<string> Aliases { get; } = AlternativeCodes ?? [];

        public bool Matches(string value)
        {
            return Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                   Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
    }
}

internal sealed record DroppedPathOptions(
    bool Headless,
    string? OutputPath = null,
    bool Force = false,
    bool WaitForKey = true,
    string? AudioEncoding = null,
    string? TextureEncoding = null);
