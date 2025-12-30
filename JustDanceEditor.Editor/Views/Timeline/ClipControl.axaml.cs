using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustDanceEditor.Editor.ViewModels.Timeline;

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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.ClickCount == 2 && DataContext is ClipViewModel clip)
        {
            // Find the parent TimelineEditorViewModel
            // Visual tree climbing is one way, but if it's in the same project we can usually reach it via a service or a known tree structure.
            // For now, let's assume we can find it via the parent track or similar, 
            // but a more robust way in this project's pattern is probably finding it in the view hierarchy.
            
            var parent = this.GetVisualParent();
            while (parent != null && parent.DataContext is not TimelineEditorViewModel)
            {
                parent = parent.GetVisualParent();
            }

            if (parent?.DataContext is TimelineEditorViewModel vm)
            {
                vm.Playback.SeekToBeat(clip.StartBeat);
                e.Handled = true;
            }
        }
    }
}