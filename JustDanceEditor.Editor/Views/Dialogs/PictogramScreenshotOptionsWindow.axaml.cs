using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Shared.Avalonia;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class PictogramScreenshotOptionsWindow : Window
{
    public PictogramScreenshotOptionsWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);
        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PictogramScreenshotOptionsViewModel vm)
            vm.Accept();

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PictogramScreenshotOptionsViewModel vm)
            vm.Cancel();

        Close();
    }
}
