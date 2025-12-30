using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Views.Timeline;

public class TimelineTrackPanel : Control
{
    // --- Dependency Properties ---

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

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<TimelineTrackPanel, IBrush?>(nameof(Background));

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    // --- Caches & Resources ---

    private static readonly Pen _linePen = new(Brushes.White, 1);
    private static readonly Typeface _textTypeface = new("Arial");
    private static readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    // Bitmap Cache: Path -> Bitmap
    private static readonly ConcurrentDictionary<string, Bitmap> _bitmapCache = new();
    private static readonly ConcurrentDictionary<string, bool> _pendingLoads = new();

    static TimelineTrackPanel()
    {
        AffectsRender<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty,
            BackgroundProperty);

        AffectsMeasure<TimelineTrackPanel>(
            PixelsPerBeatProperty,
            BeatOffsetProperty,
            MaxBeatProperty);

        ClipsProperty.Changed.AddClassHandler<TimelineTrackPanel>((x, e) => x.OnClipsChanged(e));
    }

    private void OnClipsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldObs)
            oldObs.CollectionChanged -= OnCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newObs)
            newObs.CollectionChanged += OnCollectionChanged;

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    // --- Layout ---

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = MaxBeat * PixelsPerBeat;

        if (Clips != null)
        {
            foreach (var clip in Clips)
            {
                double endX = (clip.StartBeat - BeatOffset + clip.DurationBeats) * PixelsPerBeat;
                if (endX > width) width = endX;
            }
        }

        return new Size(Math.Max(0, width), availableSize.Height);
    }

    // --- Rendering ---

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        double ppb = PixelsPerBeat;
        int offset = BeatOffset;

        // 1. Draw Background
        if (Background != null)
        {
            context.FillRectangle(Background, new Rect(bounds.Size));
        }

        // 2. Draw Marker Lines
        if (BeatOffset != 0)
        {
            double x0 = -offset * ppb;
            if (x0 > -1 && x0 < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(x0, 0), new Point(x0, bounds.Height));
        }
        else
        {
            context.DrawLine(_linePen, new Point(0, 0), new Point(0, bounds.Height));
        }

        if (MaxBeat > 0)
        {
            double xEnd = MaxBeat * ppb;
            if (xEnd > -1 && xEnd < bounds.Width + 1)
                context.DrawLine(_linePen, new Point(xEnd, 0), new Point(xEnd, bounds.Height));
        }

        if (Clips == null) return;

        // 3. Draw Clips
        bool drawText = ppb > 10;

        foreach (var clip in Clips)
        {
            double startX = (clip.StartBeat - offset) * ppb;
            double width = clip.DurationBeats * ppb;
            double endX = startX + width;

            // Culling
            if (endX < 0 || startX > bounds.Width)
                continue;

            // Draw Clip Background
            var rect = new Rect(startX, 2, Math.Max(0, width), Math.Max(1, bounds.Height - 4));
            context.FillRectangle(new SolidColorBrush(clip.BackgroundColor), rect);

            // Draw Image (Pictogram)
            if (clip.ImagePath != null)
            {
                if (_bitmapCache.TryGetValue(clip.ImagePath, out Bitmap? bmp))
                {
                    // Draw bitmap to fit height, centered horizontally
                    double aspect = bmp.Size.Width / bmp.Size.Height;
                    double drawHeight = rect.Height;
                    double drawWidth = drawHeight * aspect;

                    // Center image in the clip rect
                    double imgX = startX + (width - drawWidth) / 2;
                    double imgY = rect.Y;

                    var destRect = new Rect(imgX, imgY, drawWidth, drawHeight);

                    using (context.PushClip(rect))
                    {
                        context.DrawImage(bmp, new Rect(bmp.Size), destRect);
                    }
                }
                else
                {
                    ScheduleBitmapLoad(clip.ImagePath);
                }
            }
            // Draw Text (Only if NOT a pictogram/image)
            else if (drawText && width > 30 && !string.IsNullOrEmpty(clip.Name))
            {
                var ft = new FormattedText(
                    clip.Name,
                    _culture,
                    FlowDirection.LeftToRight,
                    _textTypeface,
                    12,
                    Brushes.Black
                )
                {
                    MaxTextWidth = width,
                    MaxTextHeight = rect.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };

                // Center horizontally: startX + (clipWidth - textWidth) / 2
                // Center vertically:   rect.Y + (clipHeight - textHeight) / 2
                double textX = startX + (width - ft.Width) / 2;
                double textY = rect.Y + (rect.Height - ft.Height) / 2;

                // Ensure we don't draw outside the clip if centered text pushes out
                if (textX < startX) textX = startX;

                context.DrawText(ft, new Point(textX, textY));
            }
        }
    }

    private void ScheduleBitmapLoad(string path)
    {
        if (_pendingLoads.ContainsKey(path)) return;
        _pendingLoads.TryAdd(path, true);

        Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    using var fs = File.OpenRead(path);
                    var bmp = Bitmap.DecodeToWidth(fs, 200);

                    _bitmapCache.TryAdd(path, bmp);

                    Dispatcher.UIThread.Post(() => {
                        _pendingLoads.TryRemove(path, out _);
                        InvalidateVisual();
                    });
                }
            }
            catch
            {
                _pendingLoads.TryRemove(path, out _);
            }
        });
    }

    // --- Interaction ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.ClickCount == 2 && Clips != null)
        {
            var point = e.GetCurrentPoint(this).Position;
            double ppb = PixelsPerBeat;
            double offset = BeatOffset;

            ClipViewModel? targetClip = null;

            foreach (var c in Clips)
            {
                double x = (c.StartBeat - offset) * ppb;
                double w = c.DurationBeats * ppb;
                if (point.X >= x && point.X <= x + w)
                {
                    targetClip = c;
                }
            }

            if (targetClip != null)
            {
                var visualParent = this.GetVisualParent();
                while (visualParent != null)
                {
                    if (visualParent is Control c && c.DataContext is TimelineEditorViewModel vm)
                    {
                        vm.Playback.SeekToBeat(targetClip.StartBeat);
                        e.Handled = true;
                        return;
                    }
                    visualParent = visualParent.GetVisualParent();
                }
            }
        }
    }
}