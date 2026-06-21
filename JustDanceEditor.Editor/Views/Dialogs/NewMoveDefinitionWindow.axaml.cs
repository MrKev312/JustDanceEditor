using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;

using KevInc.Avalonia;

using System;

namespace JustDanceEditor.Editor.Views.Dialogs;

public partial class NewMoveDefinitionWindow : Window
{
    public NewMoveDefinitionWindow()
    {
        InitializeComponent();
        PlatformTheme.ApplyFloatingWindowChrome(this);
        OkBtn.Click += OkBtn_Click;
        CancelBtn.Click += CancelBtn_Click;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NameBox.Focus();
    }

    private void OkBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewMoveDefinitionViewModel vm)
        {
            if (!vm.IsValid)
                return;
            vm.Accept();
        }

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewMoveDefinitionViewModel vm)
            vm.Cancel();
        Close();
    }
}
