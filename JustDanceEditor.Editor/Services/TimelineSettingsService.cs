using System.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public class TimelineSettingsService : INotifyPropertyChanged
{
    private static readonly TimelineSettingsService _instance = new TimelineSettingsService();
    public static TimelineSettingsService Instance => _instance;

    private TimelineSettingsService() { }

    private bool _snapToGrid;
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set { if (_snapToGrid == value) return; _snapToGrid = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToGrid))); }
    }

    private bool _snapToCurrentTimeMarker;
    public bool SnapToCurrentTimeMarker
    {
        get => _snapToCurrentTimeMarker;
        set { if (_snapToCurrentTimeMarker == value) return; _snapToCurrentTimeMarker = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToCurrentTimeMarker))); }
    }

    private double _snapGridSize = 1.0;
    public double SnapGridSize
    {
        get => _snapGridSize;
        set { if (System.Math.Abs(_snapGridSize - value) < 1e-9) return; _snapGridSize = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapGridSize))); }
    }

    private double _snapThreshold = 0.25;
    public double SnapThreshold
    {
        get => _snapThreshold;
        set { if (System.Math.Abs(_snapThreshold - value) < 1e-9) return; _snapThreshold = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapThreshold))); }
    }

    private bool _snapToClips;
    public bool SnapToClips
    {
        get => _snapToClips;
        set { if (_snapToClips == value) return; _snapToClips = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToClips))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
