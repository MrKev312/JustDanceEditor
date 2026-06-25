using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using KevInc.Avalonia;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class LyricsCreationWindow : Window
{
    public LyricsCreationWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);

        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LyricsCreationViewModel vm)
        {
            vm.Accept();
        }

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LyricsCreationViewModel vm)
            vm.Cancel();
        Close();
    }
}