using Avalonia.Controls;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.Views.Dialogs;

using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public sealed class AvaloniaDialogService(IWindowService windows) : IDialogService
{
    public async Task<TResult?> ShowDialogAsync<TResult>(object viewModel)
    {
        Window? owner = windows.MainWindow;

        Window? win = viewModel switch
        {
            LyricsCreationViewModel _ => new LyricsCreationWindow(),
            PictogramCreationViewModel _ => new PictogramCreationWindow(),
            MoveCreationViewModel _ => new MoveCreationWindow(),
            GoldEffectCreationViewModel _ => new GoldEffectCreationWindow(),
            HideHudCreationViewModel _ => new HideHudCreationWindow(),
            NewMoveDefinitionViewModel _ => new NewMoveDefinitionWindow(),
            PictogramScreenshotOptionsViewModel _ => new PictogramScreenshotOptionsWindow(),
            SettingsViewModel _ => new SettingsWindow(),
            _ => throw new System.ArgumentException("Unsupported dialog viewmodel type")
        };

        win.DataContext = viewModel;

        if (owner == null)
            throw new System.InvalidOperationException("The main window is not available.");

        try
        {
            await win.ShowDialog(owner);
            return viewModel is IDialogResult<TResult> resultProvider
                ? resultProvider.Result
                : default;
        }
        finally
        {
            win.DataContext = null;
            if (viewModel is System.IDisposable disposable)
                disposable.Dispose();
        }
    }
}