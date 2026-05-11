using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.AppHost;
using JustDanceEditor.UI.Converting;

using Microsoft.Extensions.DependencyInjection;
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

        builder.Services.AddJustDanceEditorAppHost();
        builder.Services.AddSingleton<IConversionInteraction, ConsoleConversionInteraction>();

        // Conversion workflow service
        builder.Services.AddSingleton<IConversionWorkflow, ConversionWorkflow>();

        // Register interactive console UI
        builder.Services.AddSingleton<ToolDialogue>();
        builder.Services.AddSingleton<ConsoleApp>();

        using IHost host = builder.Build();

        if (args.Length > 0 && !IsConsoleCommand(args[0]))
        {
            Console.Error.WriteLine("Command-line mode has moved to JustDanceEditor.Cli. Run JustDanceEditor.Cli help for usage.");
            Environment.ExitCode = 2;
            return;
        }

        ConsoleApp app = host.Services.GetRequiredService<ConsoleApp>();
        app.Run();

        // If there are async shutdown tasks in the future, host.DisposeAsync can be awaited here
        await Task.CompletedTask;
    }

    private static bool IsConsoleCommand(string value) =>
        value.Equals("console", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("--console", StringComparison.OrdinalIgnoreCase);
}
