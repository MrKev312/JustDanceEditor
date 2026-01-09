using NAudio.Wave;

namespace JustDanceEditor.Audio;

public interface IAudioConverter
{
    /// <summary>
    /// Converts audio from a source stream and returns a WaveStream for further processing.
    /// </summary>
    /// <param name="source">The source stream containing audio data.</param>
    /// <param name="sourceFileName">The original filename, used for format detection.</param>
    /// <returns>A WaveStream that can be used with NAudio for mixing/processing.</returns>
    Task<WaveStream> ConvertAsync(Stream source, string sourceFileName);
}
