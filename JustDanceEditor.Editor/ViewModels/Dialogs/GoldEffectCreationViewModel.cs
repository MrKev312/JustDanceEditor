using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public partial class GoldEffectCreationResult
{
    public int Frames { get; set; }
    public int EffectType { get; set; }
}

public partial class GoldEffectCreationViewModel : ObservableObject, IDialogResult<GoldEffectCreationResult>
{
    [ObservableProperty]
    private decimal durationBeats = 1.0M;

    [ObservableProperty]
    private int effectType = 0;

    public GoldEffectCreationResult? Result { get; private set; }

    public void Accept()
    {
        Result = new GoldEffectCreationResult
        {
            Frames = (int)((double)DurationBeats * 24.0),
            EffectType = EffectType
        };
    }

    public void Cancel() => Result = null;
}