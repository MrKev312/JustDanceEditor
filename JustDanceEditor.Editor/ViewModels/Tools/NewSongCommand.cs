using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Editor.Views.Dialogs;
using JustDanceEditor.Formats.JDI;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("New Song...", "File", priority: 110)]
public class NewSongCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => true;

    public void Run(ITimelineContextService? timelineContext)
    {
        _ = RunAsync(timelineContext);
    }

    private static async Task RunAsync(ITimelineContextService? timelineContext)
    {
        try
        {
            IClassicDesktopStyleApplicationLifetime? desktop =
                Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            Window? mainWindow = desktop?.MainWindow;
            if (mainWindow == null)
                return;

            // Show the new song dialog
            NewSongViewModel vm = new();
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
                mvm.OpenPackage(package, rootPath);
            }
        }
        catch (Exception ex)
        {
            // Show error to user via simple dialog
            IClassicDesktopStyleApplicationLifetime? desktop2 =
                Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            Window? mw = desktop2?.MainWindow;
            if (mw != null)
            {
                Window errorWin = new()
                {
                    Title = "Error Creating Song",
                    Width = 400,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBlock
                    {
                        Text = $"Failed to create song package:\n{ex.Message}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(16)
                    }
                };
                await errorWin.ShowDialog(mw);
            }
        }
    }
}
