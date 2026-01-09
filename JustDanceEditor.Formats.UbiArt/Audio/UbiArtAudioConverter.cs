using JustDanceEditor.Audio;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using Microsoft.Extensions.Logging;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace JustDanceEditor.Formats.UbiArt.Audio;

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
    LayeredFileSystem FileSystem);

/// <summary>
/// Converts UbiArt audio files to Opus format entirely in memory.
/// Uses Concentus for Opus encoding instead of FFmpeg.
/// </summary>
public static class UbiArtAudioConverter
{
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

        // Convert main song to WaveStream
        WaveStream mainSongStream = ConvertMainSong(request, logger);

        logger.LogInformation("Finished converting audio files in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        ISampleProvider mergedAudio;

        if (request.IsMainSongPreMerged)
        {
            // Main song is already merged, just use it directly
            mergedAudio = ApplyOffset(mainSongStream.ToSampleProvider(), request.SongData.GetSongStartTime());
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
                using Stream src = request.FileSystem.GetFileStream(clipSource.File);
                string sourceFileName = clipSource.File.Name + clipSource.File.Extension;
                if (clipSource.File.IsCooked)
                    sourceFileName += ".ckd";

                WaveStream waveStream = request.AudioConverter.ConvertAsync(src, sourceFileName).GetAwaiter().GetResult();
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
        using Stream src = request.FileSystem.GetFileStream(request.MainSongFile);
        string sourceFileName = request.MainSongFile.Name + request.MainSongFile.Extension;
        if (request.MainSongFile.IsCooked)
            sourceFileName += ".ckd";

        WaveStream waveStream = request.AudioConverter.ConvertAsync(src, sourceFileName).GetAwaiter().GetResult();
        logger.LogDebug("Converted main song to WaveStream");
        return waveStream;
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
        float mainSongOffset = request.SongData.GetSongStartTime();
        sampleProviders.Add(ApplyOffset(mainSongStream.ToSampleProvider(), mainSongOffset));

        // Add each audio clip with its calculated offset
        foreach (UbiArtAudioClipSource clipSource in request.AudioClips)
        {
            string clipKey = Path.GetFileNameWithoutExtension(clipSource.Clip.SoundSetPath);
            if (!clipStreams.TryGetValue(clipKey, out WaveStream? clipStream))
                continue;

            float offset = CalculateClipOffset(request, clipSource.Clip);
            sampleProviders.Add(ApplyOffset(clipStream.ToSampleProvider(), offset));
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

        offset += request.SongData.GetSongStartTime();
        return offset;
    }

    private static void EncodeAndWriteOpus(UbiArtAudioConversionRequest request, ISampleProvider audioSource, ILogger logger, IFileSystem io)
    {
        logger.LogInformation("Encoding to Opus...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Encode to Opus in memory
        using MemoryStream opusStream = OpusEncoderHelper.EncodeToOpusStream(audioSource);

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
