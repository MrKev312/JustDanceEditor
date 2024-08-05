using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JustDanceEditor.Converter.Helpers;

/// <summary>
/// Interface for converting audio files
/// </summary>
public interface IAudioConverter
{
    /// <summary>
    /// Convert an audio file
    /// </summary>
    /// <param name="sourcePath">The path to the source audio file</param>
    /// <param name="targetPath">The path to save the converted audio file</param>
    /// <returns>A task that represents the asynchronous conversion operation</returns>
    Task Convert(string sourcePath, string targetPath);
}

public static class Audio
{
    public static void MergeAudioFiles((string path, float startTime)[] audioFiles, string outputPath)
    {
        // If there are no files, return
        if (audioFiles.Length == 0)
            return;

        // Get the endtime which is the file where offset+duration is the highest
        float endTime = audioFiles.Max(x => x.startTime + new AudioFileReader(x.path).TotalTime.Seconds);

        // Create a list to hold the SampleProviders
        List<ISampleProvider> sampleProviders = [];

        bool anyExists = false;

        // Loop through the audio files
        foreach ((string path, float startTime) in audioFiles)
        {
            // If the file doesn't exist, skip it, but throw a big red warning
            if (!File.Exists(path))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"File {path} does not exist!");
                Console.ResetColor();
                continue;
            }

            anyExists = true;

            // Read in the next file with the correct start time
            AudioFileReader reader = new(path);

            OffsetSampleProvider offsetSampleProvider = new(reader)
            {
                DelayBy = TimeSpan.FromSeconds(startTime)
            };

            // Add the reader to the list of SampleProviders
            sampleProviders.Add(offsetSampleProvider);
        }

        // If no files exist, return
        if (!anyExists)
            return;

        // Create a mixer with the SampleProviders
        MixingSampleProvider mixer = new(sampleProviders[0].WaveFormat);

        // Loop through the SampleProviders
        foreach (ISampleProvider sampleProvider in sampleProviders)
            // Add the SampleProvider to the mixer
            mixer.AddMixerInput(sampleProvider);

        // Create the directory if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Write the output to a file
        WaveFileWriter.CreateWaveFile16(outputPath, mixer.ToWaveProvider16().ToSampleProvider());
    }
}