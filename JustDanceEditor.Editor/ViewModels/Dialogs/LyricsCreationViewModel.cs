using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class LyricsCreationResult
{
    public string Lyrics { get; set; } = string.Empty;
    public int Frames { get; set; }
    public bool IsEndOfLine { get; set; }
}

public partial class LyricsCreationViewModel : ObservableObject, IDialogResult<LyricsCreationResult>
{
    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private decimal durationBeats = 1.0M;

    [ObservableProperty]
    private bool isEndOfLine = false;

    public LyricsCreationResult? Result { get; private set; }

    public void Accept()
    {
        Result = new LyricsCreationResult
        {
            Lyrics = Lyrics ?? string.Empty,
            Frames = (int)((double)DurationBeats * 24.0),
            IsEndOfLine = IsEndOfLine
        };
    }

    public void Cancel() => Result = null;
}