using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Views;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public enum ItemType
{
    Pictogram,
    HandMove,
    FullBodyMove
}

public partial class LibraryItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int UsageCount { get; set; } = 0;

    [ObservableProperty]
    public partial IBrush? Icon { get; set; }

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    [ObservableProperty]
    public partial ItemType Type { get; set; }

    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;

    /// <summary>
    /// Whether the move's asset file exists on disk. False items will show a
    /// striped icon.  This is driven by the underlying definition when present.
    /// </summary>
    [ObservableProperty]
    public partial bool HasAsset { get; set; } = true;

    /// <summary>
    /// Default duration in frames (24 frames == 1 beat). For moves this comes from the move definition when available, otherwise 24.
    /// </summary>
    [ObservableProperty]
    public partial double DefaultDuration { get; set; } = 24.0;

    public Timeline.MoveDefinitionViewModel? Definition
    {
        get;
        set
        {
            field?.PropertyChanged -= OnDefinitionChanged;

            field = value;

            if (field != null)
            {
                field.PropertyChanged += OnDefinitionChanged;
                // initialize icon, duration and asset flag
                HasAsset = field.HasAsset;
                Icon = HasAsset ? new SolidColorBrush(field.Color) : RenderingHelpers.GetStripedBrush(field.Color);
                DefaultDuration = field.DefaultDuration;
            }
        }
    }

    private void OnDefinitionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Definition == null)
            return;

        if (e.PropertyName == nameof(Timeline.MoveDefinitionViewModel.Color))
        {
            // update brush taking missing flag into account
            Icon = HasAsset ? new SolidColorBrush(Definition.Color) : RenderingHelpers.GetStripedBrush(Definition.Color);
        }

        if (e.PropertyName == nameof(Timeline.MoveDefinitionViewModel.DefaultDuration))
        {
            DefaultDuration = Definition.DefaultDuration;
        }

        if (e.PropertyName == nameof(Timeline.MoveDefinitionViewModel.HasAsset))
        {
            HasAsset = Definition.HasAsset;
            // refresh icon when asset status changes
            Icon = HasAsset ? new SolidColorBrush(Definition.Color) : RenderingHelpers.GetStripedBrush(Definition.Color);
        }
    }

    partial void OnHasAssetChanged(bool value)
    {
        // Whenever the flag toggles, if we already have a definition update the Icon
        if (Definition != null)
        {
            Icon = value ? new SolidColorBrush(Definition.Color) : RenderingHelpers.GetStripedBrush(Definition.Color);
        }
    }

    public override string ToString() => Name;
}