using Avalonia;

using JustDanceEditor.AppHost;
using JustDanceEditor.GUI.ViewModels;

using KevInc.Avalonia.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JustDanceEditor.GUI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        UiLogBufferOptions logOptions = new()
        {
            CategoryPrefixToTrim = "JustDanceEditor."
        };
        UiLogBuffer guiLogBuffer = new(logOptions);
        builder.Services.AddSingleton(guiLogBuffer);
        builder.Logging.AddProvider(new UiLoggerProvider(guiLogBuffer, logOptions));

        builder.Services.AddJustDanceEditorAppHost(options => options.RegisterPreviewProviders = true);

        builder.Services.AddSingleton<IApplicationDialogService, AvaloniaDialogService>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        using IHost host = builder.Build();
        BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
