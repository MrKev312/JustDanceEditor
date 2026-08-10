using JustDanceEditor.AppHost;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;

using System.CommandLine;

namespace JustDanceEditor.Cli;

internal sealed class CliToolRunner(
    IReadOnlyList<IToolProvider> toolProviders,
    IConversionInteraction interactiveInteraction,
    ToolDialogue toolDialogue)
{
    public int RunToolCommand(ParseResult parseResult, CliCommandSymbols symbols)
    {
        CliOptions options = CliCommandBuilder.CreateOptions(parseResult, symbols);
        string? toolCode = parseResult.GetValue(symbols.ToolCodeArgument);
        toolCode ??= options.Get("id") ?? options.Get("tool");
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            if (options.Headless)
                throw new ArgumentException("Missing tool code. Use 'tool <provider.tool>' or '--id <provider.tool>'.");

            toolDialogue.Start();
            return 0;
        }

        return RunToolByCode(toolCode, options);
    }

    public int RunToolByCode(string toolCode, CliOptions options)
    {
        (IToolProvider provider, ToolDefinition tool) = ResolveTool(toolCode);
        PromptAnswerSet seedAnswers = BuildToolAnswers(options, tool);
        IConversionInteraction interaction = CreateInteraction(options, seedAnswers);
        PromptAnswerSet promptAnswers = tool.Prompts.Count == 0
            ? seedAnswers
            : interaction.AskAsync(new ConversionPromptSet(tool.FullCode, tool.DisplayName, tool.Prompts)).AsTask().GetAwaiter().GetResult();
        PromptAnswerSet answers = MergeAnswers(seedAnswers, promptAnswers);

        provider.ExecuteAsync(new ToolExecutionContext(tool, answers, interaction)).GetAwaiter().GetResult();
        Console.WriteLine($"{tool.DisplayName} completed.");
        return 0;
    }

    public static PromptAnswerSet MergeAnswers(PromptAnswerSet seedAnswers, PromptAnswerSet promptAnswers)
    {
        PromptAnswerSet merged = new(seedAnswers.Answers);
        foreach (KeyValuePair<string, string> answer in promptAnswers.Answers)
            merged.Set(answer.Key, answer.Value);

        return merged;
    }

    private static PromptAnswerSet BuildToolAnswers(CliOptions options, ToolDefinition tool)
    {
        PromptAnswerSet answers = new();

        foreach (string answer in options.GetMany("answer"))
        {
            int equals = answer.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException($"Invalid --answer '{answer}'. Expected --answer key=value.");

            answers.Set(answer[..equals], answer[(equals + 1)..]);
        }

        if (options.Get("input") is { } inputPath)
            answers.Set(ConversionPromptIds.InputPath, inputPath);
        if (options.Get("output") is { } outputPath)
            answers.Set(ConversionPromptIds.OutputPath, outputPath);
        if (options.HasFlag("force"))
            answers.Set(ConversionPromptIds.Force, options.Get("force") ?? "true");

        foreach (ConversionPrompt prompt in tool.Prompts)
        {
            if (options.Get(prompt.Id) is { } promptValue)
            {
                answers.Set(prompt.Id, promptValue);
                continue;
            }

            if (options.HasFlag(prompt.Id))
                answers.Set(prompt.Id, "true");
        }

        return answers;
    }

    private IConversionInteraction CreateInteraction(CliOptions options, PromptAnswerSet seedAnswers)
    {
        return options.Headless
            ? new StaticConversionInteraction(seedAnswers)
            : new SeededConversionInteraction(seedAnswers, interactiveInteraction);
    }

    private (IToolProvider Provider, ToolDefinition Tool) ResolveTool(string toolCode)
    {
        (IToolProvider Provider, ToolDefinition Tool)[] tools = [.. toolProviders
            .SelectMany(provider => provider.GetTools().Select(tool => (provider, tool)))];

        (IToolProvider Provider, ToolDefinition Tool)[] exactMatches = [.. tools.Where(item =>
            item.Tool.FullCode.Equals(toolCode, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Length == 1)
            return exactMatches[0];

        (IToolProvider Provider, ToolDefinition Tool)[] shortMatches = [.. tools.Where(item =>
            item.Tool.ToolCode.Equals(toolCode, StringComparison.OrdinalIgnoreCase) ||
            item.Tool.DisplayName.Equals(toolCode, StringComparison.OrdinalIgnoreCase))];
        if (shortMatches.Length == 1)
            return shortMatches[0];

        if (exactMatches.Length + shortMatches.Length > 1)
            throw new ArgumentException($"Tool '{toolCode}' is ambiguous. Use the full provider.tool code.");

        throw new ArgumentException($"Unknown tool '{toolCode}'. Run 'tools' to list available tools.");
    }
}