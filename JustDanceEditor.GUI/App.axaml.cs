using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using JustDanceEditor.Shared.Avalonia;

using Microsoft.Extensions.DependencyInjection;

namespace JustDanceEditor.GUI;

public sealed partial class App(IServiceProvider services) : Application
{
    private readonly IServiceProvider _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        PlatformTheme.Apply(this, typeof(App).Assembly.GetName().Name!, includeDockStyles: false);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();

        base.OnFrameworkInitializationCompleted();
    }
}
