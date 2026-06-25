namespace JustDanceEditor.Editor.ViewModels.Dialogs;

/// <summary>
/// Shared interface for ViewModels that support the waveform beat editing controls.
/// Implemented by both <see cref="NewSongViewModel"/> and <see cref="EditSongViewModel"/>.
/// </summary>
public interface ISongEditorViewModel
{
    /// <summary>Seek playback to the given time in seconds.</summary>
    void SeekTo(double timeSeconds);
}