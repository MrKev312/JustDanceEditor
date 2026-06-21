using Avalonia.Controls;

using JustDanceEditor.GUI.ViewModels;

using KevInc.Avalonia;

namespace JustDanceEditor.GUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyMainWindowChrome(this);
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}
