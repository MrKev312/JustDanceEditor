using Avalonia;
using Avalonia.Controls;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.Views.Dialogs;

using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public class AvaloniaDialogService : IDialogService
{
    public async Task<TResult?> ShowDialogAsync<TResult>(object viewModel)
    {
        Window? owner = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null;

        Window? win = viewModel switch
        {
            LyricsCreationViewModel _ => new LyricsCreationWindow(),
            PictogramCreationViewModel _ => new PictogramCreationWindow(),
            MoveCreationViewModel _ => new MoveCreationWindow(),
            GoldEffectCreationViewModel _ => new GoldEffectCreationWindow(),
            _ => throw new System.ArgumentException("Unsupported dialog viewmodel type")
        };

        win.DataContext = viewModel;

        if (owner != null)
            await win.ShowDialog(owner);
        else
            await win.ShowDialog(win);

        if (viewModel is IDialogResult<TResult> dr)
            return dr.Result;

        return default;
    }
}