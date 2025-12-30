using Avalonia;
using Avalonia.Controls;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using System;
using System.Linq;

namespace JustDanceEditor.Editor.Views.Tools;

public class PictogramScrollingPanel : Panel
{
    public static readonly StyledProperty<double> CurrentBeatProperty =
        AvaloniaProperty.Register<PictogramScrollingPanel, double>(nameof(CurrentBeat));

    public double CurrentBeat
    {
        get => GetValue(CurrentBeatProperty);
        set => SetValue(CurrentBeatProperty, value);
    }

    public static readonly StyledProperty<TimelineEditorViewModel?> ActiveTimelineProperty =
        AvaloniaProperty.Register<PictogramScrollingPanel, TimelineEditorViewModel?>(nameof(ActiveTimeline));

    public TimelineEditorViewModel? ActiveTimeline
    {
        get => GetValue(ActiveTimelineProperty);
        set => SetValue(ActiveTimelineProperty, value);
    }

    static PictogramScrollingPanel()
    {
        // Whenever CurrentBeat changes, we need to re-arrange
        AffectsArrange<PictogramScrollingPanel>(CurrentBeatProperty, ActiveTimelineProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (ActiveTimeline == null) return finalSize;

        var ts = ActiveTimeline.TimelineStructure;
        double currentTime = CurrentBeat;
        int coachCount = ActiveTimeline.CoachCount;

        double scrollDuration = GetScrollDurationInBeats(currentTime, ts);
        if (scrollDuration <= 0) return finalSize;

        foreach (var child in Children)
        {
            if (child is not Control control || control.DataContext is not ClipViewModel clip)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            double startBeat = clip.StartBeat;
            double ppb = GetBeatsPerPixel(coachCount, startBeat, ts);
            double expectedWidth = GetPictoExpectedWidth(coachCount);
            double stopBeat = startBeat + (ppb * expectedWidth);

            // Calculation
            double relPos = (startBeat - currentTime) / scrollDuration;
            double relWidth = (stopBeat - startBeat) / scrollDuration;

            double drawX = finalSize.Width * relPos;
            double drawWidth = finalSize.Width * relWidth;
            
            // Aspect ratio handling: try to get it from the control if it's an Image
            double aspect = 1.0;
            if (control is Image img && img.Source != null)
            {
                aspect = img.Source.Size.Height / img.Source.Size.Width;
            }
            
            double drawHeight = drawWidth * aspect;
            double drawY = (finalSize.Height - drawHeight) / 2.0;

            // Handle fading/perspective at the start
            double offScreenLeft = (drawX < 0) ? (-drawX / drawWidth) : 0f;
            double opacity = Math.Max(0, Math.Min(1.0, 1.0 - (1.8 * offScreenLeft)));
            
            if (drawX < 0)
            {
                drawY -= (0.4 * drawHeight * offScreenLeft);
                drawX = 0;
            }

            control.Opacity = opacity;
            
            if (opacity <= 0.01)
            {
                control.Arrange(new Rect(0, 0, 0, 0));
            }
            else
            {
                control.Arrange(new Rect(drawX, drawY, drawWidth, drawHeight));
            }
        }

        return finalSize;
    }

    private double GetScrollDurationInBeats(double beat, TimelineStructureDocument ts)
    {
        double seconds = ts.GetSecondsAtBeat(beat);
        double futureBeat = ts.GetBeatAtSeconds(seconds + 4.0);
        return futureBeat - beat;
    }

    private double GetBeatsPerPixel(int coachCount, double beat, TimelineStructureDocument ts)
    {
        double duration = GetScrollDurationInBeats(beat, ts);
        double scrollWidthCoords = GetScrollWidthInUaf2DCoords(coachCount);
        double scrollWidthPixels = scrollWidthCoords / 0.4;
        return duration / scrollWidthPixels;
    }

    private int GetScrollWidthInUaf2DCoords(int playerCount)
    {
        return playerCount switch
        {
            1 => 800,
            2 => 900,
            3 => 950,
            4 => 1120,
            6 => 1100,
            _ => 800
        };
    }

    private int GetPictoExpectedWidth(int playerCount)
    {
        return playerCount switch
        {
            1 => 512,
            2 or 3 or 4 => 740,
            6 => 968,
            _ => 512
        };
    }
}
