using Avalonia.Controls;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Open Map Folder...", "File", priority: 100)]
public class OpenMapFolderCommand(IWindowService windows) : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => true;

    public void Run(ITimelineContextService? timelineContext)
    {
        if (windows.MainWindow?.DataContext is MainWindowViewModel mainWindow)
            mainWindow.OpenMapCommand.Execute(null);
    }
}
