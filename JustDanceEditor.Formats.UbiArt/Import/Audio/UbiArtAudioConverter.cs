using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Import.Audio;

public sealed record UbiArtAudioClipSource(SoundSetClip Clip, CookedFile File);

public sealed record UbiArtAudioConversionRequest(
    JDUbiArtSong SongData,
    CookedFile MainSongFile,
    IReadOnlyList<UbiArtAudioClipSource> AudioClips,
    string TempAudioFolder,
    string MasterOutputFolder,
    string PreviewOutputFolder,
    bool IsMainSongPreMerged,
    IAudioConverter AudioConverter,
    JustDanceUbiArtFileSystem FileSystem);

/// <summary>
/// Converts UbiArt audio files to Opus format entirely in memory.
/// Uses Concentus for Opus encoding instead of FFmpeg.
/// </summary>
public static class UbiArtAudioConverter
{
    private const int MasterOpusBitrate = 256000;

    public static Task ConvertAudioAsync(UbiArtAudioConversionRequest request, ILogger logger, IFileSystem? io = null) =>
        Task.Run(() => ConvertAudio(request, logger, io));

    public static void ConvertAudio(UbiArtAudioConversionRequest request, ILogger logger, IFileSystem? io = null)
    {
        IFileSystem iofs = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SongData);
        ArgumentNullException.ThrowIfNull(request.MainSongFile);
        ArgumentNullException.ThrowIfNull(request.AudioClips);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MasterOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewOutputFolder);
        ArgumentNullException.ThrowIfNull(request.AudioConverter);

        logger.LogInformation("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (request.IsMainSongPreMerged && TryEncodePreMergedMainSongWithFfmpeg(request, logger, iofs))
        {
            logger.LogInformation("Finished converting audio files in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return;
        }

        // Convert main song to WaveStream
        WaveStream mainSongStream = ConvertMainSong(request, logger);

        logger.LogInformation("Finished converting audio files in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        ISampleProvider mergedAudio;

        if (request.IsMainSongPreMerged)
        {
            // Pre-merged media audio is already aligned to the downloaded video.
            mergedAudio = mainSongStream.ToSampleProvider();
        }
        else
        {
            // Convert audio clips and merge
            ConcurrentDictionary<string, WaveStream> clipStreams = ConvertAudioClips(request, logger);
            mergedAudio = MergeAudioStreams(request, mainSongStream, clipStreams, logger);
        }

        // Encode to Opus and write to output
        EncodeAndWriteOpus(request, mergedAudio, logger, iofs);
    }

    private static ConcurrentDictionary<string, WaveStream> ConvertAudioClips(UbiArtAudioConversionRequest request, ILogger logger)
    {
        ConcurrentDictionary<string, WaveStream> clipStreams = new();

        // Parallelize audio clip conversions
        Parallel.ForEach(request.AudioClips, clipSource =>
        {
            try
            {
                WaveStream waveStream = ConvertSourceFile(request, clipSource.File, logger);
                string clipKey = Path.GetFileNameWithoutExtension(clipSource.Clip.SoundSetPath);
                clipStreams[clipKey] = waveStream;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to convert audio clip {ClipName}", clipSource.File.Name);
            }
        });

        return clipStreams;
    }

    private static WaveStream ConvertMainSong(UbiArtAudioConversionRequest request, ILogger logger)
    {
        WaveStream waveStream = ConvertSourceFile(request, request.MainSongFile, logger);
        logger.LogDebug("Converted main song to WaveStream");
        return waveStream;
    }

    private static WaveStream ConvertSourceFile(UbiArtAudioConversionRequest request, CookedFile sourceFile, ILogger logger)
    {
        using Stream src = request.FileSystem.GetFileStream(sourceFile);
        string sourceFileName = sourceFile.Name + sourceFile.Extension;
        if (sourceFile.IsCooked)
            sourceFileName += ".ckd";

        if (IsPlainAudioFile(sourceFile, src))
            return ConvertPlainAudioFile(src, sourceFile.Extension, logger);

        return request.AudioConverter.ConvertAsync(src, sourceFileName).GetAwaiter().GetResult();
    }

    private static bool TryEncodePreMergedMainSongWithFfmpeg(UbiArtAudioConversionRequest request, ILogger logger, IFileSystem io)
    {
        CookedFile sourceFile = request.MainSongFile;
        if (sourceFile.IsCooked || !IsPlainAudioExtension(sourceFile.Extension))
            return false;

        logger.LogInformation("Encoding pre-merged audio to Opus with FFmpeg...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        io.CreateDirectory(request.MasterOutputFolder);
        string outputPath = io.Combine(request.MasterOutputFolder, "master.opus");

        using Stream source = request.FileSystem.GetFileStream(sourceFile);
        using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        RunFfmpegPipeAsync(
                source,
                output,
                BuildFfmpegOpusEncodeArguments(offsetSeconds: 0))
            .GetAwaiter()
            .GetResult();

        stopwatch.Stop();
        logger.LogInformation("Finished encoding pre-merged audio to Opus in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        return true;
    }

    private static bool IsPlainAudioFile(CookedFile sourceFile, Stream source)
    {
        if (sourceFile.IsCooked)
            return false;

        string extension = sourceFile.Extension;
        if (!extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".opus", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!source.CanSeek)
            return true;

        long originalPosition = source.Position;
        try
        {
            Span<byte> header = stackalloc byte[4];
            int read = source.Read(header);
            if (read < 4)
                return false;

            return header.SequenceEqual("OggS"u8) || header.SequenceEqual("RIFF"u8);
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    private static WaveStream ConvertPlainAudioFile(Stream source, string extension, ILogger logger)
    {
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            MemoryStream wavCopy = new();
            source.CopyTo(wavCopy);
            wavCopy.Position = 0;
            return new WaveFileReader(wavCopy);
        }

        MemoryStream oggCopy = new();
        source.CopyTo(oggCopy);
        oggCopy.Position = 0;

        if (IsOggOpusStream(oggCopy))
        {
            oggCopy.Position = 0;
            return new OpusWaveStream(oggCopy, ownsStream: true);
        }

        logger.LogDebug("Decoding plain Ogg audio with FFmpeg because it is not Ogg Opus.");
        oggCopy.Position = 0;
        return DecodePlainAudioWithFfmpegAsync(oggCopy).GetAwaiter().GetResult();
    }

    private static bool IsPlainAudioExtension(string extension) =>
        extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".opus", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOggOpusStream(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
            return false;

        long originalPosition = source.Position;
        try
        {
            Span<byte> header = stackalloc byte[512];
            int read = source.Read(header);
            if (read < 4 || !header[..4].SequenceEqual("OggS"u8))
                return false;

            return IndexOf(header[..read], "OpusHead"u8) >= 0;
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    private static async Task<WaveStream> DecodePlainAudioWithFfmpegAsync(Stream source)
    {
        MemoryStream wavStream = new();
        await RunFfmpegPipeAsync(source, wavStream, BuildFfmpegWavDecodeArguments()).ConfigureAwait(false);
        wavStream.Position = 0;
        return new WaveFileReader(wavStream);
    }

    private static async Task RunFfmpegPipeAsync(Stream input, Stream output, IEnumerable<string> arguments)
    {
        string ffmpegPath = await JdiFfmpegResolver.GetFfmpegPathAsync().ConfigureAwait(false);
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (string arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        if (!process.Start())
            throw new InvalidOperationException("Failed to start FFmpeg for audio conversion.");

        Exception? inputException = null;
        Task writeInputTask = Task.Run(async () =>
        {
            try
            {
                await input.CopyToAsync(process.StandardInput.BaseStream).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                inputException = ex;
            }
            finally
            {
                await process.StandardInput.BaseStream.DisposeAsync().ConfigureAwait(false);
            }
        });
        Task readOutputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        Task<string> readErrorTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(writeInputTask, readOutputTask, readErrorTask, process.WaitForExitAsync()).ConfigureAwait(false);

        string stderr = await readErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg audio conversion exited with code {process.ExitCode}: {stderr}");
        if (inputException != null)
            throw new InvalidOperationException("Failed to pipe audio into FFmpeg.", inputException);
    }

    private static IEnumerable<string> BuildFfmpegWavDecodeArguments()
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-i";
        yield return "pipe:0";
        yield return "-f";
        yield return "wav";
        yield return "-codec:a";
        yield return "pcm_s16le";
        yield return "-ar";
        yield return "48000";
        yield return "-ac";
        yield return "2";
        yield return "-sample_fmt";
        yield return "s16";
        yield return "pipe:1";
    }

    private static IEnumerable<string> BuildFfmpegOpusEncodeArguments(float offsetSeconds)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-i";
        yield return "pipe:0";

        string? filter = BuildAudioOffsetFilter(offsetSeconds);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            yield return "-af";
            yield return filter;
        }

        yield return "-f";
        yield return "opus";
        yield return "-codec:a";
        yield return "libopus";
        yield return "-b:a";
        yield return MasterOpusBitrate.ToString(CultureInfo.InvariantCulture);
        yield return "-ar";
        yield return "48000";
        yield return "-ac";
        yield return "2";
        yield return "pipe:1";
    }

    private static string? BuildAudioOffsetFilter(float offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) <= 0.000001f)
            return null;

        if (offsetSeconds > 0)
        {
            int delayMs = (int)Math.Round(offsetSeconds * 1000, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"adelay={delayMs}:all=1");
        }

        return string.Create(CultureInfo.InvariantCulture, $"atrim=start={-offsetSeconds},asetpts=PTS-STARTPTS");
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
            return 0;

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    private static ISampleProvider MergeAudioStreams(
        UbiArtAudioConversionRequest request,
        WaveStream mainSongStream,
        ConcurrentDictionary<string, WaveStream> clipStreams,
        ILogger logger)
    {
        logger.LogInformation("Merging audio streams...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<ISampleProvider> sampleProviders = [];

        // Add main song with offset
        float mainSongOffset = request.SongData.GetAudioStartOffset();
        ISampleProvider mainSongProvider = mainSongStream.ToSampleProvider();
        WaveFormat mixFormat = mainSongProvider.WaveFormat;
        sampleProviders.Add(ApplyOffset(mainSongProvider, mainSongOffset));

        // Add each audio clip with its calculated offset
        foreach (UbiArtAudioClipSource clipSource in request.AudioClips)
        {
            string clipKey = Path.GetFileNameWithoutExtension(clipSource.Clip.SoundSetPath);
            if (!clipStreams.TryGetValue(clipKey, out WaveStream? clipStream))
                continue;

            ISampleProvider clipProvider = NormalizeForMixing(clipStream.ToSampleProvider(), mixFormat, logger, clipKey);
            float offset = CalculateClipOffset(request, clipSource.Clip);
            sampleProviders.Add(ApplyOffset(clipProvider, offset));
        }

        if (sampleProviders.Count == 0)
        {
            throw new InvalidOperationException("No audio streams to merge.");
        }

        // Create mixer with the format of the first provider
        MixingSampleProvider mixer = new(sampleProviders[0].WaveFormat);
        foreach (ISampleProvider sampleProvider in sampleProviders)
        {
            mixer.AddMixerInput(sampleProvider);
        }

        stopwatch.Stop();
        logger.LogInformation("Finished merging audio streams in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        return mixer;
    }

    private static ISampleProvider NormalizeForMixing(ISampleProvider provider, WaveFormat targetFormat, ILogger logger, string clipName)
    {
        ISampleProvider normalized = provider;

        if (normalized.WaveFormat.SampleRate != targetFormat.SampleRate)
        {
            logger.LogDebug(
                "Resampling audio clip '{ClipName}' from {SourceRate} Hz to {TargetRate} Hz for mixing",
                clipName,
                normalized.WaveFormat.SampleRate,
                targetFormat.SampleRate);
            normalized = new WdlResamplingSampleProvider(normalized, targetFormat.SampleRate);
        }

        if (normalized.WaveFormat.Channels == targetFormat.Channels)
            return normalized;

        logger.LogDebug(
            "Converting audio clip '{ClipName}' from {SourceChannels} channel(s) to {TargetChannels} channel(s) for mixing",
            clipName,
            normalized.WaveFormat.Channels,
            targetFormat.Channels);

        if (targetFormat.Channels == 1)
            return normalized.ToMono();

        if (targetFormat.Channels == 2)
        {
            if (normalized.WaveFormat.Channels == 1)
                return normalized.ToStereo();

            return normalized.ToMono().ToStereo();
        }

        logger.LogWarning(
            "Audio clip '{ClipName}' has {SourceChannels} channel(s), but target mix requires unsupported {TargetChannels} channel(s); leaving clip unchanged",
            clipName,
            normalized.WaveFormat.Channels,
            targetFormat.Channels);
        return normalized;
    }

    private static ISampleProvider ApplyOffset(ISampleProvider provider, float offsetSeconds)
    {
        OffsetSampleProvider offsetProvider = new(provider);

        if (offsetSeconds >= 0)
            offsetProvider.DelayBy = TimeSpan.FromSeconds(offsetSeconds);
        else
            offsetProvider.SkipOver = TimeSpan.FromSeconds(-offsetSeconds);

        return offsetProvider;
    }

    private static float CalculateClipOffset(UbiArtAudioConversionRequest request, SoundSetClip clip)
    {
        double markerIndex = clip.StartTime / 24f;
        int lowerMarker = (int)Math.Floor(Math.Abs(markerIndex));
        int upperMarker = (int)Math.Ceiling(Math.Abs(markerIndex));
        float offset;

        int[] markers = request.SongData.MusicTrack.Components[0].TrackData.Structure.Markers;

        if (lowerMarker == upperMarker)
        {
            offset = markers[lowerMarker] / 48f / 1000f;
        }
        else
        {
            float lowerTime = markers[lowerMarker] / 48f / 1000f;
            float upperTime = markers[upperMarker] / 48f / 1000f;
            float t = (float)(Math.Abs(markerIndex) - lowerMarker);
            offset = lowerTime + (t * (upperTime - lowerTime));
        }

        if (markerIndex < 0)
            offset *= -1;

        offset += request.SongData.GetAudioStartOffset();
        return offset;
    }

    private static void EncodeAndWriteOpus(UbiArtAudioConversionRequest request, ISampleProvider audioSource, ILogger logger, IFileSystem io)
    {
        logger.LogInformation("Encoding to Opus...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Encode to Opus in memory
        using MemoryStream opusStream = OpusEncoderHelper.EncodeToOpusStream(audioSource, MasterOpusBitrate);

        stopwatch.Stop();
        logger.LogInformation("Finished encoding to Opus in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        // Write to output file
        io.CreateDirectory(request.MasterOutputFolder);
        string outputPath = io.Combine(request.MasterOutputFolder, "master.opus");

        using FileStream outputFile = new(outputPath, FileMode.Create, FileAccess.Write);
        opusStream.CopyTo(outputFile);

        logger.LogDebug("Wrote Opus file to {OutputPath}", outputPath);
    }
}
