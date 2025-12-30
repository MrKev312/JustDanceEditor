using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Pictogram Preview", "General")]
public partial class PictogramPreviewViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    private ObservableCollection<ClipViewModel> _visiblePictograms = [];

    [ObservableProperty]
    private double _currentBeat;

    public PictogramPreviewViewModel()
    {
    }

    protected override void HandleActiveTimelineChanged(TimelineEditorViewModel? value)
    {
        if (_lastTimeline != null)
        {
            _lastTimeline.Playback.TimeChanged -= Playback_TimeChanged;
        }

        if (value != null)
        {
            value.Playback.TimeChanged += Playback_TimeChanged;
        }

        _lastTimeline = value;
        RefreshVisiblePictograms();
        OnPropertyChanged(nameof(ActiveTimeline));
    }

    private TimelineEditorViewModel? _lastTimeline;

    private void Playback_TimeChanged(object? sender, EventArgs e)
    {
        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
        RefreshVisiblePictograms();
    }

    private void RefreshVisiblePictograms()
    {
        if (ActiveTimeline == null)
        {
            VisiblePictograms.Clear();
            return;
        }

        var pictoTrack = ActiveTimeline.Tracks.FirstOrDefault(t => t.Title == "Pictograms");
        if (pictoTrack == null)
        {
            VisiblePictograms.Clear();
            return;
        }

        // Only include pictograms within a reasonable look-ahead (e.g., 20 beats)
        double lookAhead = 20;
        var upcoming = pictoTrack.Clips
            .Where(c => c.StartBeat >= CurrentBeat - 5 && c.StartBeat <= CurrentBeat + lookAhead)
            .ToList();

        // Simple update logic: compare and sync
        // In a more complex app we'd use a better diffing strategy
        if (!VisiblePictograms.SequenceEqual(upcoming))
        {
            VisiblePictograms = new ObservableCollection<ClipViewModel>(upcoming);
        }
    }
}
