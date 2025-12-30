using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.Views.Timeline;

public class AudioBarControl : Control
{
    public static readonly StyledProperty<float[]> SamplesProperty =
        AvaloniaProperty.Register<AudioBarControl, float[]>(nameof(Samples));

    public float[] Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public static readonly StyledProperty<double> PixelsPerBeatProperty =
        AvaloniaProperty.Register<AudioBarControl, double>(nameof(PixelsPerBeat), 50.0);

    public double PixelsPerBeat
    {
        get => GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public static readonly StyledProperty<int> BeatOffsetProperty =
        AvaloniaProperty.Register<AudioBarControl, int>(nameof(BeatOffset), 0);

    public int BeatOffset
    {
        get => GetValue(BeatOffsetProperty);
        set => SetValue(BeatOffsetProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<SectionSegment>> SectionsProperty =
        AvaloniaProperty.Register<AudioBarControl, IEnumerable<SectionSegment>>(nameof(Sections));

    public IEnumerable<SectionSegment> Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    private static readonly Dictionary<SongSectionType, Color> _sectionColors = new();

    static AudioBarControl()
    {
        AffectsRender<AudioBarControl>(SamplesProperty, PixelsPerBeatProperty, SectionsProperty, BeatOffsetProperty);

        // Pre-cache section colors
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            var field = typeof(SongSectionType).GetField(type.ToString());
            var attr = field?.GetCustomAttribute<ColorAttribute>();
            if (attr != null)
            {
                _sectionColors[type] = Color.FromRgb(attr.R, attr.G, attr.B);
            }
            else
            {
                _sectionColors[type] = Colors.Gray;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        
        if (e.ClickCount == 2 && Sections != null)
        {
            var point = e.GetCurrentPoint(this);
            double ppb = PixelsPerBeat;
            double offset = BeatOffset;
            double clickedBeat = (point.Position.X / ppb) + offset;

            // Find the section that contains or starts at this beat
            var section = Sections.OrderByDescending(s => s.StartBeat)
                                 .FirstOrDefault(s => s.StartBeat <= clickedBeat);
            
            if (section != null && DataContext is TimelineEditorViewModel vm)
            {
                vm.Playback.SeekToBeat(section.StartBeat);
                e.Handled = true;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        double ppb = PixelsPerBeat;
        double offset = BeatOffset;

        // 1. Draw Section Backgrounds
        if (Sections != null)
        {
            var sortedSections = Sections.OrderBy(s => s.StartBeat).ToList();
            for (int i = 0; i < sortedSections.Count; i++)
            {
                var section = sortedSections[i];
                var color = _sectionColors.TryGetValue(section.SectionType, out var c) ? c : Colors.Gray;
                
                double startX = (section.StartBeat - offset) * ppb;
                double endX = bounds.Width;

                if (i + 1 < sortedSections.Count)
                {
                    endX = (sortedSections[i + 1].StartBeat - offset) * ppb;
                }

                if (startX < bounds.Width && endX > 0)
                {
                    var rect = new Rect(Math.Max(0, startX), 0, Math.Min(bounds.Width, endX) - Math.Max(0, startX), bounds.Height);
                    context.FillRectangle(new SolidColorBrush(color, 0.3), rect);
                }
            }
        }

        // 2. Draw Waveform
        if (Samples != null && Samples.Length > 0)
        {
            // Calculate actual beats based on sample count if possible, 
            // but here we just draw based on what we see in the viewport or total width
            // Since we don't have BPM easily here (BpmProperty was using fixed 120), 
            // we should probably just draw up to the end of samples if it matches ppb.
            
            // For simplicity, let's assume the waveform width matches the timeline width 
            // if we knew the duration. Let's use 120 as a fallback or just draw what we can.
            
            // Actually, we want the waveform to align with beats. 
            // Let's assume the sample data is tied to the total timeline duration.
            
            double totalWidth = bounds.Width; // This is constrained by the parent Grid in ScrollViewer
            
            var pen = new Pen(new SolidColorBrush(Colors.LimeGreen, 0.8), 1);
            double centerY = bounds.Height / 2;

            int step = Math.Max(1, Samples.Length / (int)totalWidth);
            for (int x = 0; x < (int)totalWidth; x++)
            {
                int sampleIdx = (int)((double)x / totalWidth * Samples.Length);
                if (sampleIdx >= Samples.Length) break;

                float val = Samples[sampleIdx];
                double h = val * centerY * 0.8;
                context.DrawLine(pen, new Point(x, centerY - h), new Point(x, centerY + h));
            }
        }

        // 3. Draw Section Labels
        if (Sections != null)
        {
            var sortedSections = Sections.OrderBy(s => s.StartBeat).ToList();
            foreach (var section in sortedSections)
            {
                double x = (section.StartBeat - offset) * ppb;
                if (x >= 0 && x < bounds.Width)
                {
                    // Draw vertical line
                    context.DrawLine(new Pen(Brushes.White, 1, new DashStyle([2, 2], 0)), new Point(x, 0), new Point(x, bounds.Height));

                    // Draw text
                    var text = new FormattedText(
                        section.SectionType.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        10,
                        Brushes.White);
                    
                    var bgRect = new Rect(x + 2, 2, text.Width + 4, text.Height + 2);
                    context.FillRectangle(new SolidColorBrush(Colors.Black, 0.5), bgRect);
                    context.DrawText(text, new Point(x + 4, 3));
                }
            }
        }
    }
}