using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Shared.Avalonia;

using System;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class NewSongWindow : Window
{
    public NewSongWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);
        NextBtn.Click += NextBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void NextBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewSongViewModel vm)
            return;

        if (vm.IsLastStep)
        {
            vm.Accept();
            Close();
        }
        else
        {
            vm.GoNextCommand.Execute(null);
        }
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewSongViewModel vm)
            vm.Cancel();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is NewSongViewModel vm)
            vm.Dispose();
    }
}
