using Avalonia.Controls;

using JustDanceEditor.Shared.Avalonia;

namespace JustDanceEditor.Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyMainWindowChrome(this);
    }
}
