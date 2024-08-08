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
