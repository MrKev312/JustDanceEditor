using Dock.Avalonia.Controls;

using JustDanceEditor.Shared.Avalonia;

namespace JustDanceEditor.Editor.Views;

public class MainHostWindow : HostWindow
{
    public MainHostWindow()
    {
        PlatformTheme.ApplyFloatingWindowChrome(this);
    }
}
