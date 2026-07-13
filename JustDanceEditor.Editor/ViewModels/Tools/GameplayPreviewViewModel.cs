using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Gameplay Preview", "View/Preview")]
public partial class GameplayPreviewViewModel : VideoToolViewModel
{
    private const double HudFadeSeconds = 1.0;
    private const double BeatEpsilon = 0.0001;

    [ObservableProperty]
    public partial LyricLineViewModel? CurrentLine { get; set; }

    [ObservableProperty]
    public partial LyricLineViewModel? NextLine { get; set; }

    [ObservableProperty]
    public partial double CurrentBeat { get; set; }

    [ObservableProperty]
    public partial Color TargetColor { get; set; } = Colors.SkyBlue;

    [ObservableProperty]
    public partial bool IsHudVisible { get; set; }

    [ObservableProperty]
    public partial double HudOpacity { get; set; } = 1.0;

    public MotionRecordingScoreHudService ScoreHud { get; }

    private readonly List<LyricLineViewModel> _allLines = [];
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _lyricsClipHandlers = [];
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _hideHudClipHandlers = [];
    private TrackViewModel? _lyricsTrack;
    private TrackViewModel? _hideHudTrack;
    private int _lineIndex = -1;
    private bool _rebuildPending;

    public GameplayPreviewViewModel(
        MotionRecordingScoreHudService? scoreHud = null,
        ITimelineContextService? timelineContext = null)
        : base(timelineContext)
    {
        ScoreHud = scoreHud ?? new MotionRecordingScoreHudService();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        base.OnTimelineAttached(timeline);

        if (timeline != null)
        {
            TargetColor = ClipViewModel.ParseRgbaHex(timeline.LyricsColor);
            CurrentBeat = timeline.CurrentBeat;
        }

        SubscribeHideHudTrack(timeline);
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
        WeakReferenceMessenger.Default.Register<GameplayPreviewViewModel, Messaging.ClipDataChangedMessage>(this, (recipient, message) =>
        {
            if (message.Source is not KaraokeClipViewModel)
                return;

            if (message.PropertyName is not (nameof(KaraokeClipViewModel.Lyrics) or nameof(KaraokeClipViewModel.IsEndOfLine) or nameof(ClipViewModel.StartBeat)))
                return;

            if (recipient._rebuildPending)
                return;

            recipient._rebuildPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                recipient._rebuildPending = false;
                recipient.BuildLines(recipient.ActiveTimeline);
                recipient.RefreshLyrics();
            });
        });

        BuildLines(timeline);
        RefreshHudVisibility();
        RefreshLyrics();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
        UnsubscribeLyricsTrack();
        UnsubscribeHideHudTrack();
        _allLines.Clear();
        _lineIndex = -1;
        CurrentLine = null;
        NextLine = null;
        CurrentBeat = 0;
        IsHudVisible = false;
        HudOpacity = 0;

        base.OnTimelineDetached(timeline);
    }

    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        base.OnTimelinePropertyChanged(propertyName);

        if (propertyName == nameof(TimelineEditorViewModel.LyricsColor) && ActiveTimeline != null)
            TargetColor = ClipViewModel.ParseRgbaHex(ActiveTimeline.LyricsColor);
    }

    protected override void OnTimeChanged()
    {
        base.OnTimeChanged();

        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
        RefreshHudVisibility();
        RefreshLyrics();
    }

    private void UnsubscribeLyricsTrack()
    {
        if (_lyricsTrack != null)
            _lyricsTrack.Clips.CollectionChanged -= LyricsClips_CollectionChanged;

        foreach ((ClipViewModel clip, PropertyChangedEventHandler handler) in _lyricsClipHandlers.ToList())
        {
            clip.PropertyChanged -= handler;
            _lyricsClipHandlers.Remove(clip);
        }

        _lyricsTrack = null;
    }

    private void UnsubscribeHideHudTrack()
    {
        if (_hideHudTrack != null)
            _hideHudTrack.Clips.CollectionChanged -= HideHudClips_CollectionChanged;

        foreach ((ClipViewModel clip, PropertyChangedEventHandler handler) in _hideHudClipHandlers.ToList())
        {
            clip.PropertyChanged -= handler;
            _hideHudClipHandlers.Remove(clip);
        }

        _hideHudTrack = null;
    }

    private void SubscribeLyricsTrack(TrackViewModel track)
    {
        UnsubscribeLyricsTrack();
        _lyricsTrack = track;
        _lyricsTrack.Clips.CollectionChanged += LyricsClips_CollectionChanged;

        foreach (KaraokeClipViewModel clip in _lyricsTrack.Clips.OfType<KaraokeClipViewModel>())
            AddLyricsClipHandler(clip);
    }

    private void SubscribeHideHudTrack(TimelineEditorViewModel? timeline)
    {
        UnsubscribeHideHudTrack();
        _hideHudTrack = timeline?.Tracks.FirstOrDefault(t => t.TrackType == TrackType.HideHud);
        if (_hideHudTrack == null)
            return;

        _hideHudTrack.Clips.CollectionChanged += HideHudClips_CollectionChanged;
        foreach (HideUserInterfaceClipViewModel clip in _hideHudTrack.Clips.OfType<HideUserInterfaceClipViewModel>())
            AddHideHudClipHandler(clip);
    }

    private void LyricsClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
                if (item is KaraokeClipViewModel clip)
                    AddLyricsClipHandler(clip);
        }

        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
                if (item is KaraokeClipViewModel clip)
                    RemoveLyricsClipHandler(clip);
        }

        BuildLines(ActiveTimeline);
        RefreshLyrics();
    }

    private void HideHudClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
                if (item is HideUserInterfaceClipViewModel clip)
                    AddHideHudClipHandler(clip);
        }

        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
                if (item is HideUserInterfaceClipViewModel clip)
                    RemoveHideHudClipHandler(clip);
        }

        RefreshHudVisibility();
    }

    private void AddLyricsClipHandler(KaraokeClipViewModel clip)
    {
        if (_lyricsClipHandlers.ContainsKey(clip))
            return;

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ClipViewModel.StartBeat) or nameof(ClipViewModel.DurationBeats) or nameof(KaraokeClipViewModel.Lyrics) or nameof(KaraokeClipViewModel.IsEndOfLine))
            {
                BuildLines(ActiveTimeline);
                RefreshLyrics();
            }
            else if (e.PropertyName == nameof(ClipViewModel.BackgroundColor))
            {
                TargetColor = clip.BackgroundColor;
            }
        }

        clip.PropertyChanged += Handler;
        _lyricsClipHandlers[clip] = Handler;
    }

    private void AddHideHudClipHandler(HideUserInterfaceClipViewModel clip)
    {
        if (_hideHudClipHandlers.ContainsKey(clip))
            return;

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ClipViewModel.StartBeat) or nameof(ClipViewModel.DurationBeats))
                RefreshHudVisibility();
        }

        clip.PropertyChanged += Handler;
        _hideHudClipHandlers[clip] = Handler;
    }

    private void RemoveLyricsClipHandler(KaraokeClipViewModel clip)
    {
        if (!_lyricsClipHandlers.Remove(clip, out PropertyChangedEventHandler? handler))
            return;

        clip.PropertyChanged -= handler;
    }

    private void RemoveHideHudClipHandler(HideUserInterfaceClipViewModel clip)
    {
        if (!_hideHudClipHandlers.Remove(clip, out PropertyChangedEventHandler? handler))
            return;

        clip.PropertyChanged -= handler;
    }

    private void BuildLines(TimelineEditorViewModel? timeline)
    {
        _allLines.Clear();
        _lineIndex = -1;
        if (timeline == null)
            return;

        TrackViewModel? lyricsTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Lyrics);
        if (lyricsTrack == null)
            return;

        SubscribeLyricsTrack(lyricsTrack);

        List<ClipViewModel> currentLineClips = [];
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
            _allLines.Add(new LyricLineViewModel([.. currentLineClips]));
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
        int lineIndex = GetActiveLineIndex(CurrentBeat);

        if (lineIndex == -1)
        {
            _lineIndex = -1;
            CurrentLine = null;
            NextLine = null;
            return;
        }

        LyricLineViewModel targetLine = _allLines[lineIndex];
        double startSeconds = timeline.GetPlaybackSecondsAtBeatLabel(targetLine.StartBeat);

        _lineIndex = lineIndex;
        if (CurrentBeat >= targetLine.StartBeat || startSeconds - currentSeconds <= 2.0)
        {
            CurrentLine = targetLine;
            NextLine = lineIndex + 1 < _allLines.Count ? _allLines[lineIndex + 1] : null;
            return;
        }

        CurrentLine = null;
        NextLine = targetLine;
    }

    private void RefreshHudVisibility()
    {
        TimelineEditorViewModel? timeline = ActiveTimeline;
        if (timeline == null)
        {
            IsHudVisible = false;
            HudOpacity = 0;
            return;
        }

        double opacity = GetHudOpacity(timeline, CurrentBeat);
        HudOpacity = opacity;
        IsHudVisible = opacity > 0.001;
    }

    private double GetHudOpacity(TimelineEditorViewModel timeline, double beat)
    {
        TrackViewModel? hideHudTrack = _hideHudTrack;
        if (hideHudTrack == null)
            return 1.0;

        double opacity = 1.0;
        foreach (HideUserInterfaceClipViewModel clip in hideHudTrack.Clips.OfType<HideUserInterfaceClipViewModel>())
        {
            if (clip.RawClip is not HideUserInterfaceClip { IsActive: true })
                continue;

            double clipStart = clip.StartBeat;
            double clipEnd = clip.StartBeat + clip.DurationBeats;
            if (clipEnd <= clipStart)
                continue;

            bool reachesTimelineEnd = clipEnd >= timeline.TimelineStructure.EndBeat - BeatEpsilon;
            bool isInsideClip = beat >= clipStart && (beat < clipEnd || (reachesTimelineEnd && beat <= clipEnd));
            if (!isInsideClip)
                continue;

            opacity = Math.Min(opacity, GetClipHudOpacity(timeline, clipStart, clipEnd, beat));
            if (opacity <= 0)
                return 0;
        }

        return opacity;
    }

    private static double GetClipHudOpacity(TimelineEditorViewModel timeline, double clipStart, double clipEnd, double beat)
    {
        TimelineStructureDocument timelineStructure = timeline.TimelineStructure;
        bool startsAtTimelineStart = clipStart <= timelineStructure.StartBeat + BeatEpsilon;
        bool endsAtTimelineEnd = clipEnd >= timelineStructure.EndBeat - BeatEpsilon;

        double currentSeconds = timeline.GetPlaybackSecondsAtBeatLabel(beat);
        double startSeconds = timeline.GetPlaybackSecondsAtBeatLabel(clipStart);
        double endSeconds = timeline.GetPlaybackSecondsAtBeatLabel(clipEnd);

        double opacity = 0.0;
        if (!startsAtTimelineStart)
        {
            double fadeOutProgress = Math.Clamp((currentSeconds - startSeconds) / HudFadeSeconds, 0, 1);
            opacity = Math.Max(opacity, 1.0 - fadeOutProgress);
        }

        if (!endsAtTimelineEnd)
        {
            double fadeInProgress = Math.Clamp((currentSeconds - (endSeconds - HudFadeSeconds)) / HudFadeSeconds, 0, 1);
            opacity = Math.Max(opacity, fadeInProgress);
        }

        return Math.Clamp(opacity, 0, 1);
    }

    private int GetActiveLineIndex(double currentBeat)
    {
        if (_lineIndex >= 0
            && _lineIndex < _allLines.Count
            && _allLines[_lineIndex].EndBeat > currentBeat
            && (_lineIndex == 0 || _allLines[_lineIndex - 1].EndBeat <= currentBeat))
        {
            return _lineIndex;
        }

        int low = 0;
        int high = _allLines.Count;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (_allLines[mid].EndBeat <= currentBeat)
                low = mid + 1;
            else
                high = mid;
        }

        return low < _allLines.Count ? low : -1;
    }
}