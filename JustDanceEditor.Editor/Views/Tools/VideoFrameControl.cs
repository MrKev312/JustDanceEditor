using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using System;

namespace JustDanceEditor.Editor.Views.Tools;

public sealed class VideoFrameControl : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<VideoFrameControl, Bitmap?>(nameof(Source));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly StyledProperty<int> FrameVersionProperty =
        AvaloniaProperty.Register<VideoFrameControl, int>(nameof(FrameVersion));

    public int FrameVersion
    {
        get => GetValue(FrameVersionProperty);
        set => SetValue(FrameVersionProperty, value);
    }

    static VideoFrameControl()
    {
        AffectsRender<VideoFrameControl>(SourceProperty, FrameVersionProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Bitmap? source = Source;
        if (source == null)
            return;

        Rect bounds = Bounds;
        Size sourceSize = source.Size;
        if (bounds.Width <= 0 || bounds.Height <= 0 || sourceSize.Width <= 0 || sourceSize.Height <= 0)
            return;

        double scale = Math.Min(bounds.Width / sourceSize.Width, bounds.Height / sourceSize.Height);
        Size destinationSize = new(sourceSize.Width * scale, sourceSize.Height * scale);
        Point destinationPosition = new(
            bounds.X + (bounds.Width - destinationSize.Width) / 2,
            bounds.Y + (bounds.Height - destinationSize.Height) / 2);

        context.DrawImage(source, new Rect(sourceSize), new Rect(destinationPosition, destinationSize));
    }
}