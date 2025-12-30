using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JustDanceEditor.Editor.ViewModels.Timeline;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimelineTrackPanel : Panel
{
    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<ClipViewModel>> ClipsProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IEnumerable<ClipViewModel>>(nameof(Clips));

    public IEnumerable<ClipViewModel> Clips
    {
        get => GetValue(ClipsProperty);
        set => SetValue(ClipsProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<double> MaxBeatProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, double>(nameof(MaxBeat), 0.0);

    public double MaxBeat
    {
        get => GetValue(MaxBeatProperty);
        set => SetValue(MaxBeatProperty, value);
    }

    private readonly Dictionary<ClipViewModel, ClipControl> _elementCache = new();

    static TimelineTrackPanel()
    {
        PixelsPerBeatProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.InvalidateArrange());
        ClipsProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.OnClipsChanged(e));
        BeatOffsetProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.UpdateMarkerLines());
        MaxBeatProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.UpdateMarkerLines());
    }

    private void OnClipsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        Children.Clear();
        _elementCache.Clear();
        UpdateMarkerLines();

        if (e.NewValue is IEnumerable<ClipViewModel> clips)
        {
            foreach (ClipViewModel clip in clips)
            {
                ClipControl ctrl = new()
                {
                    DataContext = clip,
                    Content = clip.Name,
                    Background = new SolidColorBrush(clip.BackgroundColor)
                };

                ToolTip.SetTip(ctrl, clip.Name);

                Children.Add(ctrl);
                _elementCache[clip] = ctrl;
            }

            // Watch for collection changes if ObservableCollection
            if (clips is INotifyCollectionChanged obs)
            {
                obs.CollectionChanged += OnCollectionChanged;
            }
        }
    }

    private void UpdateMarkerLines()
    {
        // Remove old marker lines
        var markerLines = Children.Where(c => c is Border b && (b.Name == "Beat0Line" || b.Name == "EndBeatLine")).ToList();
        foreach (var line in markerLines) Children.Remove(line);

        if (BeatOffset != 0)
        {
            Children.Add(new Border
            {
                Name = "Beat0Line",
                Width = 1,
                Background = Brushes.White,
                ZIndex = 1000,
                IsHitTestVisible = false
            });
        }

        if (MaxBeat > 0)
        {
            Children.Add(new Border
            {
                Name = "EndBeatLine",
                Width = 1,
                Background = Brushes.White,
                ZIndex = 1000,
                IsHitTestVisible = false
            });
        }
        
        InvalidateArrange();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Handle Add/Remove dynamically in a real implementation
        // For prototype, invalidating/rebuilding is safer but slower
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // We just take up the width needed
        double maxWidth = 0;
        foreach (Control child in Children)
        {
            if (child.Name == "Beat0Line" || child.Name == "EndBeatLine")
            {
                child.Measure(new Size(1, availableSize.Height));
                continue;
            }

            if (child.DataContext is ClipViewModel clip)
            {
                double endX = (clip.StartBeat - BeatOffset) * PixelsPerBeat + (clip.DurationBeats * PixelsPerBeat);
                if (endX > maxWidth)
                    maxWidth = endX;
            }

            child.Measure(availableSize);
        }

        return new Size(maxWidth, availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double ppb = PixelsPerBeat;
        foreach (Control child in Children)
        {
            if (child.Name == "Beat0Line")
            {
                double x = -BeatOffset * ppb;
                child.Arrange(new Rect(x, 0, 1, finalSize.Height));
                continue;
            }

            if (child.Name == "EndBeatLine")
            {
                double x = MaxBeat * ppb;
                child.Arrange(new Rect(x, 0, 1, finalSize.Height));
                continue;
            }

            if (child.DataContext is ClipViewModel clip)
            {
                double x = (clip.StartBeat - BeatOffset) * ppb;
                double w = clip.DurationBeats * ppb;
                child.Arrange(new Rect(x, 2, w, finalSize.Height - 4)); // 2px padding
            }
        }

        return finalSize;
    }
}