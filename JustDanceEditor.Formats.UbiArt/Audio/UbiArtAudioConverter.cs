using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

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
    IAudioConverter AudioConverter);

public static class UbiArtAudioConverter
{
    public static Task ConvertAudioAsync(UbiArtAudioConversionRequest request) =>
        Task.Run(() => ConvertAudio(request));

    public static void ConvertAudio(UbiArtAudioConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SongData);
        ArgumentNullException.ThrowIfNull(request.MainSongFile);
        ArgumentNullException.ThrowIfNull(request.AudioClips);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TempAudioFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MasterOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewOutputFolder);
        ArgumentNullException.ThrowIfNull(request.AudioConverter);

        Directory.CreateDirectory(request.TempAudioFolder);

        Logger.Log("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        string mainSongWavPath = ConvertMainSong(request);

        Logger.Log($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        string mergedWavPath = Path.Combine(request.TempAudioFolder, "merged.wav");

        if (request.IsMainSongPreMerged)
        {
            File.Move(mainSongWavPath, mergedWavPath, true);
        }
        else
        {
            ConvertAudioClips(request);
            MergeAudioFiles(request, mainSongWavPath, mergedWavPath);
        }

        string opusPath = ConvertToOpus(request, mergedWavPath);
        GeneratePreviewAudio(request, opusPath);
        MoveOpusToOutput(request, opusPath);
    }

    private static void ConvertAudioClips(UbiArtAudioConversionRequest request)
    {
        foreach (UbiArtAudioClipSource clipSource in request.AudioClips)
        {
            string targetPath = Path.Combine(request.TempAudioFolder, clipSource.File.Name + clipSource.File.Extension);
            if (File.Exists(targetPath))
                continue;

            request.AudioConverter.Convert(clipSource.File, targetPath).GetAwaiter().GetResult();
        }
    }

    private static string ConvertMainSong(UbiArtAudioConversionRequest request)
    {
        string targetPath = Path.Combine(request.TempAudioFolder, "mainSong.wav");
        request.AudioConverter.Convert(request.MainSongFile, targetPath).GetAwaiter().GetResult();
        return targetPath;
    }

    private static void MergeAudioFiles(UbiArtAudioConversionRequest request, string mainSongWavPath, string mergedWavPath)
    {
        Logger.Log("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<(string path, float offset)> audioFiles = [];
        audioFiles.Add((mainSongWavPath, request.SongData.GetSongStartTime()));

        foreach (UbiArtAudioClipSource clipSource in request.AudioClips)
        {
            string clipName = Path.GetFileNameWithoutExtension(clipSource.Clip.SoundSetPath);
            string wavPath = Path.Combine(request.TempAudioFolder, clipName + ".wav");
            if (!File.Exists(wavPath))
                continue;

            float offset = CalculateClipOffset(request, clipSource.Clip);
            audioFiles.Add((wavPath, offset));
        }

        MergeAudioFilesInternal([.. audioFiles], mergedWavPath);

        stopwatch.Stop();
        Logger.Log($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
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

    private static string ConvertToOpus(UbiArtAudioConversionRequest request, string mergedWavPath)
    {
        string opusPath = Path.Combine(request.TempAudioFolder, "merged.opus");

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

        Logger.Log($"Converted song audio with \"{result.Arguments}\"", LogLevel.Debug);
        return opusPath;
    }

    private static void GeneratePreviewAudio(UbiArtAudioConversionRequest request, string opusPath)
    {
        float startTime = request.SongData.GetPreviewStartTime();
        string previewOpusPath = Path.Combine(request.TempAudioFolder, "preview.opus");

        IConversion conversion = FFmpeg.Conversions.New();

        IStream stream = FFmpeg.GetMediaInfo(opusPath).Result.AudioStreams.First()
            .SetCodec(AudioCodec.libopus)
            .SetSampleRate(48000);

        IConversionResult result = conversion.AddStream(stream)
            .SetOverwriteOutput(true)
            .UseMultiThread(true)
            .SetSeek(TimeSpan.FromSeconds(startTime))
            .AddParameter($"-af \"afade=t=in:st={startTime}:d=1,afade=t=out:st={startTime + 30 - 1}:d=1\"")
            .AddParameter("-t 30")
            .SetOutput(previewOpusPath)
            .SetOverwriteOutput(true)
            .Start().Result;

        Logger.Log($"Generated preview audio with \"{result.Arguments}\"", LogLevel.Debug);

        MoveAudioToOutput(previewOpusPath, request.PreviewOutputFolder, "preview.opus");
    }

    private static void MoveOpusToOutput(UbiArtAudioConversionRequest request, string opusPath)
    {
        MoveAudioToOutput(opusPath, request.MasterOutputFolder, "master.opus");
    }

    private static void MoveAudioToOutput(string sourcePath, string destinationFolder, string targetFileName)
    {
        Directory.CreateDirectory(destinationFolder);
        
        string targetPath = Path.Combine(destinationFolder, targetFileName);
        
        File.Move(sourcePath, targetPath, true);
    }

    private static void MergeAudioFilesInternal((string path, float startTime)[] audioFiles, string outputPath)
    {
        if (audioFiles.Length == 0)
            return;

        List<ISampleProvider> sampleProviders = [];
        bool anyExists = false;

        foreach ((string path, float startTime) in audioFiles)
        {
            if (!File.Exists(path))
            {
                Logger.Log($"File {path} does not exist!", LogLevel.Warning);
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

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WaveFileWriter.CreateWaveFile16(outputPath, mixer.ToWaveProvider16().ToSampleProvider());
    }
}