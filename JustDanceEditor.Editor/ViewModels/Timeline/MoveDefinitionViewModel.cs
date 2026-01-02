using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class MoveDefinitionViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Color Color { get; set; } = Colors.LightGray;

    [ObservableProperty]
    public partial bool IsFullBody { get; set; } = false;

    /// <summary>
    /// Default duration in frames (24 frames == 1 beat)
    /// </summary>
    [ObservableProperty]
    public partial double DefaultDuration { get; set; } = 24.0;

    public override string ToString() => Id;
}