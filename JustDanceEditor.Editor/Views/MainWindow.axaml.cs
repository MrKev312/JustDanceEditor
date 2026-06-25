using Avalonia.Controls;

using KevInc.Avalonia;

namespace JustDanceEditor.Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyMainWindowChrome(this);
    }
}