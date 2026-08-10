using Avalonia.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.Views.Dialogs;
using JustDanceEditor.Formats.JDI;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("New Song...", "File", priority: 110)]
public class NewSongCommand(IWindowService windows, IEditorPromptService prompts) : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => true;

    public void Run(ITimelineContextService? timelineContext)
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            Window? mainWindow = windows.MainWindow;
            if (mainWindow == null)
                return;

            // Show the new song dialog
            NewSongViewModel vm = new(windows);
            NewSongWindow dialog = new() { DataContext = vm };

            await dialog.ShowDialog(mainWindow);

            NewSongResult? result = vm.Result;
            if (result == null)
                return;

            // Create the package on disk
            (string rootPath, IntermediateSongPackage package) =
                await NewSongPackageCreator.CreatePackageAsync(result);

            // Open it in the timeline editor
            if (mainWindow.DataContext is MainWindowViewModel mvm)
            {
                await mvm.OpenPackage(package, rootPath);
            }
        }
        catch (Exception ex)
        {
            await prompts.ShowErrorAsync(
                "Error Creating Song",
                $"Failed to create song package:\n{ex.Message}",
                ex);
        }
    }
}
