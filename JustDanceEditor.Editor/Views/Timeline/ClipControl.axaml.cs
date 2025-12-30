// File: .\Views\Timeline\ClipControl.axaml.cs
using Avalonia;
using Avalonia.Controls.Primitives;

namespace JustDanceEditor.Editor.Views.Timeline;

public class ClipControl : TemplatedControl
{
    public static readonly StyledProperty<object> ContentProperty =
        AvaloniaProperty.Register<ClipControl, object>(nameof(Content));

    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}