using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

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

    private readonly List<LyricLineViewModel> _allLines = [];

    // Lyrics track and per-clip handlers
    private TrackViewModel? _lyricsTrack;
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _lyricsClipHandlers = [];

    // Coalesces rapid bursts of ClipDataChangedMessages into a single rebuild
    private bool _rebuildPending;

    public LyricPreviewViewModel(ITimelineContextService? timelineContext = null)
        : base(timelineContext)
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
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);

        // Register for per-clip data changes so we can react to edits in the property grid
        WeakReferenceMessenger.Default.Register<LyricPreviewViewModel, Messaging.ClipDataChangedMessage>(this, (r, m) =>
        {
            if (m.Source is KaraokeClipViewModel && (m.PropertyName == nameof(KaraokeClipViewModel.Lyrics) || m.PropertyName == nameof(KaraokeClipViewModel.IsEndOfLine) || m.PropertyName == nameof(ClipViewModel.StartBeat)))
            {
                if (r._rebuildPending)
                    return;
                r._rebuildPending = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    r._rebuildPending = false;
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
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
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
            foreach (object? item in e.NewItems)
            {
                if (item is KaraokeClipViewModel clip)
                    AddLyricsClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
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

        void handler(object? s, PropertyChangedEventArgs e)
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
        }

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

        TrackViewModel? lyricsTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Lyrics);
        if (lyricsTrack == null)
            return;

        // Subscribe to changes on the lyrics track so we update when clips reorder
        SubscribeLyricsTrack(lyricsTrack);

        List<ClipViewModel> currentLineClips = [];
        // iterate clips sorted by StartBeat so BuildLines reflects current timing order
        foreach (KaraokeClipViewModel clip in lyricsTrack.Clips.OfType<KaraokeClipViewModel>().OrderBy(c => c.StartBeat))
        {
            currentLineClips.Add(clip);
            if (clip.IsEndOfLine)
            {
                _allLines.Add(new LyricLineViewModel([.. currentLineClips]));
                currentLineClips.Clear();
            }
        }

        if (currentLineClips.Count > 0)
        {
            _allLines.Add(new LyricLineViewModel([.. currentLineClips]));
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

        TimelineEditorViewModel timeline = ActiveTimeline;
        double currentSeconds = timeline.GetPlaybackSecondsAtBeatLabel(CurrentBeat);

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
            double startSeconds = timeline.GetPlaybackSecondsAtBeatLabel(targetLine.StartBeat);

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