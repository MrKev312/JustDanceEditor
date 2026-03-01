using Avalonia.Media;

namespace JustDanceEditor.Editor.Views.Timeline;

public static class TimelineResources
{
    // General pens and brushes
    public static readonly Pen LinePen = new(Brushes.White, 1);
    public static readonly Pen SelectionPen = new(Brushes.Gold, 2.0);
    public static readonly Pen BlackOutlinePen = new(Brushes.Black, 1.0);
    public static readonly Pen BoxSelectionBorderPen = new(Brushes.Gold, 1);

    public static readonly SolidColorBrush BoxSelectionFill = new(new Color(64, 0, 120, 215));
    public static readonly SolidColorBrush SelectionOverlay = new(new Color(120, 255, 215, 0));

    // Audio/section pens and brushes
    public static readonly Pen WaveformPen = new(Brushes.DarkGray, 1);
    public static readonly SolidColorBrush WaveformFill = new(Colors.LightGray, 0.3);
    public static readonly Pen WaveformEdgePen = new(Brushes.White, 1);
    public static readonly Pen SectionBorderPen = new(Brushes.White, 1, new DashStyle(new double[] { 2, 2 }, 0));
    public static readonly SolidColorBrush SectionBgBrush = new(Colors.Black, 0.5);

    // Time ruler brushes
    public static readonly SolidColorBrush MeasureBrushA = new(Colors.White, 0.05);
    public static readonly SolidColorBrush MeasureBrushB = new(Colors.White, 0.02);
    public static readonly Pen MainPen = new(Brushes.DimGray, 1);
    public static readonly Pen TickPen = new(Brushes.Gray, 1);
    public static readonly IBrush LabelBrush = Brushes.LightGray;

    // Clip-specific label color (used for clip text within timeline tracks)
    public static readonly IBrush ClipLabelBrush = Brushes.Black;

    // Typography
    public static readonly Typeface DefaultTypeface = new("Arial");
}