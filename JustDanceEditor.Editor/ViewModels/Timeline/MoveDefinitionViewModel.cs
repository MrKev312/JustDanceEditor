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

    /// <summary>
    /// Whether the corresponding motion classifier or gesture asset exists on disk.
    /// This is calculated by the timeline when the definition is registered.
    /// Library view and clip rendering use this flag to decorate missing moves.
    /// </summary>
    [ObservableProperty]
    public partial bool HasAsset { get; set; } = true;

    /// <summary>
    /// Convenience helper: true when we *do not* have a source asset.
    /// </summary>
    public bool IsAssetMissing => !HasAsset;

    public override string ToString() => Id;
}