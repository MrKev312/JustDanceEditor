using System.Diagnostics;

using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Audio;
public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    private static IAudioConverter audioConverter = new VGMStreamAdapter();

    public static void ConvertAudio(ConvertUbiArtToUnity convert)
    {
        if (convert.SongData.EngineVersion != JDVersion.JDUnlimited)
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
    }

    private static void Convert(ConvertUbiArtToUnity convert)
    {
        Console.WriteLine("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();
        SoundSetClip[] audioClips = GetAudioClips(convert.SongData.MainSequence.Clips);

        ConvertAudioFiles(convert, audioClips);
        string newMainSongPath = ConvertMainSong(convert);

        Console.WriteLine($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        MergeAudioFiles(convert, audioClips, newMainSongPath);

        string opusPath = ConvertToOpus(convert);
        MoveOpusToOutput(convert, opusPath);
    }

    private static void MoveOpusToOutput(ConvertUbiArtToUnity convert, string opusPath)
    {
        // Copy the Opus file to the output folder
        string md5 = Download.GetFileMD5(opusPath);
        string outputFolder = Path.Combine(convert.OutputFolder, "cachex", "Audio_opus");
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

    private static string ConvertMainSong(ConvertUbiArtToUnity convert)
    {
        string mainSongPath = GetMainSongPath(convert);
        string newMainSongPath = Path.Combine(convert.TempAudioFolder, "mainSong.wav");
        audioConverter.Convert(mainSongPath, newMainSongPath);
        return newMainSongPath;
    }

    private static string GetMainSongPath(ConvertUbiArtToUnity convert)
    {
        return Directory.Exists(Path.Combine(convert.WorldFolder, "audio"))
            ? Directory.GetFiles(Path.Combine(convert.WorldFolder, "audio"))[0]
            : Directory.GetFiles(Path.Combine(convert.CacheFolder, "audio")).Where(x => x.EndsWith(".wav.ckd")).First();
    }

    private static void MergeAudioFiles(ConvertUbiArtToUnity convert, SoundSetClip[] audioClips, string newMainSongPath)
    {
        Console.WriteLine("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Prepare the array with an extra slot for the main song
        List<(string path, float offset)> audioFiles = [];

        // Process the main song
        // Assuming the main song's offset is determined by the startBeat and markers in songData
        int startBeat = Math.Abs(convert.SongData.MTrack.COMPONENTS[0].trackData.structure.startBeat);
        int marker = convert.SongData.MTrack.COMPONENTS[0].trackData.structure.markers[startBeat];
        float mainSongOffset = marker / 48f / 1000f; // Convert to seconds
        audioFiles.Add((newMainSongPath, mainSongOffset));

        // Process each audio clip
        for (int i = 0; i < audioClips.Length; i++)
        {
            SoundSetClip clip = audioClips[i];
            string fileName = Path.GetFileNameWithoutExtension(clip.SoundSetPath);
            string wavPath = Path.Combine(convert.TempAudioFolder, $"{fileName}.wav");

            // If the wav file doesn't exist, skip it
            if (!File.Exists(wavPath))
                continue;

            // Calculate the offset, 56 seems to be the magic number?
            float offset = clip.StartTime / 56f;
            audioFiles.Add((wavPath, offset));
        }

        // Adjust offsets
        for (int i = 1; i < audioFiles.Count; i++)
        {
            float offset = audioFiles[i].offset;

            if (offset >= 0)
                audioFiles[i] = (audioFiles[i].path, offset - mainSongOffset);
        }

        // Call the helper to merge audio files
        Helpers.Audio.MergeAudioFiles(audioFiles.ToArray(), Path.Combine(convert.TempAudioFolder, "merged.wav"));

        stopwatch.Stop();
        Console.WriteLine($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
