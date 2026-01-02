using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Pictogram Preview", "View/Preview")]
public partial class PictogramPreviewViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<ClipViewModel> VisiblePictograms { get; set; } = [];

    [ObservableProperty]
    public partial double CurrentBeat { get; set; }

    // Track subscriptions
    private TrackViewModel? _pictoTrack;
    private readonly Dictionary<ClipViewModel, PropertyChangedEventHandler> _clipHandlers = [];

    public PictogramPreviewViewModel()
    {
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        // Start the pictogram preview at the current timeline time
        if (timeline != null)
            CurrentBeat = timeline.CurrentBeat;

        RefreshVisiblePictograms();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        UnsubscribePictoTrack();
        VisiblePictograms.Clear();
    }

    protected override void OnTimeChanged()
    {
        CurrentBeat = ActiveTimeline?.CurrentBeat ?? 0;
        RefreshVisiblePictograms();
    }

    private void UnsubscribePictoTrack()
    {
        if (_pictoTrack == null)
            return;
        _pictoTrack.Clips.CollectionChanged -= PictoClips_CollectionChanged;
        foreach (KeyValuePair<ClipViewModel, PropertyChangedEventHandler> kv in _clipHandlers.ToList())
        {
            kv.Key.PropertyChanged -= kv.Value;
            _clipHandlers.Remove(kv.Key);
        }

        _pictoTrack = null;
    }

    private void SubscribePictoTrack(TrackViewModel track)
    {
        UnsubscribePictoTrack();
        _pictoTrack = track;
        _pictoTrack.Clips.CollectionChanged += PictoClips_CollectionChanged;
        foreach (ClipViewModel clip in _pictoTrack.Clips)
            AddClipHandler(clip);
    }

    private void PictoClips_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe new clips
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ClipViewModel clip)
                    AddClipHandler(clip);
            }
        }

        // Unsubscribe removed clips
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ClipViewModel clip)
                    RemoveClipHandler(clip);
            }
        }

        // Refresh visible list
        RefreshVisiblePictograms();
    }

    private void AddClipHandler(ClipViewModel clip)
    {
        if (clip == null || _clipHandlers.ContainsKey(clip))
            return;

        void handler(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is (nameof(ClipViewModel.StartBeat)) or (nameof(ClipViewModel.DurationBeats)))
            {
                // Ensure UI update on UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(RefreshVisiblePictograms);
            }
        }

        clip.PropertyChanged += handler;
        _clipHandlers[clip] = handler;
    }

    private void RemoveClipHandler(ClipViewModel clip)
    {
        if (clip == null)
            return;
        if (_clipHandlers.TryGetValue(clip, out PropertyChangedEventHandler? handler))
        {
            clip.PropertyChanged -= handler;
            _clipHandlers.Remove(clip);
        }
    }

    private void RefreshVisiblePictograms()
    {
        if (ActiveTimeline == null)
        {
            VisiblePictograms.Clear();
            return;
        }

        TrackViewModel? pictoTrack = ActiveTimeline.Tracks.FirstOrDefault(t => t.Title == "Pictograms");
        if (pictoTrack == null)
        {
            VisiblePictograms.Clear();
            return;
        }

        // Ensure we're subscribed to the pictogram track
        SubscribePictoTrack(pictoTrack);

        // Only include pictograms within a reasonable look-ahead (e.g., 20 beats)
        double lookAhead = 20;
        double current = ActiveTimeline.CurrentBeat;
        List<ClipViewModel> upcoming = [.. pictoTrack.Clips.Where(c => c.StartBeat >= current - 5 && c.StartBeat <= current + lookAhead)];

        // Simple update logic: compare and sync
        if (!VisiblePictograms.SequenceEqual(upcoming))
        {
            VisiblePictograms = new ObservableCollection<ClipViewModel>(upcoming);
        }
    }
}