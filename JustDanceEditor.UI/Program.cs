using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.UI.Converting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JustDanceEditor.UI;

internal class Program
{
    static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");

        // Register services
        builder.Services.AddSingleton<IFileSystem, SystemFileSystem>();
        builder.Services.TryAddSingleton<SystemFileSystem>();
        builder.Services.TryAddSingleton<ITextureService, DefaultTextureService>();
        builder.Services.TryAddSingleton<IMediaProcessor, DefaultMediaProcessor>();
        builder.Services.AddSingleton<IConversionInteraction, ConsoleConversionInteraction>();

        // Conversion workflow service
        builder.Services.AddSingleton<IConversionWorkflow, ConversionWorkflow>();
        builder.Services.AddSingleton<IJdiFormat, JdiFormat>();
        builder.Services.AddSingleton<IFormatConversionStrategy, JdiConversionStrategy>();

        foreach (IConverterPlugin plugin in ConverterPluginLoader.LoadPlugins())
        {
            plugin.ConfigureServices(builder.Services);
        }

        // Register ConsoleApp
        builder.Services.AddSingleton<ConsoleApp>();

        IHost host = builder.Build();

        // Run the console app
        ConsoleApp app = host.Services.GetRequiredService<ConsoleApp>();
        app.Run();

        // If there are async shutdown tasks in the future, host.DisposeAsync can be awaited here
        await Task.CompletedTask;
    }
}
