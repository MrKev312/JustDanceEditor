using Avalonia.Controls;
using Avalonia.Media;

using Dock.Avalonia.Controls;

namespace JustDanceEditor.Editor.Views;

public class MainHostWindow : HostWindow
{
    public MainHostWindow()
    {
        // 1. Enable Mica/Blur effects
        TransparencyLevelHint = [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur
        ];

        // 2. Set Background to Transparent to allow Mica to show through
        Background = Brushes.Transparent;

        // 3. Keep the System Titlebar (Maximize, Minimize, Close)
        // If you set this to true, the content extends into the title bar (modern style).
        // If you set this to false, you get the standard Windows title bar.
        ExtendClientAreaToDecorationsHint = false;

        // Optional: Set a default icon or other window properties here
        // Icon = new WindowIcon(...);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}