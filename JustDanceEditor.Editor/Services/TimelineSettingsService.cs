using System.ComponentModel;

namespace JustDanceEditor.Editor.Services;

public class TimelineSettingsService : INotifyPropertyChanged
{
    public static TimelineSettingsService Instance { get; } = new TimelineSettingsService();

    private TimelineSettingsService() { }

    public bool SnapToGrid { get; set { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToGrid))); } }

    public bool SnapToCurrentTimeMarker { get; set { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToCurrentTimeMarker))); } }

    public double SnapGridSize { get; set { if (System.Math.Abs(field - value) < 1e-9) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapGridSize))); } } = 1.0;

    public double SnapThreshold { get; set { if (System.Math.Abs(field - value) < 1e-9) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapThreshold))); } } = 0.25;

    public bool SnapToClips { get; set { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToClips))); } }

    public event PropertyChangedEventHandler? PropertyChanged;
}
