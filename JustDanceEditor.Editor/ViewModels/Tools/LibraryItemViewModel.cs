using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

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
                // initialize icon and duration
                Icon = new SolidColorBrush(field.Color);
                DefaultDuration = field.DefaultDuration;
            }
        }
    }

    private void OnDefinitionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Timeline.MoveDefinitionViewModel.Color) && Definition != null)
        {
            Icon = new SolidColorBrush(Definition.Color);
        }

        if (e.PropertyName == nameof(Timeline.MoveDefinitionViewModel.DefaultDuration) && Definition != null)
        {
            DefaultDuration = Definition.DefaultDuration;
        }
    }

    public override string ToString() => Name;
}