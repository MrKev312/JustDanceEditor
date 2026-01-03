using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;

using System.Collections.Generic;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class MoveCreationResult
{
    public string MoveId { get; set; } = string.Empty;
    public int Frames { get; set; }
    public bool IsGold { get; set; }
}

public partial class MoveCreationViewModel(IEnumerable<string>? moves = null, bool isFullBody = false) : ObservableObject, IDialogResult<MoveCreationResult>
{
    public IEnumerable<string> AvailableMoves { get; } = moves ?? [];
    public bool IsFullBody { get; } = isFullBody;

    [ObservableProperty]
    public partial string SelectedMove { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGold { get; set; } = false;

    // Read-only duration display (beats) for the selected move
    [ObservableProperty]
    public partial string DurationDisplay { get; set; } = "1";
    public MoveCreationResult? Result { get; private set; }

    public void SetDurationDisplay(string v) => DurationDisplay = v;

    public void Accept(int frames)
    {
        Result = new MoveCreationResult
        {
            MoveId = SelectedMove ?? string.Empty,
            Frames = frames,
            IsGold = IsGold
        };
    }

    public void Cancel() => Result = null;
}