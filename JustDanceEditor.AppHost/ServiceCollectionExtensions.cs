using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Preview;
using JustDanceEditor.Formats.JDI.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JustDanceEditor.AppHost;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJustDanceEditorAppHost(
        this IServiceCollection services,
        Action<JustDanceEditorAppHostOptions>? configure = null)
    {
        JustDanceEditorAppHostOptions options = new();
        configure?.Invoke(options);

        services.AddSingleton<IFileSystem, SystemFileSystem>();
        services.TryAddSingleton<SystemFileSystem>();
        services.TryAddSingleton<ITextureService, DefaultTextureService>();
        services.TryAddSingleton<IMediaProcessor, DefaultMediaProcessor>();

        services.AddSingleton<IJdiFormat, JdiFormat>();
        services.AddSingleton<IFormatConversionStrategy, JdiConversionStrategy>();

        if (options.RegisterPreviewProviders)
            services.AddSingleton<ISongPreviewProvider, JdiSongPreviewProvider>();

        if (options.RegisterBuiltInTools)
            services.AddSingleton<IToolProvider, IpkToolProvider>();

        IReadOnlyList<IConverterPlugin> plugins = ConverterPluginLoader.LoadPlugins();
        foreach (IConverterPlugin plugin in plugins)
        {
            services.AddSingleton(plugin);
            plugin.ConfigureServices(services);
        }

        return services;
    }
}
