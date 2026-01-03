using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TrackViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Track";

    [ObservableProperty]
    public partial double Height { get; set; } = 50;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial Color TrackColor { get; set; } = Colors.Gray;
    public ObservableCollection<ClipViewModel> Clips { get; } = [];
}