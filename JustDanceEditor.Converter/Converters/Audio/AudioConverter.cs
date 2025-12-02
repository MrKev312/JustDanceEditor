using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Audio;

public sealed class AudioConversionOptions
{
    public string? MasterOutputFolder { get; init; }
    public string? PreviewOutputFolder { get; init; }
}

public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    static readonly IAudioConverter audioConverter = new VGMStreamAdapter();

    public async static Task ConvertAudioAsync(ConversionContext context, AudioConversionOptions? options = null) =>
        await Task.Run(() => Convert(context, options));

    public static void ConvertAudio(ConversionContext context, AudioConversionOptions? options = null)
    {
        try
        {
            Convert(context, options);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert audio files: {e.Message}", LogLevel.Error);
        }
    }

    static void Convert(ConversionContext context, AudioConversionOptions? options)
    {
        string masterOutputFolder = options?.MasterOutputFolder ?? context.FileSystem.OutputFolders.AudioFolder;
        string previewOutputFolder = options?.PreviewOutputFolder ?? context.FileSystem.OutputFolders.PreviewAudioFolder;

        Logger.Log("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();
        SoundSetClip[] audioClips = GetAudioClips([.. context.SongData.Clips]);

        CookedFile mainSongPath = GetMainSongPath(context);
        string newMainSongPath = ConvertMainSong(context, mainSongPath);

        Logger.Log($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        if (mainSongPath.FullPath.Contains(context.FileSystem.InputFolders.MediaFolder, StringComparison.OrdinalIgnoreCase))
            // If the song is pre-merged, just move it to the temp audio folder
            File.Move(newMainSongPath, Path.Combine(context.FileSystem.TempFolders.AudioFolder, "merged.wav"), true);
        else
        {
            // Else, convert and merge the audio files
            ConvertAudioFiles(context, audioClips);
            MergeAudioFiles(context, audioClips, newMainSongPath);
        }

        string opusPath = ConvertToOpus(context);

        // Now we quickly generate the preview audio file
        GeneratePreviewAudio(context, opusPath, previewOutputFolder);

        MoveOpusToOutput(context, opusPath, masterOutputFolder);
    }

    static void GeneratePreviewAudio(ConversionContext context, string opusPath, string previewOutputFolder)
    {
        float startTime = context.SongData.GetPreviewStartTime();

        // Generate the preview audio file
        string previewOpusPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, "preview.opus");

        GeneratePreviewAudioFFMpeg(opusPath, previewOpusPath, startTime);

        MoveHashedAudio(previewOpusPath, previewOutputFolder, context.Request.ExportType == ExportType.CustomServer);
    }

    static void GeneratePreviewAudioFFMpeg(string opusPath, string previewOpusPath, float startTime)
    {
        IConversion conversion = FFmpeg.Conversions.New();

        IStream stream = FFmpeg.GetMediaInfo(opusPath).Result.AudioStreams.First()
            .SetCodec(AudioCodec.libopus)
            .SetSampleRate(48000);

        IConversionResult result = conversion.AddStream(stream)
            .SetOverwriteOutput(true)
            .UseMultiThread(true)
            .SetSeek(TimeSpan.FromSeconds(startTime))
            // Set fade-in of .1 seconds
            .AddParameter($"-af \"afade=t=in:st={startTime}:d=1,afade=t=out:st={startTime + 30 - 1}:d=1\"")
            .AddParameter("-t 30")
            .SetOutput(previewOpusPath)
            .SetOverwriteOutput(true)
            .Start().Result;

        Logger.Log($"Generated preview audio with \"{result.Arguments}\"", LogLevel.Debug);
    }

    static void MoveOpusToOutput(ConversionContext context, string opusPath, string masterOutputFolder)
    {
        MoveHashedAudio(opusPath, masterOutputFolder, context.Request.ExportType == ExportType.CustomServer);
    }

    static void MoveHashedAudio(string sourcePath, string destinationFolder, bool appendExtension)
    {
        string md5 = Download.GetFileMD5(sourcePath);
        if (appendExtension)
            md5 += Path.GetExtension(sourcePath);

        Directory.CreateDirectory(destinationFolder);
        string target = Path.Combine(destinationFolder, md5);
        File.Move(sourcePath, target, true);
    }

    static string ConvertToOpus(ConversionContext context)
    {
        // FFMpeg to convert the merged audio file to Opus
        string mergedWavPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, "merged.wav");
        string opusPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, "merged.opus");

        ConvertToOpusFFMpeg(mergedWavPath, opusPath);

        return opusPath;
    }

    static void ConvertToOpusFFMpeg(string mergedWavPath, string opusPath)
    {
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
    }

    static SoundSetClip[] GetAudioClips(IClip[] clips)
    {
        return [.. clips.OfType<SoundSetClip>()];
    }

    static void ConvertAudioFiles(ConversionContext context, SoundSetClip[] audioClips)
    {
        foreach (SoundSetClip audioClip in audioClips)
        {
            // Change extension to .wav
            string relativePath = Path.ChangeExtension(audioClip.SoundSetPath, ".wav");
            if (!context.FileSystem.GetFilePath(relativePath, out CookedFile? wavPath))
                continue;
            string newWavPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, wavPath.Name + wavPath.Extension);
            if (!File.Exists(newWavPath))
                audioConverter.Convert(wavPath, newWavPath).Wait();
        }
    }

    static string ConvertMainSong(ConversionContext context, string mainSongPath)
    {
        string newMainSongPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, "mainSong.wav");
        audioConverter.Convert(mainSongPath, newMainSongPath).Wait();
        return newMainSongPath;
    }

    static CookedFile GetMainSongPath(ConversionContext context)
    {
        // First we check the media folder
        if (context.FileSystem.GetFolderPath(context.FileSystem.InputFolders.MediaFolder, out string? mediaFolder))
        {
            string[] oggFiles = Directory.GetFiles(mediaFolder, "*.ogg", SearchOption.AllDirectories);
            if (oggFiles.Length > 0)
                return new CookedFile(oggFiles.First());
        }

        string relativePath = context.SongData.MusicTrack.COMPONENTS[0].trackData.path;

        if (context.FileSystem.GetFilePath(relativePath, out CookedFile? mainSongPath))
            return mainSongPath;

        throw new Exception("Main song not found");
    }

    static void MergeAudioFiles(ConversionContext context, SoundSetClip[] audioClips, string newMainSongPath)
    {
        Logger.Log("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Prepare the array with an extra slot for the main song
        List<(string path, float offset)> audioFiles = [];

        // Process the main song
        // Assuming the main song's offset is determined by the startBeat and markers in songData
        float mainSongOffset = context.SongData.GetSongStartTime();
        audioFiles.Add((newMainSongPath, mainSongOffset));

        // Process each audio clip
        foreach (SoundSetClip clip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(clip.SoundSetPath);
            string wavPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, $"{fileName}.wav");

            // If the wav file doesn't exist, skip it
            if (!File.Exists(wavPath))
                continue;

            // Calculate the offset
            double markerIndex = clip.StartTime / 24f;
            // If the marker index is between two markers, we interpolate the time
            int lowerMarker = (int)Math.Floor(Math.Abs(markerIndex));
            int upperMarker = (int)Math.Ceiling(Math.Abs(markerIndex));
            float offset = 0f;
            if (lowerMarker == upperMarker)
            {
                offset = context.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[lowerMarker] / 48f / 1000f;
            }
            else
            {
                float lowerTime = context.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[lowerMarker] / 48f / 1000f;
                float upperTime = context.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[upperMarker] / 48f / 1000f;
                float t = (float)(Math.Abs(markerIndex) - lowerMarker);
                offset = lowerTime + (t * (upperTime - lowerTime));
            }

            // If the original marker index was negative, negate the offset
            if (markerIndex < 0)
                offset *= -1;
            offset += mainSongOffset;
            audioFiles.Add((wavPath, offset));
        }

        // Call the helper to merge audio files
        Helpers.Audio.MergeAudioFiles([.. audioFiles], Path.Combine(context.FileSystem.TempFolders.AudioFolder, "merged.wav"));

        stopwatch.Stop();
        Logger.Log($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}