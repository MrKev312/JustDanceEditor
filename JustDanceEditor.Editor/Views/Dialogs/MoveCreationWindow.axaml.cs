using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using KevInc.Avalonia;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class MoveCreationWindow : Window
{
    public MoveCreationWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);
        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MoveCreationViewModel vm)
            vm.AcceptSelectedMove();

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MoveCreationViewModel vm)
            vm.Cancel();
        Close();
    }
}
