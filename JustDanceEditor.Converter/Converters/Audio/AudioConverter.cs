using System.Diagnostics;
using System.Text.Json;

using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Resources;
using JustDanceEditor.Converter.UbiArt;
using JustDanceEditor.Converter.UbiArt.Tapes;

namespace JustDanceEditor.Converter.Converters.Audio;
public static class AudioConverter
{
    // Interface to make it easier to switch between different audio converters
    private static IAudioConverter audioConverter = new VGMStreamAdapter();

    public static void ConvertAudio(ConvertUbiArtToUnity convert, JDUbiArtSong songData)
    {
        Console.WriteLine("Converting audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        string tempAudioFolder = Path.Combine(convert.TempMapFolder, "audio");
        Directory.CreateDirectory(tempAudioFolder);

        MainSequence mainSequence = GetMainSequence(convert);
        AudioVibrationClip[] audioClips = GetAudioClips(mainSequence);

        ConvertAudioFiles(convert, tempAudioFolder, audioClips);
        string newMainSongPath = ConvertMainSong(convert, tempAudioFolder);

        Console.WriteLine($"Finished converting audio files in {stopwatch.ElapsedMilliseconds}ms");

        MergeAudioFiles(songData, audioClips, tempAudioFolder, newMainSongPath);
    }

    private static MainSequence GetMainSequence(ConvertUbiArtToUnity convert)
    {
        string mainSequenceTapePath = Path.Combine(convert.CacheFolder, "cinematics", $"{convert.MapName}_mainsequence.tape.ckd");
        return JsonSerializer.Deserialize<MainSequence>(File.ReadAllText(mainSequenceTapePath).Replace("\0", ""))!;
    }

    private static AudioVibrationClip[] GetAudioClips(MainSequence mainSequence)
    {
        return mainSequence.Clips.Where(s => s.__class == "SoundSetClip").ToArray();
    }

    private static void ConvertAudioFiles(ConvertUbiArtToUnity convert, string tempAudioFolder, AudioVibrationClip[] audioClips)
    {
        foreach (AudioVibrationClip audioVibrationClip in audioClips)
        {
            string fileName = Path.GetFileNameWithoutExtension(audioVibrationClip.SoundSetPath);
            string wavPath = Path.Combine(convert.CacheFolder, "audio", "amb", $"{fileName}.wav.ckd");
            string newWavPath = Path.Combine(tempAudioFolder, $"{fileName}.wav");
            audioConverter.Convert(wavPath, newWavPath);
        }
    }

    private static string ConvertMainSong(ConvertUbiArtToUnity convert, string tempAudioFolder)
    {
        string mainSongPath = GetMainSongPath(convert);
        string newMainSongPath = Path.Combine(tempAudioFolder, "mainSong.wav");
        audioConverter.Convert(mainSongPath, newMainSongPath);
        return newMainSongPath;
    }

    private static string GetMainSongPath(ConvertUbiArtToUnity convert)
    {
        return Directory.Exists(Path.Combine(convert.MapsFolder, convert.MapName, "audio"))
            ? Directory.GetFiles(Path.Combine(convert.MapsFolder, convert.MapName, "audio"))[0]
            : Directory.GetFiles(Path.Combine(convert.CacheFolder, "audio")).Where(x => x.EndsWith(".wav.ckd")).First();
    }

    private static void MergeAudioFiles(JDUbiArtSong songData, AudioVibrationClip[] audioClips, string tempAudioFolder, string newMainSongPath)
    {
        Console.WriteLine("Merging audio files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Prepare the array with an extra slot for the main song
        (string path, float offset)[] audioFiles = new (string path, float offset)[audioClips.Length + 1];

        // Process each audio clip
        for (int i = 0; i < audioClips.Length; i++)
        {
            AudioVibrationClip clip = audioClips[i];
            string fileName = Path.GetFileNameWithoutExtension(clip.SoundSetPath);
            string wavPath = Path.Combine(tempAudioFolder, $"{fileName}.wav");

            // Calculate the offset (assuming StartTime is in ticks and 48 ticks per second)
            float offset = clip.StartTime / 48f;
            audioFiles[i] = (wavPath, offset);
        }

        // Process the main song
        // Assuming the main song's offset is determined by the startBeat and markers in songData
        int startBeat = Math.Abs(songData.MTrack.COMPONENTS[0].trackData.structure.startBeat);
        int marker = songData.MTrack.COMPONENTS[0].trackData.structure.markers[startBeat];
        float mainSongOffset = marker / 48f / 1000f; // Convert to seconds
        audioFiles[^1] = (newMainSongPath, mainSongOffset);

        // Adjust offsets so the first audio file starts at 0
        float lowestOffset = audioFiles.Min(x => x.offset);
        for (int i = 0; i < audioFiles.Length; i++)
        {
            audioFiles[i].offset -= lowestOffset;
        }

        // Call the helper to merge audio files
        Helpers.Audio.MergeAudioFiles(audioFiles, Path.Combine(tempAudioFolder, "merged.wav"));

        stopwatch.Stop();
        Console.WriteLine($"Finished merging audio files in {stopwatch.ElapsedMilliseconds}ms");
    }
}
