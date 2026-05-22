using Avalonia.Controls;
using Avalonia.Media;

using JustDanceEditor.GUI.ViewModels;

namespace JustDanceEditor.GUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowMaterial();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void ApplyWindowMaterial()
    {
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur
        ];
        Background = Brushes.Transparent;
    }
}