using JustDanceEditor.AppHost;
using JustDanceEditor.Cli.Interactive;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Conversion.Abstractions.Prompts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");

        builder.Services.AddJustDanceEditorAppHost();
        builder.Services.AddSingleton<IConversionInteraction, ConsoleConversionInteraction>();
        builder.Services.AddSingleton<IConversionWorkflow, ConversionWorkflow>();
        builder.Services.AddSingleton<ToolDialogue>();
        builder.Services.AddSingleton<ConsoleApp>();
        builder.Services.AddSingleton<DroppedPathProcessor>();
        builder.Services.AddSingleton<CliApp>();

        using IHost host = builder.Build();
        CliApp cli = host.Services.GetRequiredService<CliApp>();
        return cli.Run(args);
    }
}