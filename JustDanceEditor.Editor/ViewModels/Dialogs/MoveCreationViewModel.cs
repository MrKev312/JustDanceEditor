using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Services;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class MoveCreationResult
{
    public string MoveId { get; set; } = string.Empty;
    public int Frames { get; set; }
    public bool IsGold { get; set; }
}

public partial class MoveCreationViewModel : ObservableObject, IDialogResult<MoveCreationResult>
{
    public IEnumerable<string> AvailableMoves { get; }
    public bool IsFullBody { get; }
    public MoveCreationViewModel(IEnumerable<string>? moves = null, bool isFullBody = false)
    {
        AvailableMoves = moves ?? Enumerable.Empty<string>();
        IsFullBody = isFullBody;
    }

    [ObservableProperty]
    private string selectedMove = string.Empty;

    [ObservableProperty]
    private bool isGold = false;

    // Read-only duration display (beats) for the selected move
    [ObservableProperty]
    private string durationDisplay = "1";

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