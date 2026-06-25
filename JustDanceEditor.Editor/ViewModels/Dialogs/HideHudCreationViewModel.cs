using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class HideHudCreationResult
{
    public int Frames { get; set; }
}

public partial class HideHudCreationViewModel : ObservableObject, IDialogResult<HideHudCreationResult>
{
    [ObservableProperty]
    public partial decimal DurationBeats { get; set; } = 1.0M;

    public HideHudCreationResult? Result { get; private set; }

    public void Accept()
    {
        Result = new HideHudCreationResult
        {
            Frames = (int)((double)DurationBeats * 24.0)
        };
    }

    public void Cancel() => Result = null;
}