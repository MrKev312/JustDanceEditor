using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace JustDanceEditor.Editor.Services;

public interface IWindowService
{
    Window? MainWindow { get; }
    IStorageProvider? StorageProvider { get; }
    Window? GetOwner(Visual? relativeTo = null);
}
