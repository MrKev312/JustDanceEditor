using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[ToolWindow("Lyrics Preview", "View/Preview")]
public partial class LyricPreviewViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    public partial LyricLineViewModel? CurrentLine { get; set; }

    [ObservableProperty]
    public partial LyricLineViewModel? NextLine { get; set; }

    [ObservableProperty]
    public partial double CurrentBeat { get; set; }

    [ObservableProperty]
    public partial Color TargetColor { get; set; } = Colors.SkyBlue;

    private List<LyricLineViewModel> _allLines = [];

    // Lyrics track and per-clip handlers
    private TrackViewModel? _lyricsTrack;
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _lyricsClipHandlers = [];

    public LyricPreviewViewModel()
    {
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        if (timeline != null)
        {
            // Parse lyrics color from RGBA format
            TargetColor = ClipViewModel.ParseRgbaHex(timeline.LyricsColor);

            // Ensure preview starts at current playback head
            CurrentBeat = timeline.CurrentBeat;
        }

        BuildLines(timeline);
        RefreshLyrics();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        UnsubscribeLyricsTrack();
        _allLines.Clear();
    }

    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(TimelineEditorViewModel.LyricsColor))
        {
            if (ActiveTimeline != null)
            {
                TargetColor = ClipViewModel.ParseRgbaHex(ActiveTimeline.LyricsColor);
            }
        }
    }

    protected override void OnTimeChanged()
    {
        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
        RefreshLyrics();
    }

    private void UnsubscribeLyricsTrack()
    {
        if (_lyricsTrack == null)
            return;
        _lyricsTrack.Clips.CollectionChanged -= LyricsClips_CollectionChanged;
        foreach (KeyValuePair<ClipViewModel, PropertyChangedEventHandler> kv in _lyricsClipHandlers.ToList())
        {
            kv.Key.PropertyChanged -= kv.Value;
            _lyricsClipHandlers.Remove(kv.Key);
        }

        _lyricsTrack = null;
    }

    private void SubscribeLyricsTrack(TrackViewModel track)
    {
        UnsubscribeLyricsTrack();
        _lyricsTrack = track;
        _lyricsTrack.Clips.CollectionChanged += LyricsClips_CollectionChanged;
        foreach (ClipViewModel clip in _lyricsTrack.Clips)
            AddLyricsClipHandler(clip);
    }

    private void LyricsClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new clips
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ClipViewModel clip)
                    AddLyricsClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ClipViewModel clip)
                    RemoveLyricsClipHandler(clip);
            }
        }

        // Rebuild lines and refresh
        BuildLines(ActiveTimeline);
        RefreshLyrics();
    }

    private void AddLyricsClipHandler(ClipViewModel clip)
    {
        if (clip == null || _lyricsClipHandlers.ContainsKey(clip))
            return;
        PropertyChangedEventHandler handler = (s, e) =>
        {
            if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)) or (nameof(ClipViewModel.Name)))
            {
                // When clip timing changes, rebuild and refresh so ordering reflects StartBeat
                BuildLines(ActiveTimeline);
                RefreshLyrics();
            }
            else if (e.PropertyName == nameof(ClipViewModel.BackgroundColor))
            {
                // When the background color of any lyrics clip changes, update the target color
                if (clip.RawClip is KaraokeClip)
                {
                    TargetColor = clip.BackgroundColor;
                }
            }
        };
        clip.PropertyChanged += handler;
        _lyricsClipHandlers[clip] = handler;
    }

    private void RemoveLyricsClipHandler(ClipViewModel clip)
    {
        if (clip == null)
            return;
        if (_lyricsClipHandlers.TryGetValue(clip, out PropertyChangedEventHandler? handler))
        {
            clip.PropertyChanged -= handler;
            _lyricsClipHandlers.Remove(clip);
        }
    }

    private void BuildLines(TimelineEditorViewModel? timeline)
    {
        _allLines.Clear();
        if (timeline == null)
            return;

        TrackViewModel? pictoTrack = timeline.Tracks.FirstOrDefault(t => t.Title == "Lyrics");
        if (pictoTrack == null)
            return;

        // Subscribe to changes on the lyrics track so we update when clips reorder
        SubscribeLyricsTrack(pictoTrack);

        var currentLineClips = new List<ClipViewModel>();
        // iterate clips sorted by StartBeat so BuildLines reflects current timing order
        foreach (ClipViewModel? clip in pictoTrack.Clips.OrderBy(c => c.StartBeat))
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

        TimelineStructureDocument ts = ActiveTimeline.TimelineStructure;
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
            LyricLineViewModel targetLine = _allLines[idx];
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