using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;

using Microsoft.Extensions.Logging;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Diagnostics;

using Xabe.FFmpeg;

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
    Files.LayeredFileSystem FileSystem);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TempAudioFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MasterOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewOutputFolder);
        ArgumentNullException.ThrowIfNull(request.AudioConverter);

        iofs.CreateDirectory(request.TempAudioFolder);

        logger.LogInformation("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        string mainSongWavPath = ConvertMainSong(request, logger, iofs);

        logger.LogInformation("Finished converting audio files in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        string mergedWavPath = iofs.Combine(request.TempAudioFolder, "merged.wav");

        if (request.IsMainSongPreMerged)
        {
            iofs.Move(mainSongWavPath, mergedWavPath, true);
        }
        else
        {
            ConvertAudioClips(request, logger, iofs);
            MergeAudioFiles(request, mainSongWavPath, mergedWavPath, logger, iofs);
        }

        string opusPath = ConvertToOpus(request, mergedWavPath, logger, iofs);
        MoveOpusToOutput(request, opusPath, iofs);
    }

    private static void ConvertAudioClips(UbiArtAudioConversionRequest request, ILogger logger, IFileSystem io)
    {
        // Parallelize audio clip conversions
        Parallel.ForEach(request.AudioClips, clipSource =>
        {
            string targetPath = io.Combine(request.TempAudioFolder, clipSource.File.Name + clipSource.File.Extension);
            if (io.FileExists(targetPath))
                return;

            using Stream src = request.FileSystem.GetFileStream(clipSource.File);
            string sourceFileName = clipSource.File.Name + clipSource.File.Extension;
            if (clipSource.File.IsCooked)
                sourceFileName += ".ckd";
            request.AudioConverter.Convert(src, sourceFileName, targetPath, request.TempAudioFolder).GetAwaiter().GetResult();
        });
    }

    private static string ConvertMainSong(UbiArtAudioConversionRequest request, ILogger logger, IFileSystem io)
    {
        string targetPath = io.Combine(request.TempAudioFolder, "mainSong.wav");
        using Stream src = request.FileSystem.GetFileStream(request.MainSongFile);
        string sourceFileName = request.MainSongFile.Name + request.MainSongFile.Extension;
        if (request.MainSongFile.IsCooked)
            sourceFileName += ".ckd";
        request.AudioConverter.Convert(src, sourceFileName, targetPath, request.TempAudioFolder).GetAwaiter().GetResult();
        logger.LogDebug("Converted main song to {TargetPath}", targetPath);
        return targetPath;
    }

    private static void MergeAudioFiles(UbiArtAudioConversionRequest request, string mainSongWavPath, string mergedWavPath, ILogger logger, IFileSystem io)
    {
        logger.LogInformation("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<(string path, float offset)> audioFiles = [];
        audioFiles.Add((mainSongWavPath, request.SongData.GetSongStartTime()));

        foreach (UbiArtAudioClipSource clipSource in request.AudioClips)
        {
            string clipName = Path.GetFileNameWithoutExtension(clipSource.Clip.SoundSetPath);
            string wavPath = io.Combine(request.TempAudioFolder, clipName + ".wav");
            if (!io.FileExists(wavPath))
                continue;

            float offset = CalculateClipOffset(request, clipSource.Clip);
            audioFiles.Add((wavPath, offset));
        }

        MergeAudioFilesInternal([.. audioFiles], mergedWavPath, logger, io);

        stopwatch.Stop();
        logger.LogInformation("Finished merging audio files in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    private static float CalculateClipOffset(UbiArtAudioConversionRequest request, SoundSetClip clip)
    {
        double markerIndex = clip.StartTime / 24f;
        int lowerMarker = (int)Math.Floor(Math.Abs(markerIndex));
        int upperMarker = (int)Math.Ceiling(Math.Abs(markerIndex));
        float offset;

        int[] markers = request.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers;

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

    private static string ConvertToOpus(UbiArtAudioConversionRequest request, string mergedWavPath, ILogger logger, IFileSystem io)
    {
        string opusPath = io.Combine(request.TempAudioFolder, "merged.opus");

        IConversion conversion = FFmpeg.Conversions.New();
        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(mergedWavPath).Result;

        IStream stream = mediaInfo.AudioStreams.First()
            .SetCodec(AudioCodec.libopus)
            .SetSampleRate(48000);

        IConversionResult result = conversion.AddStream(stream)
            .AddParameter("-sample_fmt flt")
            .SetOverwriteOutput(true)
            .UseMultiThread(true)
            .SetOutput(opusPath)
            .SetOverwriteOutput(true)
            .Start().Result;

        logger.LogDebug("Converted song audio with \"{Arguments}\"", result.Arguments);
        return opusPath;
    }

    private static void MoveOpusToOutput(UbiArtAudioConversionRequest request, string opusPath, IFileSystem io)
    {
        MoveAudioToOutput(opusPath, request.MasterOutputFolder, "master.opus", io);
    }

    private static void MoveAudioToOutput(string sourcePath, string destinationFolder, string targetFileName, IFileSystem io)
    {
        io.CreateDirectory(destinationFolder);

        string targetPath = io.Combine(destinationFolder, targetFileName);

        io.Move(sourcePath, targetPath, true);
    }

    private static void MergeAudioFilesInternal((string path, float startTime)[] audioFiles, string outputPath, ILogger logger, IFileSystem io)
    {
        if (audioFiles.Length == 0)
            return;

        List<ISampleProvider> sampleProviders = [];
        bool anyExists = false;

        foreach ((string path, float startTime) in audioFiles)
        {
            if (!io.FileExists(path))
            {
                logger.LogWarning("File {Path} does not exist!", path);
                continue;
            }

            anyExists = true;

            AudioFileReader reader = new(path);
            OffsetSampleProvider offsetSampleProvider = new(reader);

            if (startTime >= 0)
                offsetSampleProvider.DelayBy = TimeSpan.FromSeconds(startTime);
            else
                offsetSampleProvider.SkipOver = TimeSpan.FromSeconds(-startTime);

            sampleProviders.Add(offsetSampleProvider);
        }

        if (!anyExists)
            return;

        MixingSampleProvider mixer = new(sampleProviders[0].WaveFormat);
        foreach (ISampleProvider sampleProvider in sampleProviders)
            mixer.AddMixerInput(sampleProvider);

        io.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WaveFileWriter.CreateWaveFile16(outputPath, mixer.ToWaveProvider16().ToSampleProvider());
    }
}