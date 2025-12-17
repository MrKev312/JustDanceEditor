using JustDanceEditor.Logging;

using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Events;

namespace JustDanceEditor.Formats.UbiArt.Video;

public sealed record UbiArtVideoConversionRequest(
    JDUbiArtSong SongData,
    string VideoOutputFolder,
    string PreviewOutputFolder,
    string SourceVideoPath);

public sealed record VideoQualityProfile(
    string FileName,
    Size? Resolution,
    string Bitrate,
    string? MaxBitrate = null,
    string? BufferSize = null,
    int? Crf = null
);

public interface IVideoConversionProgress
{
    void Update(ConversionProgressEventArgs args);
    void Finish();
}

public sealed class ConsoleVideoProgress(string name) : IVideoConversionProgress
{
    private (TimeSpan current, TimeSpan finish) previous = (TimeSpan.Zero, TimeSpan.Zero);

    public void Update(ConversionProgressEventArgs args)
    {
        (TimeSpan, TimeSpan) current = (args.Duration, args.TotalLength);
        if (previous != current)
            Console.WriteLine($"{name}: {args.Duration}/{args.TotalLength}");
        previous = current;
    }

    public void Finish()
    {
        if (previous.current != previous.finish)
            Console.WriteLine($"{name}: {previous.finish}/{previous.finish}");
    }
}

// TODO: Rewrite this such that it's a part of the JDI format project. Remove the preview audio/video generation from here.
// And only generate these when exporting to Unity. ALSO put them in the output/scratch/video/(video/previewVideo). Put the
// VideoQualityProfile definitions in there too and have the JDI function take in an array. Put them in the scratch folder
// with like a little manifest such that it's known which qualities are cached and can simply be copied over.
// A match is a match with the same format, resolution, all 3 bitrates.
public static class UbiArtVideoConverter
{
    // --- Configuration Profiles ---
    // Values measured from actual game files
    // Name                           Value
    // ----                           -----
    // Low                            17494215 (480x270)
    // Med                            42448328 (768x432)
    // High                           84430596 (1280x720)
    // Ultra                          199552706 (1920x1080, but vp8 instead)
    // Ultra (vp9 equivalent)         ~119731624 (1920x1080)
    private static readonly VideoQualityProfile[] MasterProfiles =
    [
        new VideoQualityProfile("master.webm", null, "4M", Crf: 4)
        /// TODO: Move over to these if the slowdown is acceptable, maybe do this in the to Unity conversion step instead?
        //// Low (~17.5 MB)
        //new VideoQualityProfile("master_low.webm", new Size(480, 270), "1400k", "1600k", "2500k"),
        //// Med (~42.4 MB)
        //new VideoQualityProfile("master_med.webm", new Size(768, 432), "3500k", "4000k", "6500k"),
        //// High (~84.4 MB)
        //new VideoQualityProfile("master_high.webm", new Size(1280, 720), "7000k", "8000k", "13000k"),
        //// Ultra (~119.7 MB, VP9 equivalent)
        //new VideoQualityProfile("master_ultra.webm", new Size(1920, 1080), "9500k", "11000k", "17000k")
    ];

    // TODO: Should we even generate previews in here? Or just do it in the to Unity step?
    // Values measured from actual game files
    // Name                           Value
    // ----                           -----
    // Low                            2439063
    // Med                            5624559
    // High                           11178234
    // Ultra                          22025667
    private static readonly VideoQualityProfile[] PreviewProfiles =
    [
        /// For now, only Low and Ultra are used, as it's super slow to encode all 4 versions
        /// and you usually only see low and ultra in-game.
        // Low (~2.4 MB)
        new VideoQualityProfile("preview_low.webm", new Size(768, 432), "650k", "750k", "1300k"),
        //// Med (~5.6 MB)
        //new VideoQualityProfile("preview_med.webm", new Size(768, 432), "1500k", "1800k", "3000k"),
        //// High (~11.1 MB)
        //new VideoQualityProfile("preview_high.webm", new Size(768, 432), "3000k", "3500k", "6000k"),
        // Ultra (~22.0 MB)
        new VideoQualityProfile("preview_ultra.webm", new Size(768, 432), "6000k", "7000k", "12000k")
    ];

    public static void ConvertVideo(UbiArtVideoConversionRequest request)
    {
        RunConversionLogicAsync(request).GetAwaiter().GetResult();
    }

    private static async Task RunConversionLogicAsync(UbiArtVideoConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceVideoPath);

        Directory.CreateDirectory(request.VideoOutputFolder);
        Directory.CreateDirectory(request.PreviewOutputFolder);

        // 1. Process Master Versions (Usually just 1, handled sequentially)
        foreach (var profile in MasterProfiles)
        {
            await ProcessMasterVideoAsync(request, profile);
        }

        // 2. Process ALL Preview Versions (Optimized Batch)
        await GenerateAllPreviewsAsync(request, PreviewProfiles);
    }

    private static async Task ProcessMasterVideoAsync(UbiArtVideoConversionRequest request, VideoQualityProfile profile)
    {
        string outputPath = Path.Combine(request.VideoOutputFolder, profile.FileName);
        bool isNativeResolution = profile.Resolution == null;
        bool validSource = !await NeedsConversionAsync(request.SourceVideoPath);

        if (isNativeResolution && validSource)
        {
            Logger.Log($"Copying master video source: {profile.FileName}...");
            File.Copy(request.SourceVideoPath, outputPath, true);
        }
        else
        {
            // Master video is processed individually using standard flow
            IVideoConversionProgress progress = new ConsoleVideoProgress($"Master Video ({profile.FileName})");
            await ConvertSingleVideoFileAsync(request.SourceVideoPath, outputPath, profile, progress, isPreview: false, startTime: 0);
        }
    }

    /// <summary>
    /// Optimized method that generates all 4 preview versions in a single FFmpeg pass using Filter Complex splitting.
    /// This decodes the source only once, significantly reducing IO and CPU overhead for filtering.
    /// </summary>
    private static async Task GenerateAllPreviewsAsync(UbiArtVideoConversionRequest request, VideoQualityProfile[] profiles)
    {
        Logger.Log($"Generating {profiles.Length} preview versions (Batch Mode)...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        IVideoConversionProgress progress = new ConsoleVideoProgress("Generating Previews (Batch)");

        try
        {
            // 1. Get Source Info for Cropping Logic
            IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(request.SourceVideoPath);
            IVideoStream inputStream = mediaInfo.VideoStreams.First();
            float previewStart = request.SongData.GetPreviewStartTime(false);

            // 2. Build the Filter Complex
            // Chain: [0:v] -> Crop(Optional) -> Scale -> FadeIn -> FadeOut -> Split -> [v0][v1][v2][v3]
            List<string> commonFilters = [];

            // Crop
            float currentRatio = inputStream.Width / (float)inputStream.Height;
            float targetRatio = 16f / 9f;
            if (Math.Abs(currentRatio - targetRatio) > 0.001f)
            {
                commonFilters.Add(currentRatio < targetRatio ? "crop=in_w:in_w*9/16" : "crop=in_h*16/9:in_h");
            }

            // Scale (Assume all previews use the same resolution for this optimization, or use the first profile's resolution)
            var targetRes = profiles[0].Resolution ?? new Size(768, 432);
            commonFilters.Add($"scale={targetRes.Width}:{targetRes.Height}");

            // Fades (Relative to cut)
            commonFilters.Add("fade=t=in:st=0:d=1");
            commonFilters.Add("fade=t=out:st=29:d=1");

            // Split filter to duplicate the stream for each profile
            commonFilters.Add($"split={profiles.Length}{string.Join("", Enumerable.Range(0, profiles.Length).Select(i => $"[v{i}]"))}");

            string filterComplex = string.Join(",", commonFilters);

            // 3. Construct the Command
            StringBuilder args = new();

            // Input seeking (-ss before -i) for correct timestamps
            args.Append(CultureInfo.InvariantCulture, $"-ss {previewStart} -i \"{request.SourceVideoPath}\" ");

            // Filter Complex
            args.Append($"-filter_complex \"{filterComplex}\" ");

            // Output Mappings
            for (int i = 0; i < profiles.Length; i++)
            {
                var p = profiles[i];
                string outPath = Path.Combine(request.PreviewOutputFolder, p.FileName);

                // Map specific split stream [vi] to this output
                args.Append($"-map \"[v{i}]\" ");
                args.Append("-c:v libvpx-vp9 "); // Force VP9
                args.Append(CultureInfo.InvariantCulture, $"-b:v {p.Bitrate} ");
                args.Append("-r 25 ");

                if (!string.IsNullOrEmpty(p.MaxBitrate))
                    args.Append($"-maxrate {p.MaxBitrate} ");
                if (!string.IsNullOrEmpty(p.BufferSize))
                    args.Append($"-bufsize {p.BufferSize} ");
                if (p.Crf.HasValue)
                    args.Append($"-crf {p.Crf} ");

                args.Append("-t 30 "); // Duration
                args.Append("-y ");    // Overwrite
                args.Append($"\"{outPath}\" ");
            }

            // 4. Run Custom Conversion
            IConversion conversion = FFmpeg.Conversions.New();
            AttachProgress(conversion, progress, TimeSpan.FromSeconds(30)); // Progress might be jittery in batch mode, but it works

            Logger.Log("Starting Batch Conversion...", LogLevel.Debug);
            await conversion.Start(args.ToString());

            progress.Finish();
            Logger.Log($"Batch generation finished.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Batch preview generation failed: {ex.Message}", LogLevel.Error);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            Logger.Log($"Finished generating all previews in {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    // --- Single File Conversion (Used for Master Video) ---
    private static async Task ConvertSingleVideoFileAsync(
        string sourcePath,
        string outputPath,
        VideoQualityProfile profile,
        IVideoConversionProgress progress,
        bool isPreview,
        float startTime)
    {
        Logger.Log($"Processing {profile.FileName}...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            // Build standard 1-to-1 conversion
            IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(sourcePath);
            IVideoStream inputStream = mediaInfo.VideoStreams.First();
            IConversion conversion = FFmpeg.Conversions.New();
            inputStream.SetCodec(VideoCodec.vp9);

            conversion.AddStream(inputStream)
                .SetOutputFormat(Format.webm)
                .SetOverwriteOutput(true)
                .SetOutput(outputPath);

            List<string> filters = new();
            // Crop
            float currentRatio = inputStream.Width / (float)inputStream.Height;
            float targetRatio = 16f / 9f;
            if (Math.Abs(currentRatio - targetRatio) > 0.001f)
                filters.Add(currentRatio < targetRatio ? "crop=in_w:in_w*9/16" : "crop=in_h*16/9:in_h");

            // Scale
            if (profile.Resolution.HasValue)
                filters.Add($"scale={profile.Resolution.Value.Width}:{profile.Resolution.Value.Height}");

            if (filters.Count > 0)
                conversion.AddParameter($"-vf \"{string.Join(",", filters)}\"");

            // Bitrate/Quality
            conversion.AddParameter($"-b:v {profile.Bitrate}");
            conversion.AddParameter("-r 25");
            if (profile.Crf.HasValue)
                conversion.AddParameter($"-crf {profile.Crf.Value}");

            AttachProgress(conversion, progress);

            await conversion.Start();
            progress.Finish();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to process {profile.FileName}: {ex.Message}", LogLevel.Error);
            // Fallback for Master only
            if (!isPreview && profile.Resolution == null)
            {
                try
                {
                    File.Copy(sourcePath, outputPath, true);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            stopwatch.Stop();
            Logger.Log($"Finished {profile.FileName} in {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    private static async Task<bool> NeedsConversionAsync(string videoPath)
    {
        try
        {
            IMediaInfo info = await FFmpeg.GetMediaInfo(videoPath);
            IVideoStream stream = info.VideoStreams.First();
            bool codecOk = stream.Codec is "vp8" or "vp9";
            bool ratioOk = Math.Abs((stream.Width / (float)stream.Height) - (16f / 9f)) < 0.01f;
            bool fpsOk = Math.Abs(stream.Framerate - 25) < 0.01f;
            return !(codecOk && ratioOk && fpsOk);
        }
        catch
        {
            return true;
        }
    }

    private static void AttachProgress(IConversion conversion, IVideoConversionProgress progress, TimeSpan? fixedLength = null)
    {
        conversion.OnProgress += (_, args) =>
        {
            ConversionProgressEventArgs eventArgs = fixedLength.HasValue
                ? new ConversionProgressEventArgs(args.Duration, fixedLength.Value, (int)args.ProcessId)
                : args;
            progress.Update(eventArgs);
        };
    }
}