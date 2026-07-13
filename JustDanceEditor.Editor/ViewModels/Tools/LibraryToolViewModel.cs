using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Library", "View/Tools")]
public partial class LibraryToolViewModel : TimelineToolViewModel
{
    private readonly TimelineLibraryCatalogBuilder _catalog;
    internal IDialogService? Dialogs { get; }
    public ObservableCollection<LibraryItemViewModel> Items { get; } = [];

    public ObservableCollection<LibraryItemViewModel> Pictograms { get; } = [];
    public ObservableCollection<LibraryItemViewModel> HandMoves { get; } = [];
    public ObservableCollection<LibraryItemViewModel> FullBodyMoves { get; } = [];

    [ObservableProperty]
    public partial LibraryItemViewModel? SelectedPictogram { get; set; }

    [ObservableProperty]
    public partial LibraryItemViewModel? SelectedHandMove { get; set; }

    [ObservableProperty]
    public partial LibraryItemViewModel? SelectedFullBodyMove { get; set; }

    public LibraryToolViewModel(
        ITimelineContextService? timelineContext = null,
        IDialogService? dialogs = null,
        TimelineLibraryCatalogBuilder? catalog = null)
        : base(timelineContext)
    {
        Dialogs = dialogs;
        _catalog = catalog ?? new TimelineLibraryCatalogBuilder();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        PopulateItems(timeline);
        SubscribeToTimelineCollections(timeline);

        // Listen for clip property changes to update usage counts when IDs change
        // Ensure we only register once: unregister any previous registration first and only register when timeline is present
        if (timeline != null)
        {
            WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);

            WeakReferenceMessenger.Default.Register<LibraryToolViewModel, Messaging.ClipDataChangedMessage>(this, (r, m) => r.OnClipDataChanged(m));
        }
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        UnsubscribeFromTimelineCollections(timeline);
        WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
        ClearItems();
        SelectedPictogram = null;
        SelectedHandMove = null;
        SelectedFullBodyMove = null;
    }

    partial void OnSelectedPictogramChanged(LibraryItemViewModel? value)
    {
        if (value == null)
            return;

        SelectedHandMove = null;
        SelectedFullBodyMove = null;
        PublishSelection(value);
    }

    partial void OnSelectedHandMoveChanged(LibraryItemViewModel? value)
    {
        if (value == null)
            return;

        SelectedPictogram = null;
        SelectedFullBodyMove = null;
        PublishSelection(value);
    }

    partial void OnSelectedFullBodyMoveChanged(LibraryItemViewModel? value)
    {
        if (value == null)
            return;

        SelectedPictogram = null;
        SelectedHandMove = null;
        PublishSelection(value);
    }

    private void PublishSelection(LibraryItemViewModel item)
    {
        if (TimelineContext == null || ActiveTimeline == null)
            return;

        TimelineContext.SelectedObjects = [item];
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

        timeline.Tracks.CollectionChanged -= Tracks_CollectionChanged;

        foreach (TrackViewModel track in timeline.Tracks)
            track.Clips.CollectionChanged -= Clips_CollectionChanged;
    }

    private void Tracks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // When tracks added, subscribe to their clip collections; when removed unsubscribe
        if (e.NewItems != null)
        {
            foreach (object? item in e.NewItems)
            {
                if (item is TrackViewModel t)
                    t.Clips.CollectionChanged += Clips_CollectionChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
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
        Dictionary<(ItemType type, string id), int> counts = [];
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
            item.UsageCount = counts.TryGetValue((item.Type, item.Id), out int v) ? v : 0;
        }
    }
    protected override void OnTimelinePropertyChanged(string? propertyName)
    {
        // Refresh when tracks/clips change, or when the available-move lists change
        // (the latter fires when a new definition is registered via RegisterNewMoveDefinition).
        if (propertyName is null
            or nameof(TimelineEditorViewModel.Tracks)
            or nameof(TimelineEditorViewModel.AvailablePictograms)
            or nameof(TimelineEditorViewModel.AvailableHandCoachMoves)
            or nameof(TimelineEditorViewModel.AvailableFullBodyCoachMoves))
        {
            PopulateItems(ActiveTimeline);
        }
    }

    private void PopulateItems(TimelineEditorViewModel? timeline)
    {
        ClearItems();
        if (timeline == null)
            return;

        foreach (LibraryCatalogEntry entry in _catalog.Build(timeline))
        {
            Bitmap? thumbnail = null;
            IBrush icon = entry.MoveDefinition != null
                ? new SolidColorBrush(entry.MoveDefinition.Color)
                : entry.HasAsset ? Brushes.LightGray : Brushes.Red;
            if (entry.AssetPath != null
                && entry.HasAsset
                && ImageBitmapCache.TryGet(entry.AssetPath, out Bitmap? cached)
                && cached != null)
            {
                thumbnail = cached;
                icon = new ImageBrush(cached);
            }

            LibraryItemViewModel item = new()
            {
                Name = entry.Id,
                Id = entry.Id,
                Type = entry.Type,
                Icon = icon,
                Thumbnail = thumbnail,
                Definition = entry.MoveDefinition,
                DefaultDuration = entry.MoveDefinition?.DefaultDuration ?? 24,
                UsageCount = entry.UsageCount,
                HasAsset = entry.HasAsset
            };
            Items.Add(item);

            GetTypedCollection(entry.Type).Add(item);
            if (entry.AssetPath != null && entry.HasAsset && thumbnail == null)
            {
                string assetPath = entry.AssetPath;
                ImageBitmapCache.ScheduleLoad(assetPath, () =>
                {
                    if (Items.Contains(item)
                        && ImageBitmapCache.TryGet(assetPath, out Bitmap? loaded)
                        && loaded != null)
                    {
                        item.Thumbnail = loaded;
                        item.Icon = new ImageBrush(loaded);
                    }
                });
            }
        }
    }

    private ObservableCollection<LibraryItemViewModel> GetTypedCollection(ItemType type)
        => type switch
        {
            ItemType.Pictogram => Pictograms,
            ItemType.HandMove => HandMoves,
            ItemType.FullBodyMove => FullBodyMoves,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

    private void ClearItems()
    {
        foreach (LibraryItemViewModel item in Items)
            item.Dispose();
        Items.Clear();
        Pictograms.Clear();
        HandMoves.Clear();
        FullBodyMoves.Clear();
    }
}