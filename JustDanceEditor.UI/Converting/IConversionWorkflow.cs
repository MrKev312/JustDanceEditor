using JustDanceEditor.Formats.JDI;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.UI.Converting;

/// <summary>
/// Defines the interface for song conversion workflows.
/// </summary>
public interface IConversionWorkflow
{
    /// <summary>
    /// Updates covers and song title logos for maps.
    /// </summary>
    void UpdateCovers();

    /// <summary>
    /// Converts all songs in a folder in batch mode.
    /// </summary>
    void ConvertAllSongsInFolder();
}
