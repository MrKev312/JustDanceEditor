using JustDanceEditor.Conversion.Abstractions.Prompts;

namespace JustDanceEditor.AppHost;

public sealed class StaticConversionInteraction(PromptAnswerSet answers) : IConversionInteraction
{
    private readonly PromptAnswerSet _answers = answers;

    public ValueTask<PromptAnswerSet> AskAsync(ConversionPromptSet promptSet, CancellationToken cancellationToken = default)
    {
        PromptAnswerSet response = new();

        foreach (ConversionPrompt prompt in promptSet.Prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_answers.TryGetString(prompt.Id, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                response.Set(prompt.Id, value);
                continue;
            }

            if (prompt.Kind == ConversionPromptKind.Choice && prompt.Options.Count == 1)
            {
                response.Set(prompt.Id, prompt.Options[0].Value);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(prompt.DefaultValue))
            {
                response.Set(prompt.Id, prompt.DefaultValue);
                continue;
            }

            if (!prompt.Required)
            {
                response.Set(prompt.Id, string.Empty);
                continue;
            }

            string options = prompt.Options.Count == 0
                ? string.Empty
                : $" Options: {string.Join(", ", prompt.Options.Select(option => $"{option.Value} ({option.Label})"))}.";

            throw new InvalidOperationException($"Missing required answer '{prompt.Id}' for prompt '{prompt.Label}'.{options}");
        }

        return ValueTask.FromResult(response);
    }
}