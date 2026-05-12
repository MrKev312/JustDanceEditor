using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Cli.Interactive.Converting;

internal sealed class ConsoleConversionInteraction : IConversionInteraction
{
    public ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(promptSet.Title))
            Console.WriteLine(promptSet.Title);

        PromptAnswerSet answers = new();
        foreach (ConversionPrompt prompt in promptSet.Prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            answers.Set(prompt.Id, Ask(prompt));
        }

        return ValueTask.FromResult(answers);
    }

    private static string Ask(ConversionPrompt prompt) => prompt.Kind switch
    {
        ConversionPromptKind.Boolean => Question.AskYesNo(prompt.Label) ? "true" : "false",
        ConversionPromptKind.Choice => AskChoice(prompt),
        ConversionPromptKind.MultiChoice => AskMultiChoice(prompt),
        ConversionPromptKind.Integer => Question.AskNumber(prompt.Label).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ConversionPromptKind.FilePath => AskPath(prompt, file: true),
        ConversionPromptKind.FolderPath => AskPath(prompt, file: false),
        _ => AskText(prompt)
    };

    private static string AskChoice(ConversionPrompt prompt)
    {
        if (prompt.Options.Count == 0)
            throw new InvalidOperationException($"Prompt '{prompt.Id}' does not define any options.");

        string[] labels = [.. prompt.Options.Select(option => option.Label)];
        int selection = Question.Ask(labels, 0, prompt.Label);
        return prompt.Options[selection].Value;
    }

    private static string AskMultiChoice(ConversionPrompt prompt)
    {
        if (prompt.Options.Count == 0)
            throw new InvalidOperationException($"Prompt '{prompt.Id}' does not define any options.");

        Console.WriteLine(prompt.Label);
        for (int i = 0; i < prompt.Options.Count; i++)
            Console.WriteLine($"{i})  {prompt.Options[i].Label}");

        Console.Write("Select one or more options separated by commas: ");
        string? raw = Console.ReadLine();
        while (string.IsNullOrWhiteSpace(raw))
        {
            Console.Write("Selection cannot be empty. Select one or more options separated by commas: ");
            raw = Console.ReadLine();
        }

        int[] selections = [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out int index) ? index : -1)
            .Where(index => index >= 0 && index < prompt.Options.Count)
            .Distinct()];

        if (selections.Length == 0)
            throw new InvalidOperationException("No valid option was selected.");

        return string.Join(';', selections.Select(index => prompt.Options[index].Value));
    }

    private static string AskPath(ConversionPrompt prompt, bool file)
    {
        if (!string.IsNullOrWhiteSpace(prompt.DefaultValue))
        {
            string expandedDefault = prompt.DefaultValue;
            bool defaultExists = file ? File.Exists(expandedDefault) : Directory.Exists(expandedDefault);
            if (!prompt.MustExist || defaultExists)
            {
                Console.Write($"{prompt.Label} (default: {expandedDefault}): ");
                string? entered = Console.ReadLine()?.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(entered))
                    return expandedDefault;

                return entered;
            }
        }

        return file
            ? Question.AskFile(prompt.Label, prompt.MustExist)
            : Question.AskFolder(prompt.Label, prompt.MustExist);
    }

    private static string AskText(ConversionPrompt prompt)
    {
        Console.Write($"{prompt.Label}: ");
        string? value = Console.ReadLine();
        while (prompt.Required && string.IsNullOrWhiteSpace(value))
        {
            Console.Write("Value cannot be empty. Try again: ");
            value = Console.ReadLine();
        }

        return string.IsNullOrWhiteSpace(value)
            ? prompt.DefaultValue ?? string.Empty
            : value;
    }
}
