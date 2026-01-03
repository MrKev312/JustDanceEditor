using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.Views;

using System.Linq;

namespace JustDanceEditor.Editor;

public partial class App : Application
{
    public ITimelineContextService TimelineContext { get; } = new TimelineContextService();
    public IDialogService DialogService { get; } = new AvaloniaDialogService();

    public LibVLCSharp.Shared.LibVLC LibVLC { get; } = new(
        // Optimization arguments
        "--avcodec-hw=any",       // Enable hardware acceleration
        "--no-stats",              // Disable stats for less overhead
        "--no-video-title-show",   // Disable overlay title
        "--network-caching=300",  // Lower caching for better sync/latency
        "--clock-jitter=0",        // Treat clock jitter as zero for timeline sync
        "--no-osd"                 // Disable on-screen display
    );

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        DataAnnotationsValidationPlugin[] dataValidationPluginsToRemove =
            [.. BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>()];

        // remove each entry found
        foreach (DataAnnotationsValidationPlugin? plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}