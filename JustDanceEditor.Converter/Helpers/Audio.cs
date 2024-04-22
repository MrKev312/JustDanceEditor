using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JustDanceEditor.Helpers;
internal static class Audio
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

        // Loop through the audio files
        foreach ((string path, float startTime) in audioFiles)
        {
            // Read in the next file with the correct start time
            AudioFileReader reader = new(path);

            OffsetSampleProvider offsetSampleProvider = new(reader)
            {
                DelayBy = TimeSpan.FromSeconds(startTime)
            };

            // Add the reader to the list of SampleProviders
            sampleProviders.Add(offsetSampleProvider);
        }

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