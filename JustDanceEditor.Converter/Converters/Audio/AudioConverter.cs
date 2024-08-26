using System.Diagnostics;

using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Audio;
public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    static readonly IAudioConverter audioConverter = new VGMStreamAdapter();

    public async static Task ConvertAudioAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => Convert(convert));

    public static void ConvertAudio(ConvertUbiArtToUnity convert)
    {
        try
        {
            Convert(convert);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert audio files: {e.Message}", LogLevel.Error);
        }
    }

    static void Convert(ConvertUbiArtToUnity convert)
    {
        Logger.Log("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();
        SoundSetClip[] audioClips = GetAudioClips(convert.SongData.MainSequence.Clips);

        string mainSongPath = GetMainSongPath(convert);
        string newMainSongPath = ConvertMainSong(convert, mainSongPath);

        Logger.Log($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        if (mainSongPath.StartsWith(convert.InputMediaFolder, StringComparison.OrdinalIgnoreCase))
            // If the song is pre-merged, just move it to the temp audio folder
            File.Move(newMainSongPath, Path.Combine(convert.TempAudioFolder, "merged.wav"), true);
        else
        {
            // Else, convert and merge the audio files
            ConvertAudioFiles(convert, audioClips);
            MergeAudioFiles(convert, audioClips, newMainSongPath);
        }

        string opusPath = ConvertToOpus(convert);

        // Now we quickly generate the preview audio file
        GeneratePreviewAudio(convert, opusPath);

        MoveOpusToOutput(convert, opusPath);
    }

    static void GeneratePreviewAudio(ConvertUbiArtToUnity convert, string opusPath)
    {
        float startTime = convert.SongData.GetPreviewStartTime();

        // Generate the preview audio file
        string previewOpusPath = Path.Combine(convert.TempAudioFolder, "preview.opus");

        GeneratePreviewAudioFFMpeg(opusPath, previewOpusPath, startTime);

        // Move the preview audio file to the output folder
        string md5 = Download.GetFileMD5(previewOpusPath);
        string outputFolder = Path.Combine(convert.Output0Folder, "AudioPreview_opus");
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

    static void MoveOpusToOutput(ConvertUbiArtToUnity convert, string opusPath)
    {
        // Copy the Opus file to the output folder
        string md5 = Download.GetFileMD5(opusPath);
        string outputFolder = Path.Combine(convert.OutputXFolder, "Audio_opus");
        Directory.CreateDirectory(outputFolder);
        string outputOpusPath = Path.Combine(outputFolder, md5);
        File.Move(opusPath, outputOpusPath, true);
    }

    static string ConvertToOpus(ConvertUbiArtToUnity convert)
    {
        // FFMpeg to convert the merged audio file to Opus
        string mergedWavPath = Path.Combine(convert.TempAudioFolder, "merged.wav");
        string opusPath = Path.Combine(convert.TempAudioFolder, "merged.opus");

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
        return clips.OfType<SoundSetClip>().ToArray();
    }

    static void ConvertAudioFiles(ConvertUbiArtToUnity convert, SoundSetClip[] audioClips)
    {
        foreach (SoundSetClip audioVibrationClip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(audioVibrationClip.SoundSetPath);
            string wavPath = Path.Combine(convert.CacheFolder, "audio", "amb", $"{fileName}.wav.ckd");
            string newWavPath = Path.Combine(convert.TempAudioFolder, $"{fileName}.wav");
            audioConverter.Convert(wavPath, newWavPath);
        }
    }

    static string ConvertMainSong(ConvertUbiArtToUnity convert, string mainSongPath)
    {
        string newMainSongPath = Path.Combine(convert.TempAudioFolder, "mainSong.wav");
        audioConverter.Convert(mainSongPath, newMainSongPath).Wait();
        return newMainSongPath;
    }

    static string GetMainSongPath(ConvertUbiArtToUnity convert)
    {
        List<string> ending = ["alt", "vip", "altretake"];
        List<string> audios = [];
        // Is there any *.ogg file in the media folder?
        string dir = convert.InputMediaFolder;
        if (Directory.Exists(dir))
            audios.AddRange(Directory.GetFiles(dir, "*.ogg"));

        dir = Path.Combine(convert.CacheFolder, "audio");
        if (Directory.Exists(Path.Combine(dir)))
            audios.AddRange(Directory.GetFiles(Path.Combine(dir), "*.wav.ckd"));

        dir = Path.Combine(convert.WorldFolder, "audio");
        if (Directory.Exists(Path.Combine(dir)))
            audios.AddRange(Directory.GetFiles(Path.Combine(dir), "*.ogg"));

        // If the songname ends with alt, try finding it without alt
        foreach (string end in ending)
        {
            string songName = convert.SongData.Name;
            if (!songName.EndsWith(end, StringComparison.OrdinalIgnoreCase))
                continue;

            songName = songName[..^end.Length];

            dir = convert.InputMediaFolder.Replace(convert.SongData.Name, songName, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                audios.AddRange(Directory.GetFiles(dir, "*.ogg"));

            dir = Path.Combine(convert.CacheFolder, "audio").Replace(convert.SongData.Name, songName, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                audios.AddRange(Directory.GetFiles(dir, "*.wav.ckd"));

            dir = Path.Combine(convert.WorldFolder, "audio").Replace(convert.SongData.Name, songName, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                audios.AddRange(Directory.GetFiles(dir, "*.ogg"));
        }

        if (audios.Count > 0)
            return audios.First();

        throw new Exception("Main song not found");
    }

    static void MergeAudioFiles(ConvertUbiArtToUnity convert, SoundSetClip[] audioClips, string newMainSongPath)
    {
        Logger.Log("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Prepare the array with an extra slot for the main song
        List<(string path, float offset)> audioFiles = [];

        // Process the main song
        // Assuming the main song's offset is determined by the startBeat and markers in songData
        float mainSongOffset = convert.SongData.GetSongStartTime();
        audioFiles.Add((newMainSongPath, mainSongOffset));

        // Process each audio clip
        foreach (SoundSetClip clip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(clip.SoundSetPath);
            string wavPath = Path.Combine(convert.TempAudioFolder, $"{fileName}.wav");

            // If the wav file doesn't exist, skip it
            if (!File.Exists(wavPath))
                continue;

            // Calculate the offset
            float offset = clip.StartTime / 50f;
            offset += mainSongOffset;
            audioFiles.Add((wavPath, offset));
        }

        // Call the helper to merge audio files
        Helpers.Audio.MergeAudioFiles(audioFiles.ToArray(), Path.Combine(convert.TempAudioFolder, "merged.wav"));

        stopwatch.Stop();
        Logger.Log($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
