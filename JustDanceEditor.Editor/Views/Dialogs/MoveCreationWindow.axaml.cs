using Avalonia.Controls;
using Avalonia.Interactivity;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.ViewModels.Timeline;

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
        {
            // compute frames from selected move definition if possible
            TimelineEditorViewModel? mainVm = (Avalonia.Application.Current as App)?.TimelineContext?.ActiveTimeline;
            int frames = 24;
            try
            {
                if (!string.IsNullOrEmpty(vm.SelectedMove) && mainVm != null)
                {
                    MoveDefinitionViewModel def = mainVm.GetOrRegisterMove(vm.SelectedMove, vm.IsFullBody);
                    if (def != null)
                        frames = (int)def.DefaultDuration;
                }
            }
            catch { }

            vm.Accept(frames);
        }

        Close();
    }

    private void CancelBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MoveCreationViewModel vm)
            vm.Cancel();
        Close();
    }
}
