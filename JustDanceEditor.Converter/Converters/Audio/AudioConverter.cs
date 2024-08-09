﻿using System.Diagnostics;

using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Audio;
public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    private static IAudioConverter audioConverter = new VGMStreamAdapter();

    public static void ConvertAudio(ConvertUbiArtToUnity convert)
    {
        try
        {
            Convert(convert);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to convert audio files: {e.Message}");
        }
    }

    private static void Convert(ConvertUbiArtToUnity convert)
    {
        Console.WriteLine("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();
        SoundSetClip[] audioClips = GetAudioClips(convert.SongData.MainSequence.Clips);

        string mainSongPath = GetMainSongPath(convert);
        string newMainSongPath = ConvertMainSong(convert, mainSongPath);

        Console.WriteLine($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

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

    private static void GeneratePreviewAudio(ConvertUbiArtToUnity convert, string opusPath)
    {
        (float startTime, float endTime) = convert.SongData.GetPreviewStartEndTimes();

        // Get info about the Opus file
        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(opusPath).Result;

        // Check if the endTime is below the duration of the Opus file
        if (endTime > mediaInfo.Duration.TotalSeconds)
            endTime = (float)mediaInfo.Duration.TotalSeconds;

        // Generate the preview audio file
        string previewOpusPath = Path.Combine(convert.TempAudioFolder, "preview.opus");

        GeneratePreviewAudioFFMpeg(opusPath, previewOpusPath, startTime, endTime);

        // Move the preview audio file to the output folder
        string md5 = Download.GetFileMD5(previewOpusPath);
        string outputFolder = Path.Combine(convert.Output0Folder, "AudioPreview_opus");
        Directory.CreateDirectory(outputFolder);
        string outputOpusPath = Path.Combine(outputFolder, md5);
        File.Move(previewOpusPath, outputOpusPath, true);
    }

    private static void GeneratePreviewAudioFFMpeg(string opusPath, string previewOpusPath, float startTime, float endTime)
    {
        IConversion conversion = FFmpeg.Conversions.New()
            .UseMultiThread(true);

        IStream stream = FFmpeg.GetMediaInfo(opusPath).Result.AudioStreams.First()
            .SetCodec(AudioCodec.libopus)
            .SetSampleRate(48000);

        conversion.AddStream(stream)
            .SetOverwriteOutput(true)
            .UseMultiThread(true)
            .SetSeek(TimeSpan.FromSeconds(startTime))
            // Set fade-in of .1 seconds
            .AddParameter($"-af \"afade=t=in:st={startTime}:d=1,afade=t=out:st={endTime - 1}:d=1\"")
            .AddParameter("-t 30")
            .SetOutput(previewOpusPath)
            .SetOverwriteOutput(true)
            .Start().Wait();
    }

    private static void MoveOpusToOutput(ConvertUbiArtToUnity convert, string opusPath)
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
        IConversion conversion = FFmpeg.Conversions.New()
            .UseMultiThread(true);

        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(mergedWavPath).Result;

        IStream stream = mediaInfo.AudioStreams.First()
            .SetCodec(AudioCodec.libopus)
            .SetSampleRate(48000);

        conversion.AddStream(stream)
            .AddParameter("-sample_fmt flt")
            .SetOverwriteOutput(true)
            .UseMultiThread(true)
            .SetOutput(opusPath)
            .SetOverwriteOutput(true)
            .Start().Wait();
    }

    private static SoundSetClip[] GetAudioClips(IClip[] clips)
    {
        return clips.OfType<SoundSetClip>().ToArray();
    }

    private static void ConvertAudioFiles(ConvertUbiArtToUnity convert, SoundSetClip[] audioClips)
    {
        foreach (SoundSetClip audioVibrationClip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(audioVibrationClip.SoundSetPath);
            string wavPath = Path.Combine(convert.CacheFolder, "audio", "amb", $"{fileName}.wav.ckd");
            string newWavPath = Path.Combine(convert.TempAudioFolder, $"{fileName}.wav");
            audioConverter.Convert(wavPath, newWavPath);
        }
    }

    private static string ConvertMainSong(ConvertUbiArtToUnity convert, string mainSongPath)
    {
        string newMainSongPath = Path.Combine(convert.TempAudioFolder, "mainSong.wav");
        audioConverter.Convert(mainSongPath, newMainSongPath);
        return newMainSongPath;
    }

    private static string GetMainSongPath(ConvertUbiArtToUnity convert)
    {
        string[] audios = [];
        // Is there any *.ogg file in the media folder?
        if (Directory.Exists(convert.InputMediaFolder))
            audios = Directory.GetFiles(convert.InputMediaFolder, "*.ogg");
        if (audios.Length > 0)
            return audios[0];

        if (Directory.Exists(Path.Combine(convert.CacheFolder, "audio")))
        audios = Directory.GetFiles(Path.Combine(convert.CacheFolder, "audio"), "*.wav.ckd");
        if (audios.Length > 0)
            return audios[0];

        if (Directory.Exists(Path.Combine(convert.WorldFolder, "audio")))
            audios = Directory.GetFiles(Path.Combine(convert.WorldFolder, "audio"), "*.ogg");
        if (audios.Length > 0)
            return audios[0];

        throw new Exception("Main song not found");
    }

    private static void MergeAudioFiles(ConvertUbiArtToUnity convert, SoundSetClip[] audioClips, string newMainSongPath)
    {
        Console.WriteLine("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Prepare the array with an extra slot for the main song
        List<(string path, float offset)> audioFiles = [];

        // Process the main song
        // Assuming the main song's offset is determined by the startBeat and markers in songData
        int startBeat = Math.Abs(convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.startBeat);
        int marker = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[startBeat];
        float mainSongOffset = marker / 48f / 1000f; // Convert to seconds
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
        Console.WriteLine($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
