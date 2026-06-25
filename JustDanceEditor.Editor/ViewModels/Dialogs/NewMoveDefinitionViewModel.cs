using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Dialogs;

public class NewMoveDefinitionResult
{
    public string Name { get; set; } = string.Empty;
    public int DurationFrames { get; set; }
    public Color Color { get; set; } = Colors.LightGray;
    public bool IsFullBody { get; set; }
}

public partial class NewMoveDefinitionViewModel(bool isFullBody = false) : ObservableObject, IDialogResult<NewMoveDefinitionResult>
{
    public bool IsFullBody { get; } = isFullBody;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    // user enters duration directly in ticks (24 ticks = 1 beat)
    [ObservableProperty]
    public partial int DurationTicks { get; set; } = 24;

    [ObservableProperty]
    public partial Color SelectedColor { get; set; } = Colors.LightGray;

    public NewMoveDefinitionResult? Result { get; private set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);

    public void Accept()
    {
        Result = new NewMoveDefinitionResult
        {
            Name = Name.Trim(),
            DurationFrames = DurationTicks,
            Color = SelectedColor,
            IsFullBody = IsFullBody
        };
    }

    public void Cancel() => Result = null;
}