using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Audio;
public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    static readonly IAudioConverter audioConverter = new VGMStreamAdapter();

    public async static Task ConvertAudioAsync(ConversionContext context) =>
        await Task.Run(() => Convert(context));

    public static void ConvertAudio(ConversionContext context)
    {
        try
        {
            Convert(context);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert audio files: {e.Message}", LogLevel.Error);
        }
    }

    static void Convert(ConversionContext context)
    {
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
        GeneratePreviewAudio(context, opusPath);

        MoveOpusToOutput(context, opusPath);
    }

    static void GeneratePreviewAudio(ConversionContext context, string opusPath)
    {
        float startTime = context.SongData.GetPreviewStartTime();

        // Generate the preview audio file
        string previewOpusPath = Path.Combine(context.FileSystem.TempFolders.AudioFolder, "preview.opus");

        GeneratePreviewAudioFFMpeg(opusPath, previewOpusPath, startTime);

        // Move the preview audio file to the output folder
        string md5 = Download.GetFileMD5(previewOpusPath);
        if (context.Request.ExportType == ExportType.CustomServer)
            md5 += ".opus";
        string outputFolder = context.FileSystem.OutputFolders.PreviewAudioFolder;
        Directory.CreateDirectory(outputFolder);
        string outputOpusPath = Path.Combine(outputFolder, md5);
        File.Move(previewOpusPath, outputOpusPath, true);
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

    static void MoveOpusToOutput(ConversionContext context, string opusPath)
    {
        // Copy the Opus file to the output folder
        string md5 = Download.GetFileMD5(opusPath);
        if (context.Request.ExportType == ExportType.CustomServer)
            md5 += ".opus";
        string outputFolder = context.FileSystem.OutputFolders.AudioFolder;
        Directory.CreateDirectory(outputFolder);
        string outputOpusPath = Path.Combine(outputFolder, md5);
        File.Move(opusPath, outputOpusPath, true);
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
            float offset = clip.StartTime / 50f;
            offset += mainSongOffset;
            audioFiles.Add((wavPath, offset));
        }

        // Call the helper to merge audio files
        Helpers.Audio.MergeAudioFiles([.. audioFiles], Path.Combine(context.FileSystem.TempFolders.AudioFolder, "merged.wav"));

        stopwatch.Stop();
        Logger.Log($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
