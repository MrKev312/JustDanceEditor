using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using System;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class EditSongWindow : Window
{
    public EditSongWindow()
    {
        InitializeComponent();
        NextBtn.Click += NextBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    private void NextBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditSongViewModel vm)
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
        if (DataContext is EditSongViewModel vm)
            vm.Cancel();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is EditSongViewModel vm)
            vm.Dispose();
    }
}