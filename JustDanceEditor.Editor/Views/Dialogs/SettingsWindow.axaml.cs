using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using KevInc.Avalonia;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);
        SaveBtn.Click += SaveBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Accept();

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Cancel();

        Close();
    }
}
