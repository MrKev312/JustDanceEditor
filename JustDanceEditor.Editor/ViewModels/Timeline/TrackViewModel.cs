using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class TrackViewModel : ViewModelBase
{
    [ObservableProperty] private string _title = "Track";
    [ObservableProperty] private double _height = 50;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private Color _trackColor = Colors.Gray;

    public ObservableCollection<ClipViewModel> Clips { get; } = [];
}