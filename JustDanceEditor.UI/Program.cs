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
        builder.Services.AddSingleton<IToolProvider, IpkToolProvider>();

        IReadOnlyList<IConverterPlugin> plugins = ConverterPluginLoader.LoadPlugins();
        foreach (IConverterPlugin plugin in plugins)
        {
            builder.Services.AddSingleton(plugin);
            plugin.ConfigureServices(builder.Services);
        }

        // Register ConsoleApp
        builder.Services.AddSingleton<ToolDialogue>();
        builder.Services.AddSingleton<ConsoleApp>();
        builder.Services.AddSingleton<CliApp>();

        IHost host = builder.Build();

        if (args.Length > 0)
        {
            CliApp cli = host.Services.GetRequiredService<CliApp>();
            Environment.ExitCode = cli.Run(args);
        }
        else
        {
            ConsoleApp app = host.Services.GetRequiredService<ConsoleApp>();
            app.Run();
        }

        // If there are async shutdown tasks in the future, host.DisposeAsync can be awaited here
        await Task.CompletedTask;
    }
}
