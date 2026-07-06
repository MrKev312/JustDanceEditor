using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.Formats.JDI.Services;

using KevInc.Audio.Cafe.NAudio;
using KevInc.Audio.NAudio;
using KevInc.Audio.Nx.NAudio;
using KevInc.Audio.Xma2.NAudio;
using KevInc.Texture.ImageSharp;
using KevInc.Texture.Nintendo.ImageSharp;
using KevInc.Texture.PlayStation;
using KevInc.Texture.PlayStation.ImageSharp;
using KevInc.Texture.Xbox;
using KevInc.Texture.Xbox.ImageSharp;
using KevInc.UbiArt.Ipk;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Cli;

internal sealed class DroppedPathProcessor(ILogger<DroppedPathProcessor> logger, IAudioConverter audioConverter)
{
    private static readonly Lock ConsoleLock = new();
    private static readonly Lock TextureRegistrationLock = new();
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
        new("ckd-dds-dxt1", "UbiArt TEX-wrapped PC DDS DXT1 (.ckd)", ".ckd", ["pc-dxt1", "pc-dds-dxt1", "ubiart-pc-dxt1"]),
        new("ckd-dds-dxt5", "UbiArt TEX-wrapped PC DDS DXT5 (.ckd)", ".ckd", ["pc-dxt5", "pc-dds-dxt5", "ubiart-pc-dxt5"]),
        new("ckd-dds-rgba8", "UbiArt TEX-wrapped PC DDS RGBA8 (.ckd)", ".ckd", ["pc-rgba8", "pc-dds-rgba8", "ubiart-pc-rgba8"]),
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
        new("ps3-dxt1", "PlayStation 3 texture DXT1 (.ps3tex)", ".ps3tex", ["playstation3-dxt1"]),
        new("ps3-dxt5", "PlayStation 3 texture DXT5 (.ps3tex)", ".ps3tex", ["playstation3-dxt5", "ps3"]),
        new("ps3-rgba8", "PlayStation 3 texture A8R8G8B8 (.ps3tex)", ".ps3tex", ["playstation3-rgba8"]),
        new("ckd-ps3-dxt1", "UbiArt TEX-wrapped PlayStation 3 texture DXT1 (.ps3tex.ckd)", ".ps3tex.ckd", ["ps3-dxt1-ckd"]),
        new("ckd-ps3-dxt5", "UbiArt TEX-wrapped PlayStation 3 texture DXT5 (.ps3tex.ckd)", ".ps3tex.ckd", ["ps3-dxt5-ckd", "playstation3-ckd", "ubiart-ps3"]),
        new("ckd-ps3-rgba8", "UbiArt TEX-wrapped PlayStation 3 texture A8R8G8B8 (.ps3tex.ckd)", ".ps3tex.ckd", ["ps3-rgba8-ckd"]),
    ];

    private static readonly string[] CompoundSourceExtensions =
    [
        ".wav.ckd",
        ".png.ckd",
        ".tga.ckd",
        ".dds.ckd",
        ".xtx.ckd",
        ".gtx.ckd",
        ".x360tex.ckd",
        ".ps3tex.ckd",
        ".tex.ckd",
        ".ssd.ckd",
    ];

    private static bool textureFormatsRegistered;
    private readonly ILogger<DroppedPathProcessor> _logger = logger;
    private readonly IAudioConverter _audioConverter = audioConverter;

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
        UbiArtIpkWriter writer = new(inputPath, resolvedOutput);
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
        UbiArtIpkParser parser = new(inputPath, resolvedOutput);
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
        string? inputRoot = Directory.Exists(inputPath) ? Path.GetFullPath(inputPath) : null;
        return ProcessMediaFiles(inputs, input => ConvertAudioFile(input, ResolveMediaOutputPath(input, inputRoot, outputPath, target.Extension, batch), force, target)) == 0;
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
        string? inputRoot = Directory.Exists(inputPath) ? Path.GetFullPath(inputPath) : null;
        return ProcessMediaFiles(inputs, input => ConvertTextureFile(input, ResolveMediaOutputPath(input, inputRoot, outputPath, target.Extension, batch), force, target)) == 0;
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
        string resolvedOutput = outputPath ?? ChangeMediaExtension(inputPath, target.Extension);
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
        string resolvedOutput = outputPath ?? ChangeMediaExtension(inputPath, target.Extension);
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
            case "ps3-dxt1":
                using (Image<Rgba32> ps3Dxt1 = image.CloneAs<Rgba32>())
                    PlayStation3ImageSharpTextureCodec.Encode(ps3Dxt1, PlayStation3TextureFormat.DXT1, outputStream);
                break;
            case "ps3-dxt5":
                using (Image<Rgba32> ps3Dxt5 = image.CloneAs<Rgba32>())
                    PlayStation3ImageSharpTextureCodec.Encode(ps3Dxt5, PlayStation3TextureFormat.DXT5, outputStream);
                break;
            case "ps3-rgba8":
                using (Image<Rgba32> ps3Rgba = image.CloneAs<Rgba32>())
                    PlayStation3ImageSharpTextureCodec.Encode(ps3Rgba, PlayStation3TextureFormat.A8R8G8B8, outputStream);
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
                using (Image<Bgra32> gtxBc3 = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodeWiiUGtx(gtxBc3, GTX.GX2SurfaceFormat.T_BC3_UNORM, outputStream);
                break;
            case "dds-dxt1":
                using (Image<Bgra32> pcDxt1 = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodePcDds(pcDxt1, DDS.DDSFormat.DXT1, outputStream, false);
                break;
            case "dds-dxt5":
                using (Image<Bgra32> pcDxt5 = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodePcDds(pcDxt5, DDS.DDSFormat.DXT5, outputStream, false);
                break;
            case "dds-rgba8":
                using (Image<Bgra32> pcRgba8 = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodePcDds(pcRgba8, DDS.DDSFormat.RGBA8, outputStream, false);
                break;
            case "gtx-rgba8":
                using (Image<Bgra32> gtxRgba = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodeWiiUGtx(gtxRgba, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, outputStream);
                break;
            case "xtx-dxt5":
                using (Image<Bgra32> xtxDxt5 = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodeNxXtx(xtxDxt5, XTX.XTXImageFormat.DXT5, outputStream);
                break;
            case "xtx-rgba8":
                using (Image<Bgra32> xtxRgba = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodeNxXtx(xtxRgba, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, outputStream);
                break;
            case "ssd":
                using (Image<Bgra32> ssd = image.CloneAs<Bgra32>())
                    UbiArtTextureEncoder.EncodeWiiSsd(ssd, outputStream);
                break;
            case "xbox360-dxt1":
                using (Image<Rgba32> xboxDxt1 = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodeXbox360(xboxDxt1, Xbox360TextureFormat.DXT1, outputStream);
                break;
            case "xbox360-dxt5":
                using (Image<Rgba32> xboxDxt5 = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodeXbox360(xboxDxt5, Xbox360TextureFormat.DXT5, outputStream);
                break;
            case "xbox360-rgba8":
                using (Image<Rgba32> xboxRgba = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodeXbox360(xboxRgba, Xbox360TextureFormat.A8R8G8B8, outputStream);
                break;
            case "ps3-dxt1":
                using (Image<Rgba32> ps3Dxt1 = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodePlayStation3(ps3Dxt1, PlayStation3TextureFormat.DXT1, outputStream);
                break;
            case "ps3-dxt5":
                using (Image<Rgba32> ps3Dxt5 = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodePlayStation3(ps3Dxt5, PlayStation3TextureFormat.DXT5, outputStream);
                break;
            case "ps3-rgba8":
                using (Image<Rgba32> ps3Rgba = image.CloneAs<Rgba32>())
                    UbiArtTextureEncoder.EncodePlayStation3(ps3Rgba, PlayStation3TextureFormat.A8R8G8B8, outputStream);
                break;
            default:
                throw new NotSupportedException($"CKD-wrapped texture target encoding '{nativeCode}' is not supported.");
        }
    }

    private IDisposable? OpenWaveStream(string inputPath, out WaveStream waveStream)
    {
        if (HasRakiMagic(inputPath))
        {
            FileStream inputStream = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                waveStream = _audioConverter.ConvertAsync(inputStream, Path.GetFileName(inputPath)).GetAwaiter().GetResult();
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

        if (Path.GetExtension(inputPath).Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(inputPath).Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            waveStream = new WaveFileReader(inputPath);
            return null;
        }

        waveStream = ConvertToWaveStream(inputPath);
        return null;
    }

    private static WaveStream ConvertToWaveStream(string inputPath)
    {
        using MemoryStream wavStream = new DefaultMediaProcessor()
            .EncodeAudioToMemoryAsync(
                new JdiAudioEncodeRequest(inputPath)
                {
                    OutputFormat = "wav",
                    Codec = "pcm_s16le",
                    SampleRate = 48000,
                    Channels = 2,
                    SampleFormat = "s16"
                })
            .GetAwaiter()
            .GetResult();

        MemoryStream ownedStream = new(wavStream.ToArray());
        return new WaveFileReader(ownedStream);
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
            PlayStation3ImageSharpConfiguration.RegisterTextureFormat();
            Xbox360ImageSharpConfiguration.RegisterTextureFormat();
            UbiArtTextureImageSharpConfiguration.RegisterTextureFormat();
            textureFormatsRegistered = true;
        }
    }

    private static string[] ExpandInputFiles(string inputPath, Func<string, bool> predicate)
    {
        if (File.Exists(inputPath))
            return predicate(inputPath) ? [inputPath] : [];

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

        string inputFileName = Path.GetFileName(inputPath);
        string fileName = string.IsNullOrEmpty(extension)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : ChangeMediaExtension(inputFileName, extension);

        return Path.Combine(outputPath, fileName);
    }

    private static string? ResolveMediaOutputPath(string inputPath, string? inputRoot, string? outputPath, string extension, bool batch)
    {
        if (!string.IsNullOrWhiteSpace(inputRoot) && !string.IsNullOrWhiteSpace(outputPath))
        {
            string relativePath = Path.GetRelativePath(inputRoot, Path.GetFullPath(inputPath));
            string outputRelativePath = ChangeMediaExtension(relativePath, extension);
            return Path.Combine(outputPath, outputRelativePath);
        }

        return ResolveBatchOutputPath(inputPath, outputPath, extension, batch);
    }

    private static string ChangeMediaExtension(string path, string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return path;

        foreach (string compoundExtension in CompoundSourceExtensions)
        {
            if (path.EndsWith(compoundExtension, StringComparison.OrdinalIgnoreCase))
                return path[..^compoundExtension.Length] + extension;
        }

        return Path.ChangeExtension(path, extension);
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
               extension.Equals(".x360tex", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps3tex", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tex", StringComparison.OrdinalIgnoreCase) ||
               IsLikelyTextureCkdPath(path);
    }

    private static bool IsLikelyTextureCkdPath(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".png.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tga.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".dds.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".xtx.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".gtx.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".x360tex.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".ps3tex.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tex.ckd", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".ssd.ckd", StringComparison.OrdinalIgnoreCase);
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
        return (read >= 4 && header[..4].SequenceEqual("RAKI"u8)) ||
               (read >= 8 && header.Slice(4, 4).SequenceEqual("RAKI"u8));
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