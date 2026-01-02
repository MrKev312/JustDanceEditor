using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Library", "View/Tools")]
public partial class LibraryToolViewModel : TimelineToolViewModel
{
    public ObservableCollection<LibraryItemViewModel> Items { get; } = [];

    public ObservableCollection<LibraryItemViewModel> Pictograms { get; } = [];
    public ObservableCollection<LibraryItemViewModel> HandMoves { get; } = [];
    public ObservableCollection<LibraryItemViewModel> FullBodyMoves { get; } = [];

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        PopulateItems(timeline);
        SubscribeToTimelineCollections(timeline);

        // Listen for clip property changes to update usage counts when IDs change
        // Ensure we only register once: unregister any previous registration first and only register when timeline is present
        if (timeline != null)
        {
            try
            {
                WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
            }
            catch { }

            WeakReferenceMessenger.Default.Register<LibraryToolViewModel, Messaging.ClipDataChangedMessage>(this, (r, m) => r.OnClipDataChanged(m));
        }
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        UnsubscribeFromTimelineCollections(timeline);
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
        Items.Clear();
        Pictograms.Clear();
        HandMoves.Clear();
        FullBodyMoves.Clear();
    }

    private void SubscribeToTimelineCollections(TimelineEditorViewModel? timeline)
    {
        if (timeline == null)
            return;

        // Tracks collection changes
        timeline.Tracks.CollectionChanged += Tracks_CollectionChanged;

        // Subscribe to existing track clip collections
        foreach (TrackViewModel track in timeline.Tracks)
        {
            track.Clips.CollectionChanged += Clips_CollectionChanged;
        }
    }

    private void UnsubscribeFromTimelineCollections(TimelineEditorViewModel? timeline)
    {
        if (timeline == null)
            return;

        try
        {
            timeline.Tracks.CollectionChanged -= Tracks_CollectionChanged;
        }
        catch { }

        foreach (TrackViewModel track in timeline.Tracks)
        {
            try
            {
                track.Clips.CollectionChanged -= Clips_CollectionChanged;
            }
            catch { }
        }
    }

    private void Tracks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // When tracks added, subscribe to their clip collections; when removed unsubscribe
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is TrackViewModel t)
                    t.Clips.CollectionChanged += Clips_CollectionChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TrackViewModel t)
                    t.Clips.CollectionChanged -= Clips_CollectionChanged;
            }
        }

        // Refresh counts and lists
        PopulateItems(ActiveTimeline);
    }

    private void Clips_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Whenever clips are added/removed, refresh usage counts
        UpdateUsageCounts();
    }

    private void OnClipDataChanged(Messaging.ClipDataChangedMessage msg)
    {
        // If MoveId or PictogramId changed, recompute counts
        if (msg.Value.PropertyName is "MoveId" or "PictogramId")
            UpdateUsageCounts();
    }

    private void UpdateUsageCounts()
    {
        if (ActiveTimeline == null)
            return;

        // Build fresh counts
        Dictionary<(ItemType type, string id), int> counts = new();
        foreach (TrackViewModel track in ActiveTimeline.Tracks)
        {
            foreach (ClipViewModel clip in track.Clips)
            {
                switch (clip)
                {
                    case MoveClipViewModel mc:
                        ItemType t = mc.IsFullBody ? ItemType.FullBodyMove : ItemType.HandMove;
                        (ItemType t, string MoveId) k = (t, mc.MoveId);
                        if (!counts.ContainsKey(k))
                            counts[k] = 0;
                        counts[k]++;
                        break;
                    case PictogramClipViewModel pc:
                        if (pc.PictogramId != null)
                        {
                            (ItemType Pictogram, string PictogramId) k2 = (ItemType.Pictogram, pc.PictogramId);
                            if (!counts.ContainsKey(k2))
                                counts[k2] = 0;
                            counts[k2]++;
                        }

                        break;
                }
            }
        }

        // Apply counts to items
        foreach (LibraryItemViewModel item in Items)
        {
            item.UsageCount = counts.TryGetValue((item.Type, item.Id), out var v) ? v : 0;
        }
    }
    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        // When tracks/clips change, refresh items to keep usage counts and pictogram availability up to date
        if (propertyName is (nameof(TimelineEditorViewModel.Tracks)) or null)
            PopulateItems(ActiveTimeline);
    }

    private void PopulateItems(TimelineEditorViewModel? timeline)
    {
        Items.Clear();
        Pictograms.Clear();
        HandMoves.Clear();
        FullBodyMoves.Clear();
        if (timeline == null)
            return;

        // Helper to compute counts
        Dictionary<(ItemType type, string id), int> counts = [];

        foreach (TrackViewModel track in timeline.Tracks)
        {
            foreach (ClipViewModel clip in track.Clips)
            {
                switch (clip)
                {
                    case MoveClipViewModel mc:
                        ItemType t = mc.IsFullBody ? ItemType.FullBodyMove : ItemType.HandMove;
                        (ItemType t, string MoveId) k = (t, mc.MoveId);
                        counts.TryGetValue(k, out int v);
                        counts[k] = v + 1;
                        break;
                    case PictogramClipViewModel pc:
                        (ItemType Pictogram, string PictogramId) k2 = (ItemType.Pictogram, pc.PictogramId);
                        counts.TryGetValue(k2, out int v2);
                        counts[k2] = v2 + 1;
                        break;
                }
            }
        }

        // 1) Hand moves
        foreach (string id in timeline.AvailableHandCoachMoves.OrderBy(x => x))
        {
            // Ensure we have a MoveDefinition for this move
            MoveDefinitionViewModel def = timeline.GetOrRegisterMove(id, isFullBody: false);

            LibraryItemViewModel item = new()
            {
                Name = id,
                Id = id,
                Type = ItemType.HandMove,
                Icon = new SolidColorBrush(def.Color),
                Definition = def,
                DefaultDuration = def.DefaultDuration,
                UsageCount = counts.TryGetValue((ItemType.HandMove, id), out var c1) ? c1 : 0
            };

            Items.Add(item);
            HandMoves.Add(item);
        }

        // 2) Full body moves
        foreach (string id in timeline.AvailableFullBodyCoachMoves.OrderBy(x => x))
        {
            // Ensure we have a MoveDefinition for this move
            MoveDefinitionViewModel def = timeline.GetOrRegisterMove(id, isFullBody: true);

            LibraryItemViewModel item = new()
            {
                Name = id,
                Id = id,
                Type = ItemType.FullBodyMove,
                Icon = new SolidColorBrush(def.Color),
                Definition = def,
                DefaultDuration = def.DefaultDuration,
                UsageCount = counts.TryGetValue((ItemType.FullBodyMove, id), out var c2) ? c2 : 0
            };

            Items.Add(item);
            FullBodyMoves.Add(item);
        }

        // 3) Pictograms
        var pictogramDir = Path.Combine(timeline.RootPath, "assets", "pictograms");
        if (Directory.Exists(pictogramDir))
        {
            IOrderedEnumerable<string?> files = Directory.GetFiles(pictogramDir)
                .Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);

            foreach (var id in files)
            {
                // Skip null IDs (shouldn't happen but be defensive)
                if (string.IsNullOrEmpty(id))
                    continue;

                // At this point id is guaranteed non-null
                string safeId = id!;
                string path = Path.Combine(pictogramDir, safeId + ".webp");
                IBrush brush = Brushes.LightGray;
                if (BitmapCache.TryGet(path, out Avalonia.Media.Imaging.Bitmap? bmp) && bmp != null)
                {
                    brush = new ImageBrush(bmp);
                }
                else
                {
                    // schedule load and refresh the view when loaded
                    BitmapCache.ScheduleLoad(path, () =>
                    {
                        // find the item and update its thumbnail
                        LibraryItemViewModel? item = Items.FirstOrDefault(i => i.Type == ItemType.Pictogram && string.Equals(i.Id, safeId, StringComparison.OrdinalIgnoreCase));
                        if (item != null)
                        {
                            if (BitmapCache.TryGet(path, out Bitmap? loaded) && loaded != null)
                                item.Thumbnail = loaded;
                        }
                    });
                }

                (ItemType Pictogram, string safeId) pictogramKey = (ItemType.Pictogram, safeId);
                int pictogramCount = counts.TryGetValue(pictogramKey, out var c3) ? c3 : 0;
                LibraryItemViewModel item = new()
                {
                    Name = safeId,
                    Id = safeId,
                    Type = ItemType.Pictogram,
                    Icon = brush,
                    Thumbnail = BitmapCache.TryGet(path, out Bitmap? pre) ? pre : null,
                    DefaultDuration = 24.0,
                    UsageCount = pictogramCount
                };

                Items.Add(item);
                Pictograms.Add(item);
            }
        }
    }
}