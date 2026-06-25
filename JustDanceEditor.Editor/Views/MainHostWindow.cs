using Dock.Avalonia.Controls;

using KevInc.Avalonia;

namespace JustDanceEditor.Editor.Views;

public class MainHostWindow : HostWindow
{
    public MainHostWindow()
    {
        PlatformTheme.ApplyFloatingWindowChrome(this);
    }
}