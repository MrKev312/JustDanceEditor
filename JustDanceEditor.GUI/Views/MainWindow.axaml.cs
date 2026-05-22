using Avalonia.Controls;
using JustDanceEditor.GUI.ViewModels;
using JustDanceEditor.Shared.Avalonia;

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
