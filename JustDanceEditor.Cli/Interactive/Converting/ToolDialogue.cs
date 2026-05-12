using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.Conversion.Abstractions;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli.Interactive.Converting;

internal sealed class ToolDialogue(
    IEnumerable<IToolProvider> toolProviders,
    IConversionInteraction interaction,
    ILogger<ToolDialogue> logger)
{
    private readonly IToolProvider[] _providers = [.. toolProviders];
    private readonly IConversionInteraction _interaction = interaction;
    private readonly ILogger<ToolDialogue> _logger = logger;

    public void Start()
    {
        ToolProviderEntry[] providers = GetProviderEntries();
        if (providers.Length == 0)
        {
            Console.WriteLine("No tools are available in this build.");
            return;
        }

        int providerSelection = Question.Ask([.. providers.Select(provider => provider.Provider.ProviderName)], 0, "Select a tool provider");
        ToolProviderEntry selectedProvider = providers[providerSelection];

        int toolSelection = Question.Ask([.. selectedProvider.Tools.Select(tool => tool.DisplayName)], 0, "Select a tool");
        ToolDefinition selectedTool = selectedProvider.Tools[toolSelection];

        ExecuteTool(selectedProvider.Provider, selectedTool);
    }

    private void ExecuteTool(IToolProvider provider, ToolDefinition tool)
    {
        try
        {
            PromptAnswerSet answers = tool.Prompts.Count == 0
                ? new PromptAnswerSet()
                : _interaction.AskAsync(new ConversionPromptSet(tool.FullCode, tool.DisplayName, tool.Prompts)).GetAwaiter().GetResult();

            provider.ExecuteAsync(new ToolExecutionContext(tool, answers, _interaction)).GetAwaiter().GetResult();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n{tool.DisplayName} completed successfully.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n{tool.DisplayName} failed: {ex.Message}");
            Console.ResetColor();
            _logger.LogError(ex, "Tool '{ToolCode}' failed: {Message}", tool.FullCode, ex.Message);
        }
    }

    private ToolProviderEntry[] GetProviderEntries()
    {
        return [.. _providers
            .Select(provider => new ToolProviderEntry(
                provider,
                [.. provider.GetTools()
                    .OrderBy(tool => tool.Priority)
                    .ThenBy(tool => tool.DisplayName, StringComparer.OrdinalIgnoreCase)]))
            .Where(entry => entry.Tools.Length > 0)
            .OrderBy(entry => entry.Provider.Priority)
            .ThenBy(entry => entry.Provider.ProviderName, StringComparer.OrdinalIgnoreCase)];
    }

    private sealed record ToolProviderEntry(IToolProvider Provider, ToolDefinition[] Tools);
}
