using Avalonia;
using Avalonia.Media;

using System;

namespace JustDanceEditor.Editor.Views.Tools;

public abstract class ThemeAwareGraphControl : ThemeAwareDrawingControl
{
    private static readonly Color[] SeriesColors =
    [
        Color.FromRgb(88, 204, 255),
        Color.FromRgb(255, 198, 92),
        Color.FromRgb(154, 222, 113),
        Color.FromRgb(255, 128, 170),
        Color.FromRgb(190, 155, 255),
        Color.FromRgb(255, 150, 96),
        Color.FromRgb(113, 233, 207),
        Color.FromRgb(250, 139, 255)
    ];

    public static readonly StyledProperty<IBrush> AxisBrushProperty =
        AvaloniaProperty.Register<ThemeAwareGraphControl, IBrush>(nameof(AxisBrush), new SolidColorBrush(Color.FromArgb(175, 220, 224, 232)));

    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<ThemeAwareGraphControl, IBrush>(nameof(GridBrush), new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)));

    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        AvaloniaProperty.Register<ThemeAwareGraphControl, IBrush>(nameof(AccentBrush), new SolidColorBrush(Color.FromRgb(88, 204, 255)));

    public IBrush AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public IBrush GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AxisBrushProperty
            || change.Property == GridBrushProperty
            || change.Property == AccentBrushProperty)
        {
            InvalidateVisual();
        }
    }

    protected Color GetSeriesColor(int seriesIndex, byte alpha)
    {
        Color color = SeriesColors[Math.Abs(seriesIndex) % SeriesColors.Length];
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}