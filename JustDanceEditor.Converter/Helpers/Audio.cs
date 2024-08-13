using JustDanceEditor.Logging;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JustDanceEditor.Converter.Helpers;

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
                Logger.Log($"File {path} does not exist!", LogLevel.Warning);
                continue;
            }

            anyExists = true;

            // Read in the next file with the correct start time
            AudioFileReader reader = new(path);

            OffsetSampleProvider offsetSampleProvider = new(reader);

            // If index is 0 or greater, set the offset
            if (startTime >= 0)
                offsetSampleProvider.DelayBy = TimeSpan.FromSeconds(startTime);
            // Else, skip
            else
                offsetSampleProvider.SkipOver = TimeSpan.FromSeconds(-startTime);


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