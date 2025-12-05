namespace JustDanceEditor.Formats.UbiArt.Audio;

/// <summary>
/// Interface for converting audio files.
/// </summary>
public interface IAudioConverter
{
    /// <summary>
    /// Convert an audio file.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the source audio file.</param>
    /// <param name="targetPath">Absolute path to the destination audio file.</param>
    Task Convert(string sourcePath, string targetPath);
}