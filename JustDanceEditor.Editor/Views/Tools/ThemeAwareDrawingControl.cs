using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JustDanceEditor.Editor.Views.Tools;

public abstract class ThemeAwareDrawingControl : Control
{
    public static readonly StyledProperty<IBrush> TextBrushProperty =
        AvaloniaProperty.Register<ThemeAwareDrawingControl, IBrush>(nameof(TextBrush), Brushes.White);

    public static readonly StyledProperty<IBrush> MutedTextBrushProperty =
        AvaloniaProperty.Register<ThemeAwareDrawingControl, IBrush>(nameof(MutedTextBrush), Brushes.LightGray);

    protected ThemeAwareDrawingControl()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush MutedTextBrush
    {
        get => GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextBrushProperty || change.Property == MutedTextBrushProperty)
            InvalidateVisual();
    }
}
