using CommunityToolkit.Mvvm.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public partial class TimelineSettingsService : ObservableObject
{

    [ObservableProperty]
    public partial bool SnapToGrid { get; set; }

    [ObservableProperty]
    public partial bool SnapToCurrentTimeMarker { get; set; }

    [ObservableProperty]
    public partial double SnapGridSize { get; set; } = 1.0;

    [ObservableProperty]
    public partial bool SnapToClips { get; set; }

    [ObservableProperty]
    public partial double SnapThreshold { get; set; } = 0.25;
}