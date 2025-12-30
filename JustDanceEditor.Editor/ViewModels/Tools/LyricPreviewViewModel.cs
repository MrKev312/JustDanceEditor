using CommunityToolkit.Mvvm.ComponentModel;
using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Lyrics Preview", "General")]
public partial class LyricPreviewViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    private LyricLineViewModel? _currentLine;

    [ObservableProperty]
    private LyricLineViewModel? _nextLine;

    [ObservableProperty]
    private double _currentBeat;

    [ObservableProperty]
    private Avalonia.Media.Color _targetColor = Avalonia.Media.Colors.SkyBlue;

    private List<LyricLineViewModel> _allLines = [];

    public LyricPreviewViewModel()
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
            
            if (Avalonia.Media.Color.TryParse(value.LyricsColor, out var c))
            {
                TargetColor = c;
            }
        }

        _lastTimeline = value;
        BuildLines();
        RefreshLyrics();
        OnPropertyChanged(nameof(ActiveTimeline));
    }

    private TimelineEditorViewModel? _lastTimeline;

    private void Playback_TimeChanged(object? sender, EventArgs e)
    {
        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
        RefreshLyrics();
    }

    private void BuildLines()
    {
        _allLines.Clear();
        if (ActiveTimeline == null)
            return;

        var pictoTrack = ActiveTimeline.Tracks.FirstOrDefault(t => t.Title == "Lyrics");
        if (pictoTrack == null)
            return;

        var currentLineClips = new List<ClipViewModel>();
        foreach (var clip in pictoTrack.Clips)
        {
            currentLineClips.Add(clip);
            if (clip.RawClip is KaraokeClip k && k.IsEndOfLine)
            {
                _allLines.Add(new LyricLineViewModel(currentLineClips.ToList()));
                currentLineClips.Clear();
            }
        }
        if (currentLineClips.Count > 0)
        {
            _allLines.Add(new LyricLineViewModel(currentLineClips.ToList()));
        }
    }

    private void RefreshLyrics()
    {
        if (_allLines.Count == 0 || ActiveTimeline == null)
        {
            CurrentLine = null;
            NextLine = null;
            return;
        }

        var ts = ActiveTimeline.TimelineStructure;
        double currentSeconds = ts.GetSecondsAtBeat(CurrentBeat);

        // Find the index of the first line that hasn't finished yet
        int idx = _allLines.FindIndex(l => l.EndBeat > CurrentBeat);

        if (idx == -1)
        {
            CurrentLine = null;
            NextLine = null;
        }
        else
        {
            var targetLine = _allLines[idx];
            double startSeconds = ts.GetSecondsAtBeat(targetLine.StartBeat);

            // If we are currently singing the line OR it starts within the next 2 seconds
            if (CurrentBeat >= targetLine.StartBeat || (startSeconds - currentSeconds) <= 2.0)
            {
                CurrentLine = targetLine;
                NextLine = (idx + 1 < _allLines.Count) ? _allLines[idx + 1] : null;
            }
            else
            {
                // It's still a long time away ( > 2s gap), so keep the top empty
                // and show this upcoming line in the "Up Next" slot.
                CurrentLine = null;
                NextLine = targetLine;
            }
        }
    }
}

public class LyricLineViewModel(List<ClipViewModel> clips)
{
    public List<ClipViewModel> Clips { get; } = clips;
    public double StartBeat => Clips.FirstOrDefault()?.StartBeat ?? 0;
    public double EndBeat => Clips.LastOrDefault() is ClipViewModel last ? last.StartBeat + last.DurationBeats : 0;
    public string FullText => string.Join("", Clips.Select(c => (c.RawClip as KaraokeClip)?.Lyrics ?? ""));
}
