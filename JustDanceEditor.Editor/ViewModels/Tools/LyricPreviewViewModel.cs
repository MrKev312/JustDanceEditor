using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Lyrics Preview", "View/Preview")]
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

        // Ensure any previous registration is removed (idempotent) before registering our handler
        try { WeakReferenceMessenger.Default.Unregister<JustDanceEditor.Editor.Messaging.ClipDataChangedMessage>(this); } catch { }

        // Register for per-clip data changes so we can react to edits in the property grid
        WeakReferenceMessenger.Default.Register<LyricPreviewViewModel, JustDanceEditor.Editor.Messaging.ClipDataChangedMessage>(this, (r, m) =>
        {
            if (m.Source is KaraokeClipViewModel && (m.PropertyName == nameof(KaraokeClipViewModel.Lyrics) || m.PropertyName == nameof(KaraokeClipViewModel.IsEndOfLine) || m.PropertyName == nameof(ClipViewModel.StartBeat)))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    r.BuildLines(r.ActiveTimeline);
                    r.RefreshLyrics();
                });
            }
        });

        BuildLines(timeline);
        RefreshLyrics();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        // Unregister messenger and clip handlers
        WeakReferenceMessenger.Default.Unregister<JustDanceEditor.Editor.Messaging.ClipDataChangedMessage>(this);
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
        foreach (KaraokeClipViewModel clip in _lyricsTrack.Clips.OfType<KaraokeClipViewModel>())
            AddLyricsClipHandler(clip);
    }

    private void LyricsClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new clips
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is KaraokeClipViewModel clip)
                    AddLyricsClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is KaraokeClipViewModel clip)
                    RemoveLyricsClipHandler(clip);
            }
        }

        // Rebuild lines and refresh
        BuildLines(ActiveTimeline);
        RefreshLyrics();
    }

    private void AddLyricsClipHandler(KaraokeClipViewModel clip)
    {
        if (clip == null || _lyricsClipHandlers.ContainsKey(clip))
            return;
        PropertyChangedEventHandler handler = (s, e) =>
        {
            if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)) or (nameof(KaraokeClipViewModel.Lyrics)) or (nameof(KaraokeClipViewModel.IsEndOfLine)))
            {
                // When clip timing or lyrics properties change, rebuild and refresh so ordering reflects StartBeat and line grouping
                BuildLines(ActiveTimeline);
                RefreshLyrics();
            }
            else if (e.PropertyName == nameof(ClipViewModel.BackgroundColor))
            {
                // When the background color of any lyrics clip changes, update the target color
                TargetColor = clip.BackgroundColor;
            }
        };
        clip.PropertyChanged += handler;
        _lyricsClipHandlers[clip] = handler;
    }

    private void RemoveLyricsClipHandler(KaraokeClipViewModel clip)
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

        List<ClipViewModel> currentLineClips = new();
        // iterate clips sorted by StartBeat so BuildLines reflects current timing order
        foreach (KaraokeClipViewModel clip in pictoTrack.Clips.OfType<KaraokeClipViewModel>().OrderBy(c => c.StartBeat))
        {
            currentLineClips.Add(clip);
            if (clip.IsEndOfLine)
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
    public string FullText => string.Join("", Clips.Select(c => (c as KaraokeClipViewModel)?.Lyrics ?? ""));
}