using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class PictogramCreationWindow : Window
{
    public PictogramCreationWindow()
    {
        InitializeComponent();
        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PictogramCreationViewModel vm)
            vm.Accept();
        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PictogramCreationViewModel vm)
            vm.Cancel();
        Close();
    }
}