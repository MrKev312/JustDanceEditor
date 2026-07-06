using JustDanceEditor.AppHost;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.Cli;

internal sealed class CliCatalogPrinter(
    IReadOnlyList<IConverterPlugin> plugins,
    IReadOnlyList<IJdiFormat> formats,
    IReadOnlyList<IFormatConversionStrategy> strategies,
    IReadOnlyList<IToolProvider> toolProviders)
{
    public int ListPlugins(CliOptions options)
    {
        Console.WriteLine("Converter plugins:");
        foreach (IConverterPlugin plugin in plugins.OrderBy(plugin => plugin.Priority).ThenBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            string assemblyPath = plugin.GetType().Assembly.Location;
            Console.WriteLine($"- {plugin.DisplayName} ({plugin.Code}) priority {plugin.Priority}");
            Console.WriteLine($"  Assembly: {assemblyPath}");
        }

        Console.WriteLine();
        Console.WriteLine("Formats:");
        foreach (IJdiFormat format in formats.OrderBy(format => format.DisplayName, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"- {format.DisplayName} import={format.CanImport} export={format.CanExport}");

        Console.WriteLine();
        ListTargets(options);

        Console.WriteLine();
        ListTools(options);

        Console.WriteLine();
        Console.WriteLine("Assembly search directories:");
        foreach (string directory in ConverterPluginLoader.AssemblySearchDirectories)
            Console.WriteLine($"- {directory}");

        if (ConverterPluginLoader.LoadWarnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Load warnings:");
            foreach (string warning in ConverterPluginLoader.LoadWarnings)
                Console.WriteLine($"- {warning}");
        }

        return 0;
    }

    public int ListTools(CliOptions options)
    {
        string? providerFilter = options.Get("provider");

        Console.WriteLine("Tools:");
        foreach (IToolProvider provider in toolProviders
            .OrderBy(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(providerFilter) &&
                !provider.ProviderCode.Equals(providerFilter, StringComparison.OrdinalIgnoreCase) &&
                !provider.ProviderName.Equals(providerFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ToolDefinition[] tools = [.. provider.GetTools()
                .OrderBy(tool => tool.Priority)
                .ThenBy(tool => tool.DisplayName, StringComparer.OrdinalIgnoreCase)];
            if (tools.Length == 0)
                continue;

            Console.WriteLine($"{provider.ProviderName} ({provider.ProviderCode})");
            foreach (ToolDefinition tool in tools)
            {
                Console.WriteLine($"  {tool.FullCode,-28} {tool.DisplayName}");
                if (!string.IsNullOrWhiteSpace(tool.Description))
                    Console.WriteLine($"    {tool.Description}");

                foreach (ConversionPrompt prompt in tool.Prompts)
                {
                    string required = prompt.Required ? "required" : "optional";
                    Console.WriteLine($"    --{prompt.Id,-18} {prompt.Label} ({prompt.Kind}, {required})");
                }
            }
        }

        return 0;
    }

    public int ListTargets(CliOptions options)
    {
        ConversionTargetDefinition[] targets = ConversionTargetSelector.GetAvailableTargets(strategies);
        string? platformFilter = options.Get("platform");

        foreach (PlatformDescriptor platform in ConversionTargetSelector.GetSortedPlatforms(targets))
        {
            if (!string.IsNullOrWhiteSpace(platformFilter) &&
                !platform.PlatformCode.Equals(platformFilter, StringComparison.OrdinalIgnoreCase) &&
                !platform.DisplayName.Equals(platformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine(platform.DisplayName);
            foreach (ConversionTargetDefinition target in ConversionTargetSelector.GetSortedTargetsForPlatform(targets, platform.PlatformCode))
                Console.WriteLine($"  {target.TargetCode,-24} {ConversionTargetSelector.FormatTargetLabel(target, includeSupportStatus: true)}");
        }

        return 0;
    }
}