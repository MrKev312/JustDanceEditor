namespace JustDanceEditor.UI.Converting;

/// <summary>
/// Defines the interface for song conversion workflows.
/// </summary>
public interface IConversionWorkflow
{
    /// <summary>
    /// Converts all songs in a folder in batch mode.
    /// </summary>
    void ConvertAllSongsInFolder();
}