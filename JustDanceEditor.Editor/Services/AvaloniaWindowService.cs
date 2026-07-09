using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace JustDanceEditor.Editor.Services;

internal sealed class AvaloniaWindowService(Application application) : IWindowService
{
    public Window? MainWindow
        => (application.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public IStorageProvider? StorageProvider => MainWindow?.StorageProvider;

    public Window? GetOwner(Visual? relativeTo = null)
        => relativeTo == null ? MainWindow : TopLevel.GetTopLevel(relativeTo) as Window ?? MainWindow;
}
